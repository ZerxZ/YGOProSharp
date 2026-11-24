using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using YGOProSharp.Abstractions.Logging;
using YGOProSharp.Abstractions.Ocg.Enums;
using YGOProSharp.Core;
using YGOProSharp.Core.Cards;
using YGOProSharp.Persistence;
using YGOProSharp.Persistence.Caching;
using YGOProSharp.Persistence.Entities;
using YGOProSharp.Server;
using YGOProSharp.Web.Services;

namespace YGOProSharp.Web;

/// <summary>
/// 独立端口运行的前台玩家与社区用户面板宿主（默认 <c>7913</c>）：
/// <list type="bullet">
/// <item>与后台管理面板（<c>7912</c>）及游戏协议服（<c>7911</c>）完全独立端口监听，路由表零后台管理接口，消除越权攻击面；</item>
/// <item>所有房间、战报响应强制剥离玩家 IP 与私密房入房口令，严格遵守战争迷雾；</item>
/// <item>提供联机状态速查、公开对局围观看板、云录像 <c>R#&lt;id&gt;</c> 检索与 <c>.yrp</c> 下载、DIY 卡图鉴与 <c>.ypk</c> 扩展包下载、盲盒试抽、选手赛事交牌、<c>vpass</c> 昵称保护注册及社区 DIY 提案投递。</item>
/// </list>
/// </summary>
public sealed class UserPortalHost : IAsyncDisposable, IDisposable
{
    private readonly ILogger<UserPortalHost> _logger = AppLog.CreateLogger<UserPortalHost>();
    private readonly YGOProSharpServerHost _serverHost;
    private readonly OperationalStore _store;
    private readonly OperationalServerCoordinator _coordinator;
    private readonly UserPortalOptions _options;
    private readonly DiyPackageManager _diyManager;
    private readonly ConcurrentDictionary<Guid, WebSocket> _publicSubscribers = new();
    private readonly DateTime _startedAtUtc = DateTime.UtcNow;

    private WebApplication? _app;
    private bool _disposed;

    public UserPortalHost(
        YGOProSharpServerHost serverHost,
        OperationalStore store,
        OperationalServerCoordinator? coordinator = null,
        UserPortalOptions? options = null)
    {
        _serverHost = serverHost ?? throw new ArgumentNullException(nameof(serverHost));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _coordinator = coordinator ?? new OperationalServerCoordinator(store);
        _options = options ?? new UserPortalOptions();

        string diyRoot = Path.Combine(Path.GetFullPath(_serverHost.Server.Options.RootPath), "diy");
        _diyManager = new DiyPackageManager(diyRoot, new SqliteCardDatabaseManager());

        _coordinator.OnPublicEventPublished += HandlePublicEvent;
    }

    public IReadOnlyCollection<string> BoundAddresses { get; private set; } = [];

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_app is not null)
            return;

        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();

        string bindHost = string.IsNullOrWhiteSpace(_options.Host) ? "127.0.0.1" : _options.Host.Trim();
        builder.WebHost.UseKestrel(kestrel =>
        {
            if (IPAddress.TryParse(bindHost, out IPAddress? ip))
                kestrel.Listen(ip, _options.Port);
            else
                kestrel.ListenAnyIP(_options.Port);
        });

        builder.Services.ConfigureHttpJsonOptions(json =>
        {
            json.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            json.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });

        WebApplication app = builder.Build();
        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(25)
        });

        string? staticRoot = ResolveStaticFilesDirectory(_options.StaticFilesRoot);
        PhysicalFileProvider? fileProvider = staticRoot is not null ? new PhysicalFileProvider(staticRoot) : null;
        if (fileProvider is not null)
        {
            app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });
            app.UseStaticFiles(new StaticFileOptions { FileProvider = fileProvider });
        }

        MapPortalEndpoints(app, staticRoot);

        await app.StartAsync(cancellationToken).ConfigureAwait(false);
        _app = app;

        IServerAddressesFeature? addrFeature = app.Services.GetService<IServer>()?.Features.Get<IServerAddressesFeature>();
        BoundAddresses = addrFeature?.Addresses.ToArray() ?? [$"http://{bindHost}:{_options.Port}"];
        _logger.LogInformation("User portal listening on {Addresses} (GameServerPort={GamePort}).", string.Join(", ", BoundAddresses), _serverHost.Server.Options.Port);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        foreach ((Guid id, WebSocket ws) in _publicSubscribers.ToArray())
        {
            _publicSubscribers.TryRemove(id, out _);
            try
            {
                if (ws.State == WebSocketState.Open)
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Portal shutting down", cancellationToken).ConfigureAwait(false);
            }
            catch
            {
            }
            finally
            {
                ws.Dispose();
            }
        }

        WebApplication? app = _app;
        _app = null;
        if (app is not null)
        {
            try
            {
                await app.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await app.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private void MapPortalEndpoints(WebApplication app, string? staticRoot)
    {
        app.MapGet("/api/surface", () => Results.Ok(new
        {
            surface = "portal",
            gamePort = _serverHost.Server.Options.Port,
            publicGameHost = _options.PublicGameHost,
            portalPort = _options.Port
        }));

        app.MapGet("/healthz", async (CancellationToken ct) =>
        {
            DatabaseHealthSnapshot dbHealth = await _store.Database.CheckHealthAsync(ct).ConfigureAwait(false);
            bool ok = _serverHost.IsRunning && dbHealth.IsHealthy;
            return Results.Json(new
            {
                surface = "portal",
                status = ok ? "healthy" : "degraded",
                gameServerRunning = _serverHost.IsRunning,
                maintenanceMode = _serverHost.Server.IsMaintenanceMode
            }, statusCode: ok ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
        });

        // 1. 服务器实时公开概览与房间口令速查
        app.MapGet("/api/portal/overview", () =>
        {
            CoreServer srv = _serverHost.Server;
            IReadOnlyList<DiyPackageManifest> enabledDiy = _diyManager.ListPackages(enabledOnly: true);

            return Results.Ok(new
            {
                uptimeSeconds = (long)(DateTime.UtcNow - _startedAtUtc).TotalSeconds,
                isRunning = srv.IsRunning,
                maintenanceMode = srv.IsMaintenanceMode,
                maintenanceReason = srv.MaintenanceReason,
                gamePort = srv.Options.Port,
                publicGameHost = _options.PublicGameHost,
                multiRoom = srv.IsMultiRoom,
                clientCount = srv.ClientCount,
                roomCount = srv.RoomCount,
                catalogVersion = srv.ReloadableCards.CatalogVersion,
                presetDeckCount = srv.PresetDecks.Count,
                enabledDiyPackageCount = enabledDiy.Count,
                ruleGuides = new object[]
                {
                    new { prefix = "M#房间名", title = "三局两胜比赛 (Match)", desc = "标准 BO3 备牌局" },
                    new { prefix = "2V2#房间名", title = "双打模式 (Tag Duel)", desc = "4 人 16000 LP 组队轮转，大厅可输入 /ai fill 召唤 AI 队友" },
                    new { prefix = "RD#房间名", title = "随机盲盒对决", desc = "无需组卡直接准备，服务端从盲盒池随机派发主题卡组" },
                    new { prefix = "RDM#房间名", title = "镜像盲盒对决", desc = "双方（或 2v2 两队）使用完全对称的随机卡组同台竞技" },
                    new { prefix = "CHAOS#房间名", title = "平衡混沌抽卡", desc = "按黄金怪魔陷配比从全卡库实时生成 40+15 随机卡组" },
                    new { prefix = "DIYRD#房间名", title = "DIY 主题随机", desc = "优先抽取含本服 DIY 新卡的主题预组" },
                    new { prefix = "R#录像编号", title = "云录像直连回放", desc = "在客户端房间密码框输入 R#1 即可免下载直接观看历史回放" }
                }
            });
        });

        // 2. 公开大厅房间列表与观战详情（100% 剥离玩家 IP 与私密口令）
        app.MapGet("/api/portal/rooms", async (CancellationToken ct) =>
        {
            PortalRoomSummaryDto[] rooms = await _store.Cache.GetOrCreateAsync(
                "portal",
                "public_rooms",
                _ => ValueTask.FromResult(BuildPublicRoomSummaries()),
                ttl: TimeSpan.FromSeconds(1),
                l1Ttl: TimeSpan.FromSeconds(1),
                cancellationToken: ct).ConfigureAwait(false);

            return Results.Ok(new { rooms });
        });

        app.MapGet("/api/portal/rooms/{roomName}", (string roomName) =>
        {
            if (!_serverHost.Server.TryGetRoom(roomName, out Game? game) || game is null)
                return Results.NotFound(new { message = $"房间 '{roomName}' 不存在或已结束。" });

            RoomActivityState? activity = _coordinator.TryGetRoomState(game.RoomName);
            return Results.Ok(new
            {
                room = new
                {
                    summary = BuildSinglePublicRoomSummary(game),
                    chats = activity?.GetChats() ?? [],
                    publicEvents = activity?.GetPublicEvents() ?? []
                }
            });
        });

        // 3. 历史对局战报与云录像下载（剥离 IP；进行中不会入库，仅展示已结束对局）
        app.MapGet("/api/portal/replays", async (int? limit, string? player, CancellationToken ct) =>
        {
            IReadOnlyList<DuelRecordEntity> list = await _store.ListDuelRecordsAsync(
                Math.Clamp(limit ?? 30, 1, 100),
                beforeId: null,
                playerFilter: player,
                cancellationToken: ct).ConfigureAwait(false);

            return Results.Ok(new
            {
                replays = list.Select(static r => new
                {
                    r.Id,
                    replayCode = $"R#{r.Id}",
                    r.RoomName,
                    r.Mode,
                    r.MasterRule,
                    r.RandomDeckMode,
                    r.IsSuddenDeath,
                    r.TurnCount,
                    r.WinnerTeam,
                    r.EndedAt,
                    hasYrp = !string.IsNullOrWhiteSpace(r.YrpPath),
                    players = r.Players.OrderBy(static p => p.SeatIndex).Select(static p => new
                    {
                        p.SeatIndex,
                        p.TeamId,
                        p.PlayerName,
                        p.SeatType,
                        p.IsWinner
                    })
                })
            });
        });

        app.MapGet("/api/portal/replays/{id:long}.yrp", async (long id, CancellationToken ct) =>
        {
            byte[]? yrp = await _store.GetReplayYrpBytesAsync(id, ct).ConfigureAwait(false);
            return yrp is null
                ? Results.NotFound(new { message = $"录像 R#{id} 不存在。" })
                : Results.File(yrp, "application/octet-stream", $"replay_{id}.yrp");
        });

        // 4. 卡库检索、DIY 扩展包图鉴与客户端 .ypk 下载、混沌随机试抽
        app.MapGet("/api/portal/cards/search", async (string? q, int? limit, bool? diyOnly, CancellationToken ct) =>
        {
            string queryText = q?.Trim() ?? string.Empty;
            int max = Math.Clamp(limit ?? 36, 1, 80);
            long catalogVer = _serverHost.Server.ReloadableCards.CatalogVersion;
            string cacheKey = $"portal:v{catalogVer}:q:{queryText.ToLowerInvariant()}:diy:{diyOnly == true}:l:{max}";

            PortalCardItemDto[] items = await _store.Cache.GetOrCreateAsync(
                "cards",
                cacheKey,
                _ => ValueTask.FromResult(SearchPublicCards(queryText, max, diyOnly == true)),
                ttl: TimeSpan.FromMinutes(30),
                l1Ttl: TimeSpan.FromMinutes(10),
                cancellationToken: ct).ConfigureAwait(false);

            return Results.Ok(new { catalogVersion = catalogVer, cards = items });
        });

        app.MapGet("/api/portal/diy/packages", () =>
        {
            IReadOnlyList<DiyPackageManifest> enabled = _diyManager.ListPackages(enabledOnly: true);
            var items = enabled.Select(p =>
            {
                IReadOnlyList<DiyCardSummary> cards = _diyManager.ListPackageCards(p.Id);
                return new
                {
                    p.Id,
                    p.Name,
                    p.Version,
                    p.Author,
                    p.UpdatedAtUtc,
                    cardCount = cards.Count,
                    sampleCards = cards.Take(12).Select(static c => new
                    {
                        c.Card.Id,
                        c.Card.Name,
                        c.Card.Desc,
                        c.HasImage
                    })
                };
            });

            return Results.Ok(new { packages = items });
        });

        app.MapGet("/api/portal/diy/packages/{id}/export.ypk", (string id) =>
        {
            if (!DiyPackageManager.IsValidPackageId(id))
                return Results.BadRequest(new { message = "非法包 ID。" });

            DiyPackageManifest? pkg = _diyManager.ListPackages(enabledOnly: true)
                .FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
            if (pkg is null)
                return Results.NotFound(new { message = "未找到已启用的 DIY 扩展包。" });

            byte[] ypk = _diyManager.ExportPackageAsYpk(pkg.Id);
            return Results.File(ypk, "application/zip", $"{pkg.Id}.ypk");
        });

        app.MapGet("/api/portal/diy/packages/{id}/cards/{cardId:int}/image", (HttpContext http, string id, int cardId) =>
        {
            if (!DiyPackageManager.IsValidPackageId(id) || cardId <= 0)
                return Results.BadRequest();

            var img = _diyManager.TryReadCardImage(id, cardId);
            if (img is null)
                return Results.NotFound();

            http.Response.Headers.CacheControl = "public, max-age=300";
            return Results.Bytes(img.Value.Bytes, img.Value.ContentType);
        });

        app.MapPost("/api/portal/decks/chaos-preview", (ChaosPreviewRequestDto? body) =>
        {
            int mainSize = Math.Clamp(body?.MainSize ?? 40, 20, 60);
            bool pureChaos = body?.PureChaos == true;
            bool diyOnly = body?.DiyOnly == true;

            RandomDeckGenerator generator = new(_serverHost.Server.CardRepository);
            Deck generated = generator.GenerateChaosDeck(
                banlist: null,
                ocg: !diyOnly,
                tcg: !diyOnly,
                allowCustom: true,
                options: new ChaosDraftOptions
                {
                    MainDeckSize = mainSize,
                    ExtraDeckSize = 15,
                    BalancedRatio = !pureChaos,
                    PreferDiyCards = diyOnly
                },
                random: Random.Shared);

            INamedCardRepository? namedRepo = _options.NamedCardRepositoryProvider?.Invoke();
            return Results.Ok(new
            {
                mainCount = generated.Main.Count,
                extraCount = generated.Extra.Count,
                sampleMainCards = generated.Main.Take(20).Select(id => new
                {
                    id,
                    name = namedRepo is not null && namedRepo.TryGetCard(id, out NamedCard nc) ? nc.Name : $"#{id}"
                }),
                ydk = YdkSerializer.Serialize(generated, "PlayerChaosPreview")
            });
        });

        // 5. 玩家自助服务（滑动窗口防刷限流）：vpass 昵称保护、赛事选手交牌、社区 DIY 投稿提案
        app.MapPost("/api/portal/player/vpass", async (HttpContext http, PortalVpassRequestDto body, CancellationToken ct) =>
        {
            string ip = GetRemoteIp(http) ?? "unknown";
            RateLimitDecision rl = await _store.Cache.CheckSlidingWindowAsync($"portal_vpass:{ip}", 10, TimeSpan.FromMinutes(5), ct).ConfigureAwait(false);
            if (!rl.Allowed)
                return Results.Json(new { message = "操作过于频繁，请稍后再试。" }, statusCode: StatusCodes.Status429TooManyRequests);

            string playerName = body.PlayerName?.Trim() ?? string.Empty;
            string vpass = body.Vpass?.Trim() ?? string.Empty;
            if (playerName.Length < 1 || playerName.Length > 20 || vpass.Length < 4 || vpass.Length > 32)
                return Results.BadRequest(new { message = "游戏昵称需 1~20 字符，保护口令需 4~32 字符。" });

            (bool allowed, string? reason) = await _store.VerifyPlayerVpassAsync(playerName, vpass, ct).ConfigureAwait(false);
            if (!allowed)
                return Results.BadRequest(new { success = false, message = reason ?? "该昵称已被他人绑定保护，口令不匹配。" });

            return Results.Ok(new
            {
                success = true,
                loginFormat = $"{playerName}${vpass}",
                message = $"昵称保护已生效！进入游戏时请将客户端昵称填写为：{playerName}${vpass}"
            });
        });

        app.MapPost("/api/portal/tournament-decks", async (HttpContext http, UpsertTournamentDeckDto body, CancellationToken ct) =>
        {
            string ip = GetRemoteIp(http) ?? "unknown";
            RateLimitDecision rl = await _store.Cache.CheckSlidingWindowAsync($"portal_tdeck:{ip}", 8, TimeSpan.FromMinutes(5), ct).ConfigureAwait(false);
            if (!rl.Allowed)
                return Results.Json(new { message = "提交过于频繁，请稍后再试。" }, statusCode: StatusCodes.Status429TooManyRequests);

            if (string.IsNullOrWhiteSpace(body.PlayerName) || string.IsNullOrWhiteSpace(body.YdkContent))
                return Results.BadRequest(new { message = "请填写参赛昵称并粘贴完整的 .ydk 卡组内容。" });

            try
            {
                YdkDeckData parsed = YdkSerializer.Parse(body.YdkContent, body.PlayerName);
                if (parsed.Main.Count < 40)
                    return Results.BadRequest(new { message = $"主卡组至少需要 40 张卡片（当前解析到 {parsed.Main.Count} 张）。" });

                TournamentDeckEntity saved = await _store.SubmitPlayerTournamentDeckAsync(
                    string.IsNullOrWhiteSpace(body.TournamentTag) ? "default" : body.TournamentTag.Trim(),
                    body.PlayerName.Trim(),
                    body.YdkContent,
                    parsed.Main.Count,
                    parsed.Extra.Count,
                    parsed.Side.Count,
                    ip,
                    ct).ConfigureAwait(false);

                return Results.Ok(new
                {
                    success = true,
                    playerName = saved.PlayerName,
                    tournamentTag = saved.TournamentTag,
                    deckHash = saved.DeckHash,
                    mainCount = saved.MainCount,
                    extraCount = saved.ExtraCount,
                    sideCount = saved.SideCount
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        });

        app.MapPost("/api/portal/diy/proposals", async (HttpContext http, PortalSubmitProposalDto body, CancellationToken ct) =>
        {
            string ip = GetRemoteIp(http) ?? "unknown";
            RateLimitDecision rl = await _store.Cache.CheckSlidingWindowAsync($"portal_prop:{ip}", 5, TimeSpan.FromMinutes(10), ct).ConfigureAwait(false);
            if (!rl.Allowed)
                return Results.Json(new { message = "投稿过于频繁，请稍后再试。" }, statusCode: StatusCodes.Status429TooManyRequests);

            string pkgId = string.IsNullOrWhiteSpace(body.PackageId) ? "community-diy" : body.PackageId.Trim();
            if (!DiyPackageManager.IsValidPackageId(pkgId))
                return Results.BadRequest(new { message = "目标包 ID 格式不合法。" });

            if (body.Card?.Id is null or <= 0 || string.IsNullOrWhiteSpace(body.Card.Name))
                return Results.BadRequest(new { message = "请填写有效的正整数卡密与卡片名称。" });

            if (!string.IsNullOrEmpty(body.LuaScript))
            {
                LuaScriptSecurityAudit audit = DiyPackageManager.AuditLuaScript(body.LuaScript);
                if (!audit.IsSafe)
                    return Results.BadRequest(new { message = "Lua 脚本未通过安全预检：" + string.Join("; ", audit.Violations) });
            }

            DiyProposalPayloadDto payload = new(
                Cards: [new ProposedCardChangeDto(body.Card, body.LuaScript)],
                Scripts: null,
                Decks: null,
                StringsConf: null);

            string author = string.IsNullOrWhiteSpace(body.AuthorName) ? "community_player" : body.AuthorName.Trim();
            DiyProposalEntity created = await _store.CreateDiyProposalAsync(
                pkgId,
                $"[玩家投稿:{author}] {body.Card.Name} (#{body.Card.Id})",
                JsonSerializer.Serialize(payload),
                proposerTokenId: null,
                proposerName: author,
                actorType: "anonymous",
                actorId: null,
                ip: ip,
                cancellationToken: ct).ConfigureAwait(false);

            return Results.Ok(new
            {
                success = true,
                proposalId = created.Id,
                status = created.Status,
                message = "投稿已进入待审队列，待服主在管理后台沙盒验证并批准后即可全服热重载生效！"
            });
        });

        // 6. 公开只读实时事件 WebSocket（仅推送公开房间与热重载通知）
        app.Map("/ws/public", async (HttpContext http, CancellationToken ct) =>
        {
            if (!http.WebSockets.IsWebSocketRequest)
            {
                http.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            using WebSocket ws = await http.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
            Guid subId = Guid.NewGuid();
            _publicSubscribers[subId] = ws;

            try
            {
                byte[] welcome = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type = "connected", surface = "portal" }));
                await ws.SendAsync(welcome, WebSocketMessageType.Text, true, ct).ConfigureAwait(false);

                byte[] recvBuf = new byte[1024];
                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    WebSocketReceiveResult res = await ws.ReceiveAsync(recvBuf, ct).ConfigureAwait(false);
                    if (res.MessageType == WebSocketMessageType.Close)
                        break;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (WebSocketException)
            {
            }
            finally
            {
                _publicSubscribers.TryRemove(subId, out _);
            }
        });

        if (staticRoot is not null)
        {
            string indexHtml = Path.Combine(staticRoot, "index.html");
            app.MapFallback(async (HttpContext http) =>
            {
                if (http.Request.Path.StartsWithSegments("/api") || http.Request.Path.StartsWithSegments("/ws"))
                {
                    http.Response.StatusCode = StatusCodes.Status404NotFound;
                    await http.Response.WriteAsJsonAsync(new { error = "not_found", message = "前台用户面板不存在该接口。" });
                    return;
                }

                http.Response.ContentType = "text/html; charset=utf-8";
                await http.Response.SendFileAsync(indexHtml);
            });
        }
    }

    private PortalRoomSummaryDto[] BuildPublicRoomSummaries()
    {
        IReadOnlyList<RoomSnapshot> snapshots = _serverHost.GetRooms();
        List<PortalRoomSummaryDto> list = new(snapshots.Count);
        foreach (RoomSnapshot snap in snapshots)
        {
            if (_serverHost.Server.TryGetRoom(snap.RoomName, out Game? g) && g is not null)
                list.Add(BuildSinglePublicRoomSummary(g));
        }
        return list.ToArray();
    }

    private static PortalRoomSummaryDto BuildSinglePublicRoomSummary(Game g)
    {
        RoomSnapshot snap = g.CreateSnapshot();
        return new PortalRoomSummaryDto(
            snap.RoomName,
            snap.State.ToString(),
            snap.Mode,
            snap.Region,
            snap.MasterRule,
            snap.StartLp,
            snap.Team0Lp,
            snap.Team1Lp,
            snap.TurnCount,
            g.DuelCount,
            snap.DuelistCount,
            snap.MaxDuelists,
            snap.ObserverCount,
            snap.HostPlayerName,
            snap.RandomDeckMode.ToString(),
            snap.IsSuddenDeath,
            g.Banlist?.Name,
            g.Players.Select((p, idx) => new PortalRoomSlotDto(
                idx,
                g.IsTag ? (idx >= 2 ? 1 : 0) : idx,
                p?.Name,
                idx < g.IsReady.Length && g.IsReady[idx],
                p is not null && g.IsHost(p),
                p?.SeatOrigin ?? "empty")).ToArray());
    }

    private PortalCardItemDto[] SearchPublicCards(string query, int limit, bool diyOnly)
    {
        INamedCardRepository? namedRepo = _options.NamedCardRepositoryProvider?.Invoke();
        bool isNumeric = int.TryParse(query, out int exactCode);
        List<PortalCardItemDto> results = new(limit);

        if (namedRepo is not null)
        {
            foreach (NamedCard nc in namedRepo.GetAllCards())
            {
                bool isDiy = (nc.Ot & 0x4) != 0;
                if (diyOnly && !isDiy) continue;

                bool match = string.IsNullOrEmpty(query) ||
                             (isNumeric && nc.Id == exactCode) ||
                             nc.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                             nc.Description.Contains(query, StringComparison.OrdinalIgnoreCase);
                if (!match) continue;

                results.Add(new PortalCardItemDto(
                    nc.Id,
                    nc.Name,
                    nc.Description,
                    nc.Ot,
                    isDiy,
                    nc.Type,
                    nc.Level,
                    nc.Attack,
                    nc.Defense));
                if (results.Count >= limit) break;
            }
            if (results.Count > 0) return results.ToArray();
        }

        foreach (Card c in _serverHost.Server.CardRepository.GetAllCards())
        {
            bool isDiy = (c.Ot & 0x4) != 0;
            if (diyOnly && !isDiy) continue;
            if (!string.IsNullOrEmpty(query) && (!isNumeric || c.Id != exactCode)) continue;

            results.Add(new PortalCardItemDto(
                c.Id,
                $"Card #{c.Id}",
                string.Empty,
                c.Ot,
                isDiy,
                c.Type,
                c.Level,
                c.Attack,
                c.Defense));
            if (results.Count >= limit) break;
        }

        return results.ToArray();
    }

    private void HandlePublicEvent(string eventType, object payload)
    {
        _ = _store.Cache.FlushNamespaceAsync("portal");
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            type = eventType,
            timestampUtc = DateTime.UtcNow,
            data = payload
        }));

        foreach ((Guid id, WebSocket ws) in _publicSubscribers.ToArray())
        {
            if (ws.State != WebSocketState.Open)
            {
                _publicSubscribers.TryRemove(id, out _);
                continue;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    _publicSubscribers.TryRemove(id, out _);
                }
            });
        }
    }

    private static string? ResolveStaticFilesDirectory(string? configuredRoot)
    {
        string[] candidates =
        [
            configuredRoot ?? string.Empty,
            Path.Combine(AppContext.BaseDirectory, "wwwroot"),
            Path.Combine(Directory.GetCurrentDirectory(), "YGOProSharp.Web", "wwwroot"),
            Path.Combine(Directory.GetCurrentDirectory(), "web", "dist"),
            Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")
        ];

        foreach (string candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;
            string full = Path.GetFullPath(candidate);
            if (Directory.Exists(full) && File.Exists(Path.Combine(full, "index.html")))
                return full;
        }

        return null;
    }

    private static string? GetRemoteIp(HttpContext http)
    {
        IPAddress? addr = http.Connection.RemoteIpAddress;
        if (addr is null)
            return null;
        return addr.IsIPv4MappedToIPv6 ? addr.MapToIPv4().ToString() : addr.ToString();
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        _coordinator.OnPublicEventPublished -= HandlePublicEvent;
        await StopAsync().ConfigureAwait(false);
    }
}

public sealed record PortalVpassRequestDto(string? PlayerName, string? Vpass);
public sealed record PortalSubmitProposalDto(string? PackageId, string? AuthorName, DiyCardEntryDto? Card, string? LuaScript);

public sealed record PortalRoomSlotDto(
    int SeatIndex,
    int TeamId,
    string? PlayerName,
    bool IsReady,
    bool IsHost,
    string SeatOrigin);

public sealed record PortalRoomSummaryDto(
    string RoomName,
    string State,
    int Mode,
    int Region,
    int MasterRule,
    int StartLp,
    int Team0Lp,
    int Team1Lp,
    int TurnCount,
    int DuelCount,
    int DuelistCount,
    int MaxDuelists,
    int ObserverCount,
    string? HostPlayerName,
    string RandomDeckMode,
    bool IsSuddenDeath,
    string? BanlistName,
    IReadOnlyList<PortalRoomSlotDto> Slots);

public sealed record PortalCardItemDto(
    int Id,
    string Name,
    string Desc,
    int Ot,
    bool IsDiy,
    int Type,
    int Level,
    int Attack,
    int Defense);
