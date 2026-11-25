using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using YGOProSharp.Abstractions.Logging;
using YGOProSharp.AgentBridge;
using YGOProSharp.Core;
using YGOProSharp.Core.Cards;
using YGOProSharp.Mcp;
using YGOProSharp.Persistence;
using YGOProSharp.Persistence.Caching;
using YGOProSharp.Persistence.Entities;
using YGOProSharp.Persistence.Security;
using YGOProSharp.Protocol.Enums;
using YGOProSharp.Server;
using YGOProSharp.Web.Logging;
using YGOProSharp.Web.Services;

namespace YGOProSharp.Web;

/// <summary>
/// ASP.NET Core 控制面宿主：承载管理面板认证、五级 RBAC、IP 直播脱敏、房间裁判控制、缓存命中监控、DIY 包管理、MCP Token 签发、WebSocket 事件流及静态 SPA 托管。
/// </summary>
public sealed class WebControlHost : IAsyncDisposable, IDisposable
{
    private readonly ILogger<WebControlHost> _logger = AppLog.CreateLogger<WebControlHost>();
    private readonly YGOProSharpServerHost _serverHost;
    private readonly OperationalStore _store;
    private readonly OperationalServerCoordinator _coordinator;
    private readonly WebControlOptions _options;
    private readonly LogRingBuffer _logBuffer;
    private readonly WebEventHub _eventHub;
    private readonly DiyPackageManager _diyManager;
    private readonly McpServerEngine _mcpEngine;
    private readonly UserPortalHost? _embeddedPortal;
    private readonly DateTime _startedAtUtc = DateTime.UtcNow;

    private WebApplication? _app;
    private bool _disposed;

    public WebControlHost(
        YGOProSharpServerHost serverHost,
        OperationalStore store,
        OperationalServerCoordinator? coordinator = null,
        WebControlOptions? options = null,
        LogRingBuffer? logBuffer = null)
    {
        _serverHost = serverHost ?? throw new ArgumentNullException(nameof(serverHost));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _coordinator = coordinator ?? new OperationalServerCoordinator(store);
        _options = options ?? new WebControlOptions();
        _logBuffer = logBuffer ?? new LogRingBuffer(_options.LogRingBufferCapacity);
        _eventHub = new WebEventHub();

        string diyRoot = Path.Combine(Path.GetFullPath(_serverHost.Server.Options.RootPath), "diy");
        _diyManager = new DiyPackageManager(diyRoot, new SqliteCardDatabaseManager());
        AgentSessionManager sessionManager = _coordinator.GetOrCreateAgentSessionManager(_serverHost, _options.NamedCardRepositoryProvider);
        _mcpEngine = new McpServerEngine(new McpRuntimeDependencies
        {
            ServerHost = _serverHost,
            Store = _store,
            SessionManager = sessionManager,
            DiyManager = _diyManager,
            NamedCardRepositoryProvider = () => _options.NamedCardRepositoryProvider?.Invoke() ?? EmptyNamedCardRepository.Instance,
            LuaRuntimeProvider = _options.LuaRuntimeProvider,
            SpawnWindBotCallback = _options.WindBotSpawner
        });

        if (_options.EnableUnifiedPortal)
        {
            _embeddedPortal = new UserPortalHost(
                _serverHost,
                _store,
                _coordinator,
                new UserPortalOptions
                {
                    Host = _options.Host,
                    Port = _options.Port,
                    PublicGameHost = _options.PublicGameHost,
                    RequireHttpsCookie = _options.RequireHttpsCookie,
                    McpPortHint = _options.Port,
                    StaticFilesRoot = _options.StaticFilesRoot,
                    NamedCardRepositoryProvider = _options.NamedCardRepositoryProvider,
                    WindBotSpawner = _options.WindBotSpawner,
                    AvailableBotDecksProvider = _options.AvailableBotDecksProvider
                });
        }

        _coordinator.OnPublicEventPublished += HandleCoordinatorEvent;
        _logBuffer.OnEntryAdded += HandleBufferedLogAdded;
    }

    public LogRingBuffer LogBuffer => _logBuffer;
    public WebEventHub EventHub => _eventHub;
    public OperationalServerCoordinator Coordinator => _coordinator;
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

        MapEndpoints(app, staticRoot);

        await app.StartAsync(cancellationToken).ConfigureAwait(false);
        _app = app;

        IServerAddressesFeature? addrFeature = app.Services.GetService<IServer>()?.Features.Get<IServerAddressesFeature>();
        BoundAddresses = addrFeature?.Addresses.ToArray() ?? [$"http://{bindHost}:{_options.Port}"];
        _logger.LogInformation(
            "Web control plane listening on {Addresses} (PlayerPortal='/', AdminPanel='/admin', Mcp='/mcp', UnifiedPortal={UnifiedPortal}, StaticRoot={StaticRoot}).",
            string.Join(", ", BoundAddresses),
            _options.EnableUnifiedPortal,
            staticRoot ?? "<none>");
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
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

    private void MapEndpoints(WebApplication app, string? staticRoot)
    {
        // ==================== 0. 面板角色探测与容器健康探针 ====================
        app.MapGet("/api/surface", () => Results.Ok(new
        {
            surface = "admin",
            unifiedPortal = _options.EnableUnifiedPortal,
            defaultPage = _options.EnableUnifiedPortal ? "portal" : "admin",
            adminPath = "/admin",
            gamePort = _serverHost.Server.Options.Port,
            publicGameHost = _options.PublicGameHost,
            adminPort = _options.Port,
            portalPort = _options.PortalPort ?? (_options.EnableUnifiedPortal ? _options.Port : null)
        }));

        app.MapGet("/healthz", async (CancellationToken ct) =>
        {
            DatabaseHealthSnapshot dbHealth = await _store.Database.CheckHealthAsync(ct).ConfigureAwait(false);
            CacheStatsSnapshot cacheStats = _store.Cache.GetStats();
            bool serverOk = _serverHost.IsRunning;
            bool healthy = serverOk && dbHealth.IsHealthy;

            return Results.Json(new
            {
                status = healthy ? "healthy" : "degraded",
                uptimeSeconds = (long)(DateTime.UtcNow - _startedAtUtc).TotalSeconds,
                gameServerRunning = serverOk,
                gameServerListening = _serverHost.IsListening,
                maintenanceMode = _serverHost.Server.IsMaintenanceMode,
                database = new
                {
                    provider = dbHealth.Provider,
                    healthy = dbHealth.IsHealthy,
                    isHealthy = dbHealth.IsHealthy,
                    latencyMs = dbHealth.LatencyMs,
                    journalMode = dbHealth.JournalMode,
                    activeUserCount = dbHealth.ActiveUserCount,
                    activeBanCount = dbHealth.ActiveBanCount,
                    totalDuelRecords = dbHealth.TotalDuelRecords,
                    error = dbHealth.ErrorMessage
                },
                cache = new
                {
                    redisConfigured = cacheStats.RedisConfigured,
                    redisConnected = cacheStats.RedisConnected,
                    hitRatioPercent = cacheStats.HitRatioPercent
                }
            }, statusCode: healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
        });

        // ==================== 1. 认证、首次强制改密与会话设备 ====================
        app.MapPost("/api/v1/auth/login", async (HttpContext http, LoginRequestDto body, CancellationToken ct) =>
        {
            string? ip = GetRemoteIp(http);
            string? ua = http.Request.Headers.UserAgent.ToString();
            LoginResult res = await _store.LoginAsync(body.Username ?? string.Empty, body.Password ?? string.Empty, ip, ua, ct).ConfigureAwait(false);

            if (!res.Success || res.SessionToken is null || res.Principal is null)
            {
                int code = res.StatusCode switch
                {
                    "rate_limited" => StatusCodes.Status429TooManyRequests,
                    "locked" or "account_locked" or "account_disabled" => StatusCodes.Status423Locked,
                    _ => StatusCodes.Status401Unauthorized
                };
                return Results.Json(new
                {
                    success = false,
                    error = res.StatusCode,
                    message = res.Message,
                    remainingAttempts = res.RemainingAttempts,
                    lockedSeconds = res.LockedSeconds
                }, statusCode: code);
            }

            http.Response.Cookies.Append(_options.SessionCookieName, res.SessionToken, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Strict,
                Secure = _options.RequireHttpsCookie || http.Request.IsHttps,
                Expires = res.ExpiresAt,
                Path = "/"
            });

            return Results.Ok(new
            {
                success = true,
                message = res.Message,
                accessToken = res.SessionToken,
                csrfToken = res.CsrfToken,
                expiresAt = res.ExpiresAt,
                user = BuildUserSelfDto(res.Principal)
            });
        });

        app.MapPost("/api/v1/auth/logout", async (HttpContext http, CancellationToken ct) =>
        {
            (string? rawToken, _) = ExtractRawSessionToken(http);
            await _store.LogoutSessionAsync(rawToken, GetRemoteIp(http), ct).ConfigureAwait(false);
            http.Response.Cookies.Delete(_options.SessionCookieName, new CookieOptions { Path = "/" });
            return Results.Ok(new { success = true });
        });

        app.MapGet("/api/v1/auth/me", async (HttpContext http, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? errorResult) = await AuthenticateAsync(http, allowWhenMustChangePassword: true, ct).ConfigureAwait(false);
            if (errorResult is not null)
                return errorResult;

            return Results.Ok(new
            {
                user = BuildUserSelfDto(principal!)
            });
        });

        app.MapPost("/api/v1/auth/change-password", async (HttpContext http, ChangePasswordRequestDto body, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? errorResult) = await AuthenticateAsync(http, allowWhenMustChangePassword: true, ct).ConfigureAwait(false);
            if (errorResult is not null)
                return errorResult;

            (bool ok, string msg) = await _store.ChangeOwnPasswordAsync(
                principal!.UserId,
                principal.SessionId,
                body.CurrentPassword ?? string.Empty,
                body.NewPassword,
                GetRemoteIp(http),
                ct,
                newUsername: body.NewUsername,
                newDisplayName: body.DisplayName).ConfigureAwait(false);

            if (!ok)
                return Results.BadRequest(new { success = false, message = msg });

            AuthenticatedPrincipal? refreshed = await RevalidateCurrentPrincipalAsync(http, ct).ConfigureAwait(false);
            return Results.Ok(new
            {
                success = true,
                message = msg,
                user = refreshed is not null ? BuildUserSelfDto(refreshed) : null
            });
        });

        app.MapPost("/api/v1/auth/ws-ticket", async (HttpContext http, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? errorResult) = await AuthenticateAsync(http, allowWhenMustChangePassword: false, ct).ConfigureAwait(false);
            if (errorResult is not null)
                return errorResult;

            (string? rawSessionToken, _) = ExtractRawSessionToken(http);
            string nonce = await _store.Cache.IssueOneTimeTicketAsync(
                "ws",
                rawSessionToken!,
                TimeSpan.FromSeconds(45),
                ct).ConfigureAwait(false);

            return Results.Ok(new { ticket = nonce, expiresInSeconds = 45 });
        });

        app.MapGet("/api/v1/auth/sessions", async (HttpContext http, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? errorResult) = await AuthenticateAsync(http, allowWhenMustChangePassword: false, ct).ConfigureAwait(false);
            if (errorResult is not null)
                return errorResult;

            IReadOnlyList<PanelSessionEntity> sessions = await _store.ListSessionsAsync(principal!.UserId, ct).ConfigureAwait(false);
            return Results.Ok(new
            {
                currentSessionId = principal.SessionId,
                sessions = sessions.Select(s => new
                {
                    s.Id,
                    s.TokenPrefix,
                    ip = principal.IsStreamerOnly ? null : s.Ip,
                    s.UserAgent,
                    s.CreatedAt,
                    s.LastSeenAt,
                    s.ExpiresAt,
                    isCurrent = s.Id == principal.SessionId
                })
            });
        });

        app.MapDelete("/api/v1/auth/sessions/{id:guid}", async (HttpContext http, Guid id, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? errorResult) = await AuthenticateAsync(http, allowWhenMustChangePassword: false, ct).ConfigureAwait(false);
            if (errorResult is not null)
                return errorResult;

            bool ok = await _store.RevokeSessionByIdAsync(principal!, id, GetRemoteIp(http), ct).ConfigureAwait(false);
            return ok ? Results.Ok(new { success = true }) : Results.NotFound(new { success = false, message = "会话不存在或无权注销。" });
        });

        // ==================== 2. 系统监控、数据库健康与 Redis 缓存命中率 ====================
        app.MapGet("/api/v1/system/cache/stats", async (HttpContext http, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? _, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.DashboardView, ct).ConfigureAwait(false);
            if (err is not null) return err;

            DatabaseHealthSnapshot dbHealth = await _store.Database.CheckHealthAsync(ct).ConfigureAwait(false);
            CacheStatsSnapshot stats = _store.Cache.GetStats();
            return Results.Ok(new
            {
                database = new
                {
                    provider = dbHealth.Provider,
                    healthy = dbHealth.IsHealthy,
                    isHealthy = dbHealth.IsHealthy,
                    latencyMs = dbHealth.LatencyMs,
                    journalMode = dbHealth.JournalMode,
                    activeUserCount = dbHealth.ActiveUserCount,
                    activeBanCount = dbHealth.ActiveBanCount,
                    totalDuelRecords = dbHealth.TotalDuelRecords,
                    error = dbHealth.ErrorMessage
                },
                cache = stats
            });
        });

        app.MapPost("/api/v1/system/cache/flush", async (HttpContext http, FlushCacheRequestDto? body, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.SystemCacheFlush, ct).ConfigureAwait(false);
            if (err is not null) return err;

            string? ns = body?.Namespace;
            long generation = string.IsNullOrWhiteSpace(ns)
                ? await _store.Cache.FlushAllAsync(ct).ConfigureAwait(false)
                : await _store.Cache.FlushNamespaceAsync(ns, ct).ConfigureAwait(false);

            await _store.AppendAuditAsync("panel_user", principal!.UserId.ToString(), principal.Username, "system.cache_flush", RbacPermissions.SystemCacheFlush, ns ?? "all", GetRemoteIp(http), "ok", null, ct).ConfigureAwait(false);
            _eventHub.BroadcastEvent("cache.stats", _store.Cache.GetStats(), RbacPermissions.DashboardView);
            return Results.Ok(new { success = true, @namespace = ns ?? "all", generation });
        });

        app.MapGet("/api/v1/system/logs", async (HttpContext http, int? limit, string? level, string? search, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.DashboardView, ct).ConfigureAwait(false);
            if (err is not null) return err;

            if (principal!.IsStreamerOnly)
                return Results.Ok(new { entries = Array.Empty<BufferedLogEntry>(), redactedForStreamer = true });

            IReadOnlyList<BufferedLogEntry> entries = _logBuffer.GetRecent(limit ?? 150, level, search);
            return Results.Ok(new { entries, redactedForStreamer = false });
        });

        // ==================== 3. 游戏服总览、维护开关与热重载 ====================
        app.MapGet("/api/v1/server/status", async (HttpContext http, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? _, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.DashboardView, ct).ConfigureAwait(false);
            if (err is not null) return err;

            CoreServer srv = _serverHost.Server;
            DatabaseHealthSnapshot dbHealth = await _store.Database.CheckHealthAsync(ct).ConfigureAwait(false);
            CacheStatsSnapshot cache = _store.Cache.GetStats();
            IReadOnlyList<DiyPackageManifest> diyPackages = _diyManager.ListPackages();

            return Results.Ok(new
            {
                uptimeSeconds = (long)(DateTime.UtcNow - _startedAtUtc).TotalSeconds,
                isRunning = srv.IsRunning,
                isListening = srv.IsListening,
                port = srv.Options.Port,
                multiRoom = srv.IsMultiRoom,
                maintenanceMode = srv.IsMaintenanceMode,
                maintenanceReason = srv.MaintenanceReason,
                clientCount = srv.ClientCount,
                roomCount = srv.RoomCount,
                wsSubscribers = _eventHub.SubscriberCount,
                catalogVersion = srv.ReloadableCards.CatalogVersion,
                banlistCount = BanlistManager.Banlists.Count,
                presetDeckCount = srv.PresetDecks.Count,
                diyPackageCount = diyPackages.Count,
                enabledDiyPackageCount = diyPackages.Count(static p => p.Enabled),
                database = new
                {
                    provider = dbHealth.Provider,
                    healthy = dbHealth.IsHealthy,
                    isHealthy = dbHealth.IsHealthy,
                    latencyMs = dbHealth.LatencyMs,
                    journalMode = dbHealth.JournalMode,
                    activeUserCount = dbHealth.ActiveUserCount,
                    activeBanCount = dbHealth.ActiveBanCount,
                    totalDuelRecords = dbHealth.TotalDuelRecords,
                    error = dbHealth.ErrorMessage
                },
                cache
            });
        });

        app.MapPost("/api/v1/server/maintenance", async (HttpContext http, MaintenanceRequestDto body, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.ServerStop, ct).ConfigureAwait(false);
            if (err is not null) return err;

            _serverHost.Server.SetMaintenanceMode(body.Enabled, body.Reason);
            await _store.AppendAuditAsync("panel_user", principal!.UserId.ToString(), principal.Username, "server.maintenance", RbacPermissions.ServerStop, body.Enabled ? "enable" : "disable", GetRemoteIp(http), "ok", body.Reason, ct).ConfigureAwait(false);
            _eventHub.BroadcastEvent("server.maintenance", new { enabled = body.Enabled, reason = _serverHost.Server.MaintenanceReason }, RbacPermissions.DashboardView);
            return Results.Ok(new { success = true, maintenanceMode = _serverHost.Server.IsMaintenanceMode, reason = _serverHost.Server.MaintenanceReason });
        });

        app.MapPost("/api/v1/server/broadcast", async (HttpContext http, MessageRequestDto body, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.RoomShout, ct).ConfigureAwait(false);
            if (err is not null) return err;

            if (string.IsNullOrWhiteSpace(body.Message))
                return Results.BadRequest(new { success = false, message = "广播内容不能为空。" });

            string formatted = $"[公告/{principal!.DisplayName}] {body.Message.Trim()}";
            _serverHost.Server.BroadcastSystemMessage(formatted);
            await _store.AppendAuditAsync("panel_user", principal.UserId.ToString(), principal.Username, "server.broadcast", RbacPermissions.RoomShout, "all_rooms", GetRemoteIp(http), "ok", body.Message.Trim(), ct).ConfigureAwait(false);
            return Results.Ok(new { success = true });
        });

        // ==================== 4. 房间大厅、裁判巡房、随机模式与 AI 斗蛐蛐建房 ====================
        app.MapGet("/api/v1/rooms", async (HttpContext http, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.RoomView, ct).ConfigureAwait(false);
            if (err is not null) return err;

            RawRoomSummaryDto[] rawRooms = await _store.Cache.GetOrCreateAsync(
                "rooms",
                "public_list",
                _ => ValueTask.FromResult(BuildRawRoomSummaries()),
                ttl: TimeSpan.FromMilliseconds(500),
                l1Ttl: TimeSpan.FromMilliseconds(500),
                cancellationToken: ct).ConfigureAwait(false);

            object[] sanitized = rawRooms.Select(r => SanitizeRoomSummary(r, principal!)).ToArray();
            return Results.Ok(new
            {
                rooms = sanitized,
                banlists = BanlistManager.Banlists.Select(static (b, idx) => new { index = idx, b.Name, b.WhitelistOnly }).ToArray(),
                availableBotDecks = _options.AvailableBotDecksProvider?.Invoke() ?? []
            });
        });

        app.MapPost("/api/v1/rooms", async (HttpContext http, CreateRoomRequestDto body, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? err) = await RequireAnyPermissionAsync(
                http,
                [RbacPermissions.RoomCreate, RbacPermissions.RoomCreateOwn],
                ct).ConfigureAwait(false);
            if (err is not null) return err;

            if (!_serverHost.Server.IsMultiRoom)
                return Results.BadRequest(new { success = false, message = "当前服务端未开启 EnableMultiRoom，无法动态新建独立房间。" });

            string roomName = string.IsNullOrWhiteSpace(body.RoomName)
                ? $"Arena-{TokenCrypto.GenerateShortCode(5)}"
                : body.RoomName.Trim();

            GameOptions roomOptions = RoomConfigHelper.ResolveCreateRoomOptions(
                _serverHost.Server.Options.Game,
                _serverHost.Server.PasswordParser,
                roomName,
                body,
                out string canonicalFullHostPassword);

            if (!_serverHost.Server.TryCreateRoom(roomName, out Game? createdRoom, roomOptions) || createdRoom is null)
                return Results.Conflict(new { success = false, message = $"无法创建房间 '{roomName}'（可能房名已存在、处于维护模式或已达上限）。" });

            RoomActivityState state = _coordinator.GetOrCreateRoomState(createdRoom.RoomName);
            state.OwnerUserId = principal!.UserId;
            state.OwnerUsername = principal.Username;
            state.RawJoinPassword = canonicalFullHostPassword;

            // 若勾选了签发斗蛐蛐临时 player Token（支持指定 1~4 枚，未指定时 1v1 默认 2 枚、2v2 默认 4 枚），自动校验权限并生成
            List<object> issuedTokens = new();
            if (body.IssuePlayerTokens && RbacPolicyEvaluator.CanIssueMcpRole(principal.RoleCodes, principal.EffectivePermissions, McpRoleCodes.Player))
            {
                int defaultCount = createdRoom.IsTag ? 4 : 2;
                int count = Math.Clamp(body.AiTokenCount ?? defaultCount, 1, 4);
                for (int i = 0; i < count; i++)
                {
                    string? customNickname = body.AiPlayerNames is not null && i < body.AiPlayerNames.Length
                        ? body.AiPlayerNames[i]?.Trim()
                        : null;
                    if (string.IsNullOrWhiteSpace(customNickname))
                        customNickname = null;

                    string label = customNickname ?? $"待AI自取名 ({createdRoom.RoomName})";
                    (bool issuedOk, string issueErr, IssuedMcpTokenResult? issued) = await _store.IssueMcpTokenAsync(
                        principal,
                        name: label,
                        mcpRole: McpRoleCodes.Player,
                        allowedRooms: [createdRoom.RoomName],
                        maxSessions: 1,
                        requireApproval: true,
                        expiresInHours: 12,
                        ip: GetRemoteIp(http),
                        cancellationToken: ct,
                        preferredPlayerName: customNickname).ConfigureAwait(false);

                    if (!issuedOk && customNickname is not null)
                        return Results.BadRequest(new { success = false, message = issueErr });

                    if (issuedOk && issued is not null)
                    {
                        issuedTokens.Add(new
                        {
                            seat = i,
                            name = issued.Entity.Name,
                            preferredPlayerName = issued.Entity.PreferredPlayerName,
                            mcpRole = issued.Entity.McpRole,
                            rawToken = issued.RawToken
                        });
                    }
                }
            }

            await _store.Cache.FlushNamespaceAsync("rooms", ct).ConfigureAwait(false);
            await _store.Cache.FlushNamespaceAsync("portal", ct).ConfigureAwait(false);
            await _store.AppendAuditAsync("panel_user", principal.UserId.ToString(), principal.Username, "room.create", RbacPermissions.RoomCreate, createdRoom.RoomName, GetRemoteIp(http), "ok", null, ct).ConfigureAwait(false);

            return Results.Ok(new
            {
                success = true,
                hostPassword = principal.IsStreamerOnly ? null : createdRoom.RoomName,
                fullHostPassword = principal.IsStreamerOnly ? null : canonicalFullHostPassword,
                room = SanitizeRoomSummary(BuildSingleRawRoomSummary(createdRoom), principal),
                issuedPlayerTokens = issuedTokens
            });
        });

        app.MapGet("/api/v1/rooms/{roomName}", async (HttpContext http, string roomName, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.RoomView, ct).ConfigureAwait(false);
            if (err is not null) return err;

            if (!_serverHost.Server.TryGetRoom(roomName, out Game? game) || game is null)
                return Results.NotFound(new { message = $"房间 '{roomName}' 不存在或已结束。" });

            RoomActivityState? activity = _coordinator.TryGetRoomState(game.RoomName);
            return Results.Ok(new
            {
                room = SanitizeRoomDetail(game, activity, principal!)
            });
        });

        app.MapPatch("/api/v1/rooms/{roomName}/rules", async (HttpContext http, string roomName, UpdateRoomRulesRequestDto body, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? err) = await RequireAnyPermissionAsync(
                http,
                [RbacPermissions.RoomEditRules, RbacPermissions.RoomCreateOwn],
                ct).ConfigureAwait(false);
            if (err is not null) return err;

            if (!_serverHost.Server.TryGetRoom(roomName, out Game? game) || game is null)
                return Results.NotFound(new { message = $"房间 '{roomName}' 不存在。" });

            if (!CanManageRoom(principal!, game.RoomName, RbacPermissions.RoomEditRules))
                return Results.Json(new { message = "仅房管或房间创建者可修改该房间规则。" }, statusCode: StatusCodes.Status403Forbidden);

            RoomRuleOverrides overrides = new();
            if (!string.IsNullOrWhiteSpace(body.PresetRuleString))
            {
                string spec = body.PresetRuleString.Contains('#') ? body.PresetRuleString : body.PresetRuleString + "#";
                overrides = _serverHost.Server.PasswordParser.Parse(spec).Overrides;
            }

            if (body.Mode.HasValue) overrides.Mode = body.Mode.Value;
            if (body.Region.HasValue) overrides.Region = body.Region.Value;
            if (body.MasterRule.HasValue) overrides.MasterRule = Math.Clamp(body.MasterRule.Value, 1, 5);
            if (body.StartLp.HasValue) overrides.StartLp = Math.Clamp(body.StartLp.Value, 1, 999999);
            if (body.StartHand.HasValue) overrides.StartHand = Math.Clamp(body.StartHand.Value, 0, 40);
            if (body.DrawCount.HasValue) overrides.DrawCount = Math.Clamp(body.DrawCount.Value, 0, 35);
            if (body.GameTimer.HasValue) overrides.GameTimer = Math.Clamp(body.GameTimer.Value, 0, 9999);
            if (body.NoCheckDeck.HasValue) overrides.NoCheckDeck = body.NoCheckDeck.Value;
            if (body.NoShuffleDeck.HasValue) overrides.NoShuffleDeck = body.NoShuffleDeck.Value;
            if (body.BanlistIndex.HasValue)
            {
                if (body.BanlistIndex.Value < 0)
                    overrides.NoBanlist = true;
                else
                    overrides.BanlistIndex = body.BanlistIndex.Value;
            }
            if (!string.IsNullOrWhiteSpace(body.RandomDeckMode) &&
                Enum.TryParse(body.RandomDeckMode.Trim(), ignoreCase: true, out RandomDeckMode parsedRand))
            {
                overrides.RandomDeck = parsedRand;
            }

            if (!game.ApplyLobbyRuleOverrides(overrides))
                return Results.BadRequest(new { success = false, message = "当前房间已离开大厅阶段或 Tag 席位不兼容，无法修改规则。" });

            await _store.Cache.FlushNamespaceAsync("rooms", ct).ConfigureAwait(false);
            await _store.AppendAuditAsync("panel_user", principal!.UserId.ToString(), principal.Username, "room.edit_rules", RbacPermissions.RoomEditRules, game.RoomName, GetRemoteIp(http), "ok", null, ct).ConfigureAwait(false);
            _eventHub.BroadcastEvent("room.updated", new { roomName = game.RoomName, reason = "rules.changed" }, RbacPermissions.RoomView);
            return Results.Ok(new { success = true, room = SanitizeRoomDetail(game, _coordinator.TryGetRoomState(game.RoomName), principal) });
        });

        app.MapPost("/api/v1/rooms/{roomName}/death", async (HttpContext http, string roomName, SuddenDeathRequestDto body, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.RoomStartDeath, ct).ConfigureAwait(false);
            if (err is not null) return err;

            if (!_serverHost.Server.TryGetRoom(roomName, out Game? game) || game is null)
                return Results.NotFound(new { message = $"房间 '{roomName}' 不存在。" });

            bool ok;
            if (body.Enabled)
            {
                ok = game.StartSuddenDeath(body.ExtraTurns ?? 4);
                if (ok) game.BroadcastSystemMessage($"[Judge] 裁判 {principal!.DisplayName} 已开启额外回合猝死规则（ED）。");
            }
            else
            {
                game.CancelSuddenDeath();
                ok = true;
                game.BroadcastSystemMessage($"[Judge] 裁判 {principal!.DisplayName} 已取消额外回合猝死规则。");
            }

            await _store.Cache.FlushNamespaceAsync("rooms", ct).ConfigureAwait(false);
            await _store.AppendAuditAsync("panel_user", principal!.UserId.ToString(), principal.Username, "room.sudden_death", RbacPermissions.RoomStartDeath, game.RoomName, GetRemoteIp(http), ok ? "ok" : "error", null, ct).ConfigureAwait(false);
            return Results.Ok(new { success = ok, isSuddenDeath = game.IsSuddenDeath, targetTurn = game.SuddenDeathTargetTurn });
        });

        app.MapPost("/api/v1/rooms/{roomName}/chat", async (HttpContext http, string roomName, MessageRequestDto body, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.RoomShout, ct).ConfigureAwait(false);
            if (err is not null) return err;

            if (!_serverHost.Server.TryGetRoom(roomName, out Game? game) || game is null)
                return Results.NotFound(new { message = $"房间 '{roomName}' 不存在。" });

            if (string.IsNullOrWhiteSpace(body.Message))
                return Results.BadRequest(new { message = "消息内容不能为空。" });

            string text = $"[裁判/{principal!.DisplayName}] {body.Message.Trim()}";
            game.BroadcastSystemMessage(text);
            _coordinator.GetOrCreateRoomState(game.RoomName).AddChat($"[裁判/{principal.DisplayName}]", (int)PlayerType.Yellow, body.Message.Trim());
            _eventHub.BroadcastEvent("room.chat", new { roomName = game.RoomName, sender = $"[裁判/{principal.DisplayName}]", senderType = (int)PlayerType.Yellow, message = body.Message.Trim(), timestampUtc = DateTime.UtcNow }, RbacPermissions.RoomView);
            await _store.AppendAuditAsync("panel_user", principal.UserId.ToString(), principal.Username, "room.shout", RbacPermissions.RoomShout, game.RoomName, GetRemoteIp(http), "ok", body.Message.Trim(), ct).ConfigureAwait(false);
            return Results.Ok(new { success = true });
        });

        app.MapPost("/api/v1/rooms/{roomName}/bots", async (HttpContext http, string roomName, SpawnBotRequestDto body, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.BotSpawn, ct).ConfigureAwait(false);
            if (err is not null) return err;

            if (!_serverHost.Server.TryGetRoom(roomName, out Game? game) || game is null)
                return Results.NotFound(new { message = $"房间 '{roomName}' 不存在。" });

            if (game.State != GameState.Lobby)
                return Results.BadRequest(new { message = "仅可在大厅阶段召唤内置 WindBot。" });

            if (_options.WindBotSpawner is null)
                return Results.BadRequest(new { message = "当前宿主未挂载内置 WindBot 召唤器。" });

            int emptySlots = game.Players.Count(static p => p is null);
            if (emptySlots == 0)
                return Results.BadRequest(new { message = "当前房间决斗席位已满。" });

            int count = body.FillAll ? emptySlots : Math.Clamp(body.Count ?? 1, 1, emptySlots);
            IReadOnlyList<string> spawned = _options.WindBotSpawner(game, body.Deck, body.Dialog, count);
            await _store.AppendAuditAsync("panel_user", principal!.UserId.ToString(), principal.Username, "room.spawn_bot", RbacPermissions.BotSpawn, game.RoomName, GetRemoteIp(http), "ok", string.Join(",", spawned), ct).ConfigureAwait(false);
            return Results.Ok(new { success = true, spawned });
        });

        app.MapPost("/api/v1/rooms/{roomName}/kick", async (HttpContext http, string roomName, KickPlayerRequestDto body, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.RoomKick, ct).ConfigureAwait(false);
            if (err is not null) return err;

            if (!_serverHost.Server.TryGetRoom(roomName, out Game? game) || game is null)
                return Results.NotFound(new { message = $"房间 '{roomName}' 不存在。" });

            Player? target = null;
            if (body.SlotIndex.HasValue && body.SlotIndex.Value >= 0 && body.SlotIndex.Value < game.Players.Length)
                target = game.Players[body.SlotIndex.Value];
            else if (!string.IsNullOrWhiteSpace(body.PlayerName))
            {
                string trimmedName = body.PlayerName.Trim();
                target = game.Players.FirstOrDefault(p => p is not null && string.Equals(p.Name, trimmedName, StringComparison.OrdinalIgnoreCase))
                         ?? RoomConfigHelper.SnapshotObservers(game).FirstOrDefault(o => string.Equals(o.Name, trimmedName, StringComparison.OrdinalIgnoreCase));
            }

            if (target is null)
                return Results.NotFound(new { message = "未找到目标玩家。" });

            string kickedName = target.Name ?? "Player";
            game.RemovePlayer(target);
            target.Disconnect();
            await _store.Cache.FlushNamespaceAsync("rooms", ct).ConfigureAwait(false);
            await _store.AppendAuditAsync("panel_user", principal!.UserId.ToString(), principal.Username, "room.kick", RbacPermissions.RoomKick, $"{game.RoomName}:{kickedName}", GetRemoteIp(http), "ok", null, ct).ConfigureAwait(false);
            return Results.Ok(new { success = true, kicked = kickedName });
        });

        app.MapDelete("/api/v1/rooms/{roomName}", async (HttpContext http, string roomName, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? err) = await RequireAnyPermissionAsync(
                http,
                [RbacPermissions.RoomClose, RbacPermissions.RoomCreateOwn],
                ct).ConfigureAwait(false);
            if (err is not null) return err;

            if (!_serverHost.Server.TryGetRoom(roomName, out Game? game) || game is null)
                return Results.NotFound(new { message = $"房间 '{roomName}' 不存在。" });

            if (!CanManageRoom(principal!, game.RoomName, RbacPermissions.RoomClose))
                return Results.Json(new { message = "无权关闭他人创建的房间。" }, statusCode: StatusCodes.Status403Forbidden);

            if (_serverHost.Server.IsMultiRoom)
            {
                foreach (Player? p in game.Players.ToArray())
                    p?.Disconnect();
                foreach (Player obs in game.Observers.ToArray())
                    obs.Disconnect();
                game.End();
            }
            else
            {
                return Results.BadRequest(new { message = "单房间模式下不支持单独销毁默认房间。" });
            }

            await _store.Cache.FlushNamespaceAsync("rooms", ct).ConfigureAwait(false);
            await _store.AppendAuditAsync("panel_user", principal!.UserId.ToString(), principal.Username, "room.close", RbacPermissions.RoomClose, game.RoomName, GetRemoteIp(http), "ok", null, ct).ConfigureAwait(false);
            return Results.Ok(new { success = true });
        });

        // ==================== 5. 封禁表、赛事交牌与战报云录像 ====================
        app.MapGet("/api/v1/bans", async (HttpContext http, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? _, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.BanManage, ct).ConfigureAwait(false);
            if (err is not null) return err;

            IReadOnlyList<ActiveBanRule> bans = await _store.GetActiveBansAsync(ct).ConfigureAwait(false);
            return Results.Ok(new { bans });
        });

        app.MapPost("/api/v1/bans", async (HttpContext http, CreateBanRequestDto body, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.BanManage, ct).ConfigureAwait(false);
            if (err is not null) return err;

            (bool ok, string msg, BanEntity? ban) = await _store.CreateBanAsync(
                principal!,
                body.TargetType ?? "ip",
                body.TargetValue ?? string.Empty,
                body.Reason ?? "违反服务器规则",
                body.DurationMinutes,
                GetRemoteIp(http),
                ct).ConfigureAwait(false);

            return ok ? Results.Ok(new { success = true, message = msg, ban }) : Results.BadRequest(new { success = false, message = msg });
        });

        app.MapDelete("/api/v1/bans/{id:guid}", async (HttpContext http, Guid id, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.BanManage, ct).ConfigureAwait(false);
            if (err is not null) return err;

            bool ok = await _store.RemoveBanAsync(principal!, id, GetRemoteIp(http), ct).ConfigureAwait(false);
            return ok ? Results.Ok(new { success = true }) : Results.NotFound(new { success = false, message = "封禁记录不存在。" });
        });

        app.MapGet("/api/v1/tournament-decks", async (HttpContext http, string? tag, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? _, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.TournamentDeckRead, ct).ConfigureAwait(false);
            if (err is not null) return err;

            IReadOnlyList<TournamentDeckEntity> decks = await _store.ListTournamentDecksAsync(tag, ct).ConfigureAwait(false);
            return Results.Ok(new { decks });
        });

        app.MapPost("/api/v1/tournament-decks", async (HttpContext http, UpsertTournamentDeckDto body, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.TournamentDeckWrite, ct).ConfigureAwait(false);
            if (err is not null) return err;

            if (string.IsNullOrWhiteSpace(body.PlayerName) || string.IsNullOrWhiteSpace(body.YdkContent))
                return Results.BadRequest(new { message = "选手名与卡组内容不能为空。" });

            YdkDeckData parsed = YdkSerializer.Parse(body.YdkContent, body.PlayerName);
            TournamentDeckEntity saved = await _store.UpsertTournamentDeckAsync(
                principal!,
                body.TournamentTag ?? "default",
                body.PlayerName,
                body.YdkContent,
                parsed.Main.Count,
                parsed.Extra.Count,
                parsed.Side.Count,
                GetRemoteIp(http),
                ct).ConfigureAwait(false);

            return Results.Ok(new { success = true, deck = saved });
        });

        app.MapGet("/api/v1/replays", async (HttpContext http, int? limit, long? beforeId, string? q, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? err) = await RequireAnyPermissionAsync(
                http,
                [RbacPermissions.DuelLogView, RbacPermissions.DuelLogViewOwn],
                ct).ConfigureAwait(false);
            if (err is not null) return err;

            IReadOnlyList<DuelRecordEntity> records = await _store.ListDuelRecordsAsync(limit ?? 50, beforeId, q, ct).ConfigureAwait(false);
            object[] sanitized = records.Select(r => SanitizeDuelRecord(r, principal!)).ToArray();
            return Results.Ok(new { records = sanitized });
        });

        app.MapGet("/api/v1/replays/{id:long}.yrp", async (HttpContext http, long id, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? _, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.ReplayDownload, ct).ConfigureAwait(false);
            if (err is not null) return err;

            byte[]? yrp = await _store.GetReplayYrpBytesAsync(id, ct).ConfigureAwait(false);
            if (yrp is null || yrp.Length == 0)
                return Results.NotFound(new { message = $"未找到录像 R#{id} 的 .yrp 文件。" });

            return Results.File(yrp, "application/octet-stream", $"replay_{id}.yrp");
        });

        // ==================== 6. 随机盲盒卡组池、版本化卡库搜索与 DIY 管理 ====================
        app.MapGet("/api/v1/decks/pool", async (HttpContext http, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? _, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.DeckPoolView, ct).ConfigureAwait(false);
            if (err is not null) return err;

            IReadOnlyList<PresetDeckEntry> entries = _serverHost.Server.PresetDecks.Entries;
            return Results.Ok(new
            {
                decks = entries.Select(static e => new
                {
                    e.Id,
                    e.Name,
                    e.HasDiyCards,
                    mainCount = e.Data.Main.Count,
                    extraCount = e.Data.Extra.Count,
                    sideCount = e.Data.Side.Count,
                    tags = e.Data.Tags
                })
            });
        });

        app.MapPost("/api/v1/decks/pool", async (HttpContext http, UploadPoolDeckDto body, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.DeckPoolManage, ct).ConfigureAwait(false);
            if (err is not null) return err;

            if (string.IsNullOrWhiteSpace(body.Name) || string.IsNullOrWhiteSpace(body.YdkContent))
                return Results.BadRequest(new { message = "卡组名称与 .ydk 内容不能为空。" });

            string safeName = Path.GetFileNameWithoutExtension(body.Name.Trim());
            if (string.IsNullOrWhiteSpace(safeName) || safeName.Contains("..", StringComparison.Ordinal))
                return Results.BadRequest(new { message = "卡组名称包含非法字符。" });

            YdkDeckData parsed = YdkSerializer.Parse(body.YdkContent, safeName, body.Tags);
            if (parsed.Main.Count == 0 && parsed.Extra.Count == 0)
                return Results.BadRequest(new { message = "卡组必须至少包含一张主卡组或额外卡组卡片。" });

            string randomPoolDir = Path.Combine(Path.GetFullPath(_serverHost.Server.Options.RootPath), "decks", "random");
            string subDir = body.Category is { Length: > 0 } && !body.Category.Contains("..", StringComparison.Ordinal)
                ? Path.Combine(randomPoolDir, Path.GetFileName(body.Category.Trim()))
                : randomPoolDir;
            Directory.CreateDirectory(subDir);
            string targetFile = Path.Combine(subDir, safeName + ".ydk");
            await File.WriteAllTextAsync(targetFile, body.YdkContent, ct).ConfigureAwait(false);

            _serverHost.Server.ReloadAssets();
            await _store.AppendAuditAsync("panel_user", principal!.UserId.ToString(), principal.Username, "deck_pool.upload", RbacPermissions.DeckPoolManage, safeName, GetRemoteIp(http), "ok", null, ct).ConfigureAwait(false);
            return Results.Ok(new { success = true, totalPoolDecks = _serverHost.Server.PresetDecks.Count });
        });

        app.MapPost("/api/v1/decks/chaos-preview", async (HttpContext http, ChaosPreviewRequestDto? body, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? _, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.DeckPoolView, ct).ConfigureAwait(false);
            if (err is not null) return err;

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
            int normalOrEffect = 0, highLevel = 0, spells = 0, traps = 0;
            foreach (int id in generated.Main)
            {
                if (_serverHost.Server.CardRepository.TryGetCard(id, out Card c))
                {
                    if ((c.Type & (int)YGOProSharp.Abstractions.Ocg.Enums.CardType.Spell) != 0) spells++;
                    else if ((c.Type & (int)YGOProSharp.Abstractions.Ocg.Enums.CardType.Trap) != 0) traps++;
                    else if (c.Level >= 5) highLevel++;
                    else normalOrEffect++;
                }
            }

            return Results.Ok(new
            {
                mainCount = generated.Main.Count,
                extraCount = generated.Extra.Count,
                breakdown = new { lowLevelMonsters = normalOrEffect, highLevelMonsters = highLevel, spells, traps },
                sampleMainCards = generated.Main.Take(20).Select(id => new
                {
                    id,
                    name = namedRepo is not null && namedRepo.TryGetCard(id, out NamedCard nc) ? nc.Name : $"#{id}"
                }),
                sampleExtraCards = generated.Extra.Select(id => new
                {
                    id,
                    name = namedRepo is not null && namedRepo.TryGetCard(id, out NamedCard nc) ? nc.Name : $"#{id}"
                }),
                ydk = YdkSerializer.Serialize(generated, "ChaosPreview")
            });
        });

        app.MapGet("/api/v1/cards/search", async (HttpContext http, string? q, int? limit, bool? diyOnly, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? _, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.CardSearch, ct).ConfigureAwait(false);
            if (err is not null) return err;

            string queryText = q?.Trim() ?? string.Empty;
            int max = Math.Clamp(limit ?? 40, 1, 100);
            long catalogVer = _serverHost.Server.ReloadableCards.CatalogVersion;
            string cacheKey = $"v{catalogVer}:q:{queryText.ToLowerInvariant()}:diy:{diyOnly == true}:l:{max}";

            object[] items = await _store.Cache.GetOrCreateAsync(
                "cards",
                cacheKey,
                _ => ValueTask.FromResult(SearchCardsCore(queryText, max, diyOnly == true)),
                ttl: TimeSpan.FromMinutes(30),
                l1Ttl: TimeSpan.FromMinutes(10),
                cancellationToken: ct).ConfigureAwait(false);

            return Results.Ok(new { catalogVersion = catalogVer, cards = items });
        });

        app.MapGet("/api/v1/diy/packages", async (HttpContext http, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? _, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.DiyView, ct).ConfigureAwait(false);
            if (err is not null) return err;

            IReadOnlyList<DiyPackageManifest> packages = _diyManager.ListPackages();
            var items = packages.Select(p =>
            {
                DiyPackageValidationReport report = _diyManager.ValidatePackage(p.Id);
                return new { manifest = p, validation = report };
            }).ToArray();

            string[] trashEntries = Directory.Exists(_diyManager.TrashDirectory)
                ? Directory.GetDirectories(_diyManager.TrashDirectory).Select(Path.GetFileName).Where(static s => s is not null).Cast<string>().ToArray()
                : [];

            return Results.Ok(new { packages = items, trash = trashEntries });
        });

        app.MapPost("/api/v1/diy/packages", async (HttpContext http, UpsertDiyManifestDto body, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.DiyEdit, ct).ConfigureAwait(false);
            if (err is not null) return err;

            if (!DiyPackageManager.IsValidPackageId(body.Id))
                return Results.BadRequest(new { message = "包 ID 仅允许字母、数字、点、下划线和连字符（1~64 位）。" });

            DiyPackageManifest saved = _diyManager.UpsertManifest(new DiyPackageManifest
            {
                Id = body.Id!.Trim(),
                Name = string.IsNullOrWhiteSpace(body.Name) ? body.Id.Trim() : body.Name.Trim(),
                Version = string.IsNullOrWhiteSpace(body.Version) ? "1.0.0" : body.Version.Trim(),
                Author = body.Author?.Trim() ?? principal!.Username,
                Enabled = body.Enabled ?? true,
                Priority = body.Priority ?? 100
            });

            await _store.AppendAuditAsync("panel_user", principal!.UserId.ToString(), principal.Username, "diy.package_upsert", RbacPermissions.DiyEdit, saved.Id, GetRemoteIp(http), "ok", null, ct).ConfigureAwait(false);
            return Results.Ok(new { success = true, manifest = saved });
        });

        app.MapPost("/api/v1/diy/packages/{id}/upload", async (HttpContext http, string id, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.DiyUpload, ct).ConfigureAwait(false);
            if (err is not null) return err;

            if (!DiyPackageManager.IsValidPackageId(id))
                return Results.BadRequest(new { message = "非法 DIY 包 ID。" });

            if (!http.Request.HasFormContentType)
                return Results.BadRequest(new { message = "请使用 multipart/form-data 上传文件。" });

            IFormCollection form = await http.Request.ReadFormAsync(ct).ConfigureAwait(false);
            if (form.Files.Count == 0)
                return Results.BadRequest(new { message = "未检测到上传文件。" });

            List<string> savedFiles = new();
            List<string> errors = new();

            foreach (IFormFile file in form.Files)
            {
                string fileName = Path.GetFileName(file.FileName);
                string ext = Path.GetExtension(fileName).ToLowerInvariant();
                if (file.Length > 64L * 1024 * 1024)
                {
                    errors.Add($"{fileName}: 文件超过 64MB 上限。");
                    continue;
                }

                using MemoryStream ms = new();
                await file.CopyToAsync(ms, ct).ConfigureAwait(false);
                byte[] bytes = ms.ToArray();

                try
                {
                    if (ext == ".cdb")
                    {
                        CardDatabaseValidationResult val = _diyManager.SaveCardDatabase(id, bytes, fileName);
                        if (!val.IsValid) errors.Add($"{fileName}: {val.ErrorMessage}");
                        else savedFiles.Add(fileName);
                    }
                    else if (ext == ".lua")
                    {
                        string luaText = System.Text.Encoding.UTF8.GetString(bytes);
                        LuaScriptSecurityAudit audit = _diyManager.SaveScript(id, fileName, luaText);
                        if (!audit.IsSafe) errors.AddRange(audit.Violations.Select(v => $"{fileName}: {v}"));
                        else savedFiles.Add(fileName);
                    }
                    else if (ext == ".ydk")
                    {
                        _diyManager.SaveDeck(id, fileName, System.Text.Encoding.UTF8.GetString(bytes));
                        savedFiles.Add(fileName);
                    }
                    else if (ext is ".ypk" or ".zip")
                    {
                        ms.Position = 0;
                        IReadOnlyList<string> archiveErrs = DiyPackageManager.AuditArchiveEntries(ms);
                        if (archiveErrs.Count > 0)
                        {
                            errors.AddRange(archiveErrs.Select(e => $"{fileName}: {e}"));
                        }
                        else
                        {
                            string pkgDir = _diyManager.GetPackageDirectory(id);
                            Directory.CreateDirectory(pkgDir);
                            await File.WriteAllBytesAsync(Path.Combine(pkgDir, fileName), bytes, ct).ConfigureAwait(false);
                            savedFiles.Add(fileName);
                        }
                    }
                    else if (ext == ".conf")
                    {
                        string confText = System.Text.Encoding.UTF8.GetString(bytes);
                        if (fileName.Equals("lflist.conf", StringComparison.OrdinalIgnoreCase))
                            _ = BanlistManager.ParseText(confText);

                        string pkgDir = _diyManager.GetPackageDirectory(id);
                        Directory.CreateDirectory(pkgDir);
                        await File.WriteAllBytesAsync(Path.Combine(pkgDir, fileName), bytes, ct).ConfigureAwait(false);
                        savedFiles.Add(fileName);
                    }
                    else
                    {
                        errors.Add($"{fileName}: 不支持的文件扩展名 '{ext}'。");
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"{fileName}: {ex.Message}");
                }
            }

            DiyPackageValidationReport report = _diyManager.ValidatePackage(id);
            await _store.AppendAuditAsync("panel_user", principal!.UserId.ToString(), principal.Username, "diy.upload", RbacPermissions.DiyUpload, id, GetRemoteIp(http), errors.Count == 0 ? "ok" : "partial_error", JsonSerializer.Serialize(new { savedFiles, errors }), ct).ConfigureAwait(false);

            return Results.Ok(new
            {
                success = errors.Count == 0,
                savedFiles,
                errors,
                validation = report
            });
        });

        app.MapDelete("/api/v1/diy/packages/{id}", async (HttpContext http, string id, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.DiyEdit, ct).ConfigureAwait(false);
            if (err is not null) return err;

            try
            {
                string trashName = _diyManager.MoveToTrash(id);
                await _store.AppendAuditAsync("panel_user", principal!.UserId.ToString(), principal.Username, "diy.move_to_trash", RbacPermissions.DiyEdit, id, GetRemoteIp(http), "ok", trashName, ct).ConfigureAwait(false);
                return Results.Ok(new { success = true, trashEntry = trashName });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message });
            }
        });

        app.MapPost("/api/v1/diy/trash/{trashEntry}/restore", async (HttpContext http, string trashEntry, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.DiyEdit, ct).ConfigureAwait(false);
            if (err is not null) return err;

            try
            {
                string restoredId = _diyManager.RestoreFromTrash(trashEntry);
                await _store.AppendAuditAsync("panel_user", principal!.UserId.ToString(), principal.Username, "diy.restore_trash", RbacPermissions.DiyEdit, restoredId, GetRemoteIp(http), "ok", null, ct).ConfigureAwait(false);
                return Results.Ok(new { success = true, packageId = restoredId });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message });
            }
        });

        app.MapPost("/api/v1/diy/reload", async (HttpContext http, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.DiyReload, ct).ConfigureAwait(false);
            if (err is not null) return err;

            ServerReloadResult reloadResult = _serverHost.Server.ReloadAssets();
            await _store.Cache.FlushNamespaceAsync("cards", ct).ConfigureAwait(false);
            await _store.AppendAuditAsync("panel_user", principal!.UserId.ToString(), principal.Username, "diy.reload", RbacPermissions.DiyReload, $"v{reloadResult.CatalogVersion}", GetRemoteIp(http), "ok", null, ct).ConfigureAwait(false);
            _eventHub.BroadcastEvent("diy.reloaded", reloadResult, RbacPermissions.DashboardView);
            return Results.Ok(new { success = true, reload = reloadResult });
        });

        app.MapGet("/api/v1/diy/conflicts", async (HttpContext http, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? _, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.DiyView, ct).ConfigureAwait(false);
            if (err is not null) return err;

            DiyGlobalConflictReport report = _diyManager.AnalyzeAllPackageConflicts(_serverHost.Server.CardRepository);
            return Results.Ok(report);
        });

        app.MapGet("/api/v1/diy/packages/{id}/cards", async (HttpContext http, string id, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? _, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.DiyView, ct).ConfigureAwait(false);
            if (err is not null) return err;

            if (!DiyPackageManager.IsValidPackageId(id))
                return Results.BadRequest(new { message = "非法 DIY 包 ID。" });

            IReadOnlyList<DiyCardSummary> cards = _diyManager.ListPackageCards(id);
            StringsConfSummary stringsConf = _diyManager.ReadStringsConf(id);
            return Results.Ok(new { packageId = id, cards, stringsConf });
        });

        app.MapGet("/api/v1/diy/packages/{id}/cards/{cardId:int}", async (HttpContext http, string id, int cardId, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? _, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.DiyView, ct).ConfigureAwait(false);
            if (err is not null) return err;

            if (!DiyPackageManager.IsValidPackageId(id))
                return Results.BadRequest(new { message = "非法 DIY 包 ID。" });

            DiyCardDetail? detail = _diyManager.GetCardDetail(id, cardId);
            return detail is not null
                ? Results.Ok(new { detail })
                : Results.NotFound(new { message = $"包 '{id}' 中不存在卡密 {cardId}。" });
        });

        app.MapPut("/api/v1/diy/packages/{id}/cards/{cardId:int}", async (HttpContext http, string id, int cardId, UpsertDiyCardRequestDto body, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.DiyEdit, ct).ConfigureAwait(false);
            if (err is not null) return err;

            if (!DiyPackageManager.IsValidPackageId(id) || cardId <= 0)
                return Results.BadRequest(new { message = "包 ID 或卡密无效。" });

            try
            {
                DiyCardEntry entry = MapCardDtoToEntry(body.Card, cardId);
                CardDatabaseValidationResult dbVal = _diyManager.UpsertCard(id, entry, body.LuaScript);
                if (!dbVal.IsValid)
                    return Results.BadRequest(new { success = false, message = dbVal.ErrorMessage ?? "写入工作副本校验未通过。" });

                ServerReloadResult? reload = null;
                if (body.AutoReload == true && principal!.HasPermission(RbacPermissions.DiyReload))
                {
                    reload = _serverHost.Server.ReloadAssets();
                    await _store.Cache.FlushNamespaceAsync("cards", ct).ConfigureAwait(false);
                    _eventHub.BroadcastEvent("diy.reloaded", reload, RbacPermissions.DashboardView);
                }

                DiyPackageValidationReport pkgReport = _diyManager.ValidatePackage(id);
                DiyCardDetail? detail = _diyManager.GetCardDetail(id, cardId);
                await _store.AppendAuditAsync("panel_user", principal!.UserId.ToString(), principal.Username, "diy.card_upsert", RbacPermissions.DiyEdit, $"{id}:{cardId}", GetRemoteIp(http), "ok", null, ct).ConfigureAwait(false);

                return Results.Ok(new
                {
                    success = true,
                    detail,
                    validation = pkgReport,
                    reload
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message });
            }
        });

        app.MapDelete("/api/v1/diy/packages/{id}/cards/{cardId:int}", async (HttpContext http, string id, int cardId, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.DiyEdit, ct).ConfigureAwait(false);
            if (err is not null) return err;

            if (!DiyPackageManager.IsValidPackageId(id) || cardId <= 0)
                return Results.BadRequest(new { message = "包 ID 或卡密无效。" });

            bool deleted = _diyManager.DeleteCard(id, cardId, deleteAssets: true);
            if (!deleted)
                return Results.NotFound(new { success = false, message = "未找到对应卡片记录。" });

            await _store.AppendAuditAsync("panel_user", principal!.UserId.ToString(), principal.Username, "diy.card_delete", RbacPermissions.DiyEdit, $"{id}:{cardId}", GetRemoteIp(http), "ok", null, ct).ConfigureAwait(false);
            return Results.Ok(new { success = true });
        });

        app.MapPost("/api/v1/diy/packages/{id}/cards/{cardId:int}/image", async (HttpContext http, string id, int cardId, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? err) = await RequireAnyPermissionAsync(http, [RbacPermissions.DiyEdit, RbacPermissions.DiyUpload], ct).ConfigureAwait(false);
            if (err is not null) return err;

            if (!DiyPackageManager.IsValidPackageId(id) || cardId <= 0)
                return Results.BadRequest(new { message = "包 ID 或卡密无效。" });
            if (!http.Request.HasFormContentType)
                return Results.BadRequest(new { message = "请通过 multipart/form-data 上传 PNG 或 JPG 卡图。" });

            IFormCollection form = await http.Request.ReadFormAsync(ct).ConfigureAwait(false);
            if (form.Files.Count == 0)
                return Results.BadRequest(new { message = "未收到图片文件。" });

            IFormFile file = form.Files[0];
            if (file.Length > 8 * 1024 * 1024)
                return Results.BadRequest(new { message = "单张卡图不能超过 8MB。" });

            using MemoryStream ms = new();
            await file.CopyToAsync(ms, ct).ConfigureAwait(false);

            try
            {
                string savedFileName = _diyManager.SaveCardImage(id, cardId, ms.GetBuffer().AsSpan(0, (int)ms.Length));
                await _store.AppendAuditAsync("panel_user", principal!.UserId.ToString(), principal.Username, "diy.card_image_upload", RbacPermissions.DiyEdit, $"{id}:{cardId}", GetRemoteIp(http), "ok", savedFileName, ct).ConfigureAwait(false);
                return Results.Ok(new { success = true, fileName = savedFileName });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message });
            }
        });

        app.MapGet("/api/v1/diy/packages/{id}/cards/{cardId:int}/image", async (HttpContext http, string id, int cardId, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? _, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.DiyView, ct).ConfigureAwait(false);
            if (err is not null) return err;

            if (!DiyPackageManager.IsValidPackageId(id) || cardId <= 0)
                return Results.BadRequest(new { message = "包 ID 或卡密无效。" });

            var image = _diyManager.TryReadCardImage(id, cardId);
            if (image is null)
                return Results.NotFound(new { message = "暂无卡图。" });

            http.Response.Headers.CacheControl = "no-cache";
            return Results.Bytes(image.Value.Bytes, image.Value.ContentType);
        });

        app.MapPut("/api/v1/diy/packages/{id}/strings", async (HttpContext http, string id, SaveStringsConfDto body, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.DiyEdit, ct).ConfigureAwait(false);
            if (err is not null) return err;

            if (!DiyPackageManager.IsValidPackageId(id))
                return Results.BadRequest(new { message = "非法 DIY 包 ID。" });

            StringsConfSummary summary = _diyManager.SaveStringsConf(id, body.Content ?? string.Empty);
            await _store.AppendAuditAsync("panel_user", principal!.UserId.ToString(), principal.Username, "diy.strings_save", RbacPermissions.DiyEdit, id, GetRemoteIp(http), "ok", null, ct).ConfigureAwait(false);
            return Results.Ok(new { success = true, stringsConf = summary });
        });

        app.MapGet("/api/v1/diy/packages/{id}/export.ypk", async (HttpContext http, string id, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.DiyView, ct).ConfigureAwait(false);
            if (err is not null) return err;

            if (!DiyPackageManager.IsValidPackageId(id))
                return Results.BadRequest(new { message = "非法 DIY 包 ID。" });

            try
            {
                byte[] ypkBytes = _diyManager.ExportPackageAsYpk(id);
                await _store.AppendAuditAsync("panel_user", principal!.UserId.ToString(), principal.Username, "diy.export_ypk", RbacPermissions.DiyView, id, GetRemoteIp(http), "ok", null, ct).ConfigureAwait(false);
                return Results.File(ypkBytes, "application/zip", $"{id}.ypk");
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.NotFound(new { message = ex.Message });
            }
        });

        app.MapPost("/api/v1/diy/sandbox-test", async (HttpContext http, SandboxTestCardRequestDto body, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.DiySandboxTest, ct).ConfigureAwait(false);
            if (err is not null) return err;

            int cardId = body.Card?.Id is > 0 ? body.Card.Id.Value : 99000001;
            DiyCardEntry draftCard = MapCardDtoToEntry(body.Card, cardId);
            CardSandboxResult sandboxResult = _serverHost.Server.RunCardSandboxTest(new CardSandboxRequest(
                draftCard,
                body.LuaScript,
                body.TestExecuteFirstActivation ?? true));

            await _store.AppendAuditAsync(
                "panel_user",
                principal!.UserId.ToString(),
                principal.Username,
                "diy.sandbox_test",
                RbacPermissions.DiySandboxTest,
                $"c{cardId}",
                GetRemoteIp(http),
                sandboxResult.Success ? "ok" : "error",
                null,
                ct).ConfigureAwait(false);

            return Results.Ok(new { result = sandboxResult });
        });

        app.MapGet("/api/v1/diy/proposals", async (HttpContext http, string? status, string? packageId, int? limit, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? _, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.DiyView, ct).ConfigureAwait(false);
            if (err is not null) return err;

            IReadOnlyList<DiyProposalEntity> proposals = await _store.ListDiyProposalsAsync(status, packageId, limit ?? 80, ct).ConfigureAwait(false);
            return Results.Ok(new { proposals });
        });

        app.MapPost("/api/v1/diy/proposals", async (HttpContext http, SubmitDiyProposalRequestDto body, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? err) = await RequireAnyPermissionAsync(http, [RbacPermissions.DiyEdit, RbacPermissions.DiyUpload], ct).ConfigureAwait(false);
            if (err is not null) return err;

            string pkgId = body.PackageId?.Trim() ?? string.Empty;
            if (!DiyPackageManager.IsValidPackageId(pkgId))
                return Results.BadRequest(new { message = "必须指定合法的 DIY 包 ID。" });

            DiyProposalPayloadDto payload = body.Payload ?? new DiyProposalPayloadDto(null, null, null, null);
            List<string> auditErrors = PreAuditProposalPayload(payload);
            if (auditErrors.Count > 0)
                return Results.BadRequest(new { success = false, message = "提案内包含未通过安全预检的内容。", errors = auditErrors });

            string diffJson = JsonSerializer.Serialize(payload);
            DiyProposalEntity created = await _store.CreateDiyProposalAsync(
                pkgId,
                body.Title ?? $"更新 DIY 包 {pkgId}",
                diffJson,
                proposerTokenId: null,
                proposerName: principal!.Username,
                actorType: "panel_user",
                actorId: principal.UserId.ToString(),
                ip: GetRemoteIp(http),
                cancellationToken: ct).ConfigureAwait(false);

            _eventHub.BroadcastEvent("diy.proposal_created", new { created.Id, created.PackageId, created.Title }, RbacPermissions.DiyView);
            return Results.Ok(new { success = true, proposal = created });
        });

        app.MapPost("/api/v1/diy/proposals/{proposalId:guid}/review", async (HttpContext http, Guid proposalId, ReviewDiyProposalRequestDto body, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.DiyReviewProposal, ct).ConfigureAwait(false);
            if (err is not null) return err;

            ServerReloadResult? reloadResult = null;
            (bool ok, string msg, DiyProposalEntity? updated) = await _store.ReviewDiyProposalAsync(
                principal!,
                proposalId,
                body.Approve,
                body.Comment,
                async (proposal, innerCt) =>
                {
                    (bool applied, string? applyErr) = ApplyDiyProposalToDisk(proposal);
                    if (!applied)
                        return (false, applyErr);

                    reloadResult = _serverHost.Server.ReloadAssets();
                    await _store.Cache.FlushNamespaceAsync("cards", innerCt).ConfigureAwait(false);
                    return (true, null);
                },
                GetRemoteIp(http),
                ct).ConfigureAwait(false);

            if (!ok)
                return Results.BadRequest(new { success = false, message = msg });

            if (reloadResult is not null)
                _eventHub.BroadcastEvent("diy.reloaded", reloadResult, RbacPermissions.DashboardView);

            return Results.Ok(new
            {
                success = true,
                message = msg,
                proposal = updated,
                reload = reloadResult
            });
        });

        // ==================== 7. 用户、五级角色权限矩阵、MCP Token 与审计日志 ====================
        app.MapGet("/api/v1/permissions", async (HttpContext http, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.DashboardView, ct).ConfigureAwait(false);
            if (err is not null) return err;

            return Results.Ok(new
            {
                explicitPermissions = RbacPermissions.AllExplicitPermissions,
                builtInTemplates = RbacPolicyEvaluator.BuiltInRoles,
                allowedMcpRolesToIssue = RbacPolicyEvaluator.GetAllowedMcpRolesToIssue(principal!.RoleCodes, principal.EffectivePermissions)
            });
        });

        app.MapGet("/api/v1/roles", async (HttpContext http, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? _, IResult? err) = await RequireAnyPermissionAsync(
                http,
                [RbacPermissions.RoleManage, RbacPermissions.UserManage, RbacPermissions.DashboardView],
                ct).ConfigureAwait(false);
            if (err is not null) return err;

            IReadOnlyList<PanelRoleEntity> roles = await _store.ListRolesAsync(ct).ConfigureAwait(false);
            return Results.Ok(new
            {
                roles = roles.Select(static r => new
                {
                    r.Id,
                    r.Code,
                    r.Name,
                    r.Description,
                    r.IsBuiltIn,
                    permissions = RbacPolicyEvaluator.ParsePermissionsJson(r.PermissionsJson)
                })
            });
        });

        app.MapPut("/api/v1/roles/{code}", async (HttpContext http, string code, UpsertRoleRequestDto body, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.RoleManage, ct).ConfigureAwait(false);
            if (err is not null) return err;

            (bool ok, string msg, PanelRoleEntity? role) = await _store.UpsertRoleAsync(
                principal!,
                code,
                body.Name ?? code,
                body.Description ?? string.Empty,
                body.Permissions ?? [],
                GetRemoteIp(http),
                ct).ConfigureAwait(false);

            return ok ? Results.Ok(new { success = true, message = msg, role }) : Results.BadRequest(new { success = false, message = msg });
        });

        app.MapGet("/api/v1/users", async (HttpContext http, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? _, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.UserManage, ct).ConfigureAwait(false);
            if (err is not null) return err;

            IReadOnlyList<PanelUserEntity> users = await _store.ListUsersAsync(ct).ConfigureAwait(false);
            return Results.Ok(new
            {
                users = users.Select(static u => new
                {
                    u.Id,
                    u.Username,
                    u.DisplayName,
                    u.Enabled,
                    u.MustChangePassword,
                    u.FailedLoginCount,
                    u.LockedUntil,
                    u.LastLoginAt,
                    u.LastLoginIp,
                    u.CreatedAt,
                    roles = u.UserRoles.Where(static ur => ur.Role is not null).Select(static ur => ur.Role.Code).ToArray(),
                    permissionsOverride = RbacPolicyEvaluator.ParseOverrideJson(u.PermissionsOverrideJson)
                })
            });
        });

        app.MapPost("/api/v1/users", async (HttpContext http, CreateUserRequestDto body, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.UserManage, ct).ConfigureAwait(false);
            if (err is not null) return err;

            UserPermissionsOverride? over = body.GrantPermissions is not null || body.RevokePermissions is not null
                ? new UserPermissionsOverride(body.GrantPermissions ?? [], body.RevokePermissions ?? [])
                : null;

            bool defaultMustChange = string.IsNullOrWhiteSpace(body.InitialPassword);
            (bool ok, string msg, PanelUserEntity? created, string? generatedPw) = await _store.CreateUserAsync(
                principal!,
                body.Username ?? string.Empty,
                body.DisplayName ?? string.Empty,
                body.InitialPassword,
                body.RoleCodes ?? [BuiltInRoleCodes.AiOperator],
                over,
                body.MustChangePassword ?? defaultMustChange,
                GetRemoteIp(http),
                ct).ConfigureAwait(false);

            if (!ok || created is null)
                return Results.BadRequest(new { success = false, message = msg });

            return Results.Ok(new
            {
                success = true,
                message = msg,
                generatedPassword = generatedPw,
                user = new { created.Id, created.Username, created.DisplayName, created.MustChangePassword }
            });
        });

        app.MapPatch("/api/v1/users/{id:guid}", async (HttpContext http, Guid id, UpdateUserRequestDto body, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.UserManage, ct).ConfigureAwait(false);
            if (err is not null) return err;

            bool hasOverride = body.GrantPermissions is not null || body.RevokePermissions is not null;
            UserPermissionsOverride? over = hasOverride
                ? new UserPermissionsOverride(body.GrantPermissions ?? [], body.RevokePermissions ?? [])
                : null;

            (bool ok, string msg) = await _store.UpdateUserAsync(
                principal!,
                id,
                body.DisplayName,
                body.Enabled,
                body.UnlockAccount == true,
                body.RoleCodes,
                over,
                hasOverride,
                GetRemoteIp(http),
                ct,
                newUsername: body.Username).ConfigureAwait(false);

            return ok ? Results.Ok(new { success = true, message = msg }) : Results.BadRequest(new { success = false, message = msg });
        });

        app.MapPost("/api/v1/users/{id:guid}/reset-password", async (HttpContext http, Guid id, ResetUserPasswordRequestDto? body, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.UserManage, ct).ConfigureAwait(false);
            if (err is not null) return err;

            (bool ok, string msg, string? tempPw) = await _store.ResetUserPasswordAsync(
                principal!,
                id,
                GetRemoteIp(http),
                ct,
                customPassword: body?.CustomPassword,
                mustChangePassword: body?.MustChangePassword).ConfigureAwait(false);
            return ok ? Results.Ok(new { success = true, message = msg, oneTimePassword = tempPw }) : Results.BadRequest(new { success = false, message = msg });
        });

        app.MapGet("/api/v1/mcp/tokens", async (HttpContext http, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? err) = await RequireAnyPermissionAsync(
                http,
                [RbacPermissions.McpTokenIssueSelf, RbacPermissions.McpTokenManageAll, RbacPermissions.UserManage],
                ct).ConfigureAwait(false);
            if (err is not null) return err;

            IReadOnlyList<McpTokenEntity> tokens = await _store.ListMcpTokensAsync(principal!, ct).ConfigureAwait(false);
            return Results.Ok(new
            {
                allowedRolesToIssue = RbacPolicyEvaluator.GetAllowedMcpRolesToIssue(principal!.RoleCodes, principal.EffectivePermissions),
                tokens = tokens.Select(static t => new
                {
                    t.Id,
                    t.Name,
                    t.PreferredPlayerName,
                    t.OwnerUsername,
                    t.TokenPrefix,
                    t.McpRole,
                    allowedRooms = string.IsNullOrWhiteSpace(t.AllowedRoomsJson) ? [] : (JsonSerializer.Deserialize<string[]>(t.AllowedRoomsJson) ?? []),
                    t.MaxSessions,
                    t.RequireApproval,
                    t.ExpiresAt,
                    t.RevokedAt,
                    t.LastUsedAt,
                    t.CreatedAt
                })
            });
        });

        app.MapPost("/api/v1/mcp/tokens", async (HttpContext http, IssueMcpTokenRequestDto body, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? err) = await RequireAnyPermissionAsync(
                http,
                [RbacPermissions.McpTokenIssueSelf, RbacPermissions.McpTokenManageAll],
                ct).ConfigureAwait(false);
            if (err is not null) return err;

            string? preferredName = !string.IsNullOrWhiteSpace(body.PreferredPlayerName)
                ? body.PreferredPlayerName.Trim()
                : (!string.IsNullOrWhiteSpace(body.Name) ? body.Name.Trim() : null);

            (bool ok, string msg, IssuedMcpTokenResult? issued) = await _store.IssueMcpTokenAsync(
                principal!,
                body.Name ?? string.Empty,
                body.McpRole ?? McpRoleCodes.Player,
                body.AllowedRooms,
                body.MaxSessions ?? 1,
                body.RequireApproval ?? true,
                body.ExpiresInHours,
                GetRemoteIp(http),
                ct,
                preferredPlayerName: preferredName).ConfigureAwait(false);

            if (!ok || issued is null)
                return Results.Json(new { success = false, message = msg }, statusCode: StatusCodes.Status403Forbidden);

            return Results.Ok(new
            {
                success = true,
                message = msg,
                rawToken = issued.RawToken,
                token = new
                {
                    issued.Entity.Id,
                    issued.Entity.Name,
                    issued.Entity.PreferredPlayerName,
                    issued.Entity.TokenPrefix,
                    issued.Entity.McpRole,
                    issued.Entity.MaxSessions,
                    issued.Entity.RequireApproval,
                    issued.Entity.ExpiresAt
                }
            });
        });

        app.MapPatch("/api/v1/mcp/tokens/{id:guid}", async (HttpContext http, Guid id, UpdateMcpTokenNicknameDto body, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? err) = await RequireAnyPermissionAsync(
                http,
                [RbacPermissions.McpTokenIssueSelf, RbacPermissions.McpTokenManageAll, RbacPermissions.UserManage],
                ct).ConfigureAwait(false);
            if (err is not null) return err;

            (bool ok, string msg, string? updatedName) = await _store.UpdateMcpTokenNicknameAsync(
                principal!,
                id,
                body.PreferredPlayerName,
                GetRemoteIp(http),
                ct).ConfigureAwait(false);

            return ok
                ? Results.Ok(new { success = true, message = msg, preferredPlayerName = updatedName })
                : Results.BadRequest(new { success = false, message = msg });
        });

        app.MapDelete("/api/v1/mcp/tokens/{id:guid}", async (HttpContext http, Guid id, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? principal, IResult? err) = await RequireAnyPermissionAsync(
                http,
                [RbacPermissions.McpTokenIssueSelf, RbacPermissions.McpTokenManageAll, RbacPermissions.UserManage],
                ct).ConfigureAwait(false);
            if (err is not null) return err;

            bool ok = await _store.RevokeMcpTokenAsync(principal!, id, GetRemoteIp(http), ct).ConfigureAwait(false);
            if (ok)
                _coordinator.RevokeMcpTokenSessions(id);
            return ok ? Results.Ok(new { success = true }) : Results.NotFound(new { success = false, message = "Token 不存在或无权吊销。" });
        });

        // 单端口融合模式下同端口挂载前台玩家门户公开路由（/api/portal/* 与 /ws/public）
        _embeddedPortal?.MapPortalCoreRoutes(app);

        // 挂载标准 Streamable HTTP MCP 服务端（支持全角色 player / spectator / diy_creator / admin，兼容 Bearer Header 与 ?key=）
        app.MapMcpStreamableHttp(_mcpEngine, _store);

        app.MapGet("/api/v1/audit-logs", async (HttpContext http, int? limit, string? actor, string? action, string? result, CancellationToken ct) =>
        {
            (AuthenticatedPrincipal? _, IResult? err) = await RequirePermissionAsync(http, RbacPermissions.AuditViewAll, ct).ConfigureAwait(false);
            if (err is not null) return err;

            IReadOnlyList<AuditLogEntity> logs = await _store.QueryAuditLogsAsync(limit ?? 100, actor, action, result, ct).ConfigureAwait(false);
            return Results.Ok(new { logs });
        });

        // ==================== 8. WebSocket 实时事件推送（支持一次性 Ticket） ====================
        app.Map("/ws/events", async (HttpContext http, CancellationToken ct) =>
        {
            if (!http.WebSockets.IsWebSocketRequest)
                return Results.BadRequest(new { message = "Expected WebSocket upgrade request." });

            AuthenticatedPrincipal? principal = null;
            if (http.Request.Query.TryGetValue("ticket", out var ticketVals) && !string.IsNullOrWhiteSpace(ticketVals.ToString()))
            {
                string? rawSessionFromTicket = await _store.Cache.ConsumeOneTimeTicketAsync("ws", ticketVals.ToString(), ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(rawSessionFromTicket))
                    principal = await _store.ValidateSessionAsync(rawSessionFromTicket, ct).ConfigureAwait(false);
            }
            else
            {
                (string? rawToken, _) = ExtractRawSessionToken(http);
                principal = await _store.ValidateSessionAsync(rawToken, ct).ConfigureAwait(false);
            }

            if (principal is null || principal.MustChangePassword)
                return Results.Unauthorized();

            using WebSocket ws = await http.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
            await _eventHub.HandleConnectionAsync(ws, principal, ct).ConfigureAwait(false);
            return Results.Empty;
        });

        // ==================== 9. SPA 静态路由回退 ====================
        if (staticRoot is not null)
        {
            string indexHtmlPath = Path.Combine(staticRoot, "index.html");
            app.MapFallback(async (HttpContext http) =>
            {
                PathString path = http.Request.Path;
                if (path.StartsWithSegments("/api") ||
                    path.StartsWithSegments("/ws") ||
                    path.StartsWithSegments("/mcp") ||
                    path.StartsWithSegments("/healthz"))
                {
                    http.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                if (File.Exists(indexHtmlPath))
                {
                    http.Response.ContentType = "text/html; charset=utf-8";
                    await http.Response.SendFileAsync(indexHtmlPath, http.RequestAborted).ConfigureAwait(false);
                }
                else
                {
                    http.Response.StatusCode = StatusCodes.Status404NotFound;
                }
            });
        }
    }

    // ==================== 鉴权、CSRF、RBAC 与隐私脱敏辅助 ====================

    private (string? RawToken, bool IsBearer) ExtractRawSessionToken(HttpContext http)
    {
        string authHeader = http.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return (authHeader["Bearer ".Length..].Trim(), true);

        if (http.Request.Cookies.TryGetValue(_options.SessionCookieName, out string? cookieVal) && !string.IsNullOrWhiteSpace(cookieVal))
            return (cookieVal.Trim(), false);

        return (null, false);
    }

    private async Task<AuthenticatedPrincipal?> RevalidateCurrentPrincipalAsync(HttpContext http, CancellationToken ct)
    {
        (string? rawToken, _) = ExtractRawSessionToken(http);
        return await _store.ValidateSessionAsync(rawToken, ct).ConfigureAwait(false);
    }

    private async Task<(AuthenticatedPrincipal? Principal, IResult? ErrorResult)> AuthenticateAsync(
        HttpContext http,
        bool allowWhenMustChangePassword,
        CancellationToken ct)
    {
        (string? rawToken, bool isBearer) = ExtractRawSessionToken(http);
        if (string.IsNullOrWhiteSpace(rawToken))
            return (null, Results.Json(new { error = "unauthorized", message = "请先登录管理面板。" }, statusCode: StatusCodes.Status401Unauthorized));

        AuthenticatedPrincipal? principal = await _store.ValidateSessionAsync(rawToken, ct).ConfigureAwait(false);
        if (principal is null)
            return (null, Results.Json(new { error = "session_expired", message = "登录会话已失效或已过期，请重新登录。" }, statusCode: StatusCodes.Status401Unauthorized));

        // 基于 Cookie 认证的写操作强制校验 X-CSRF-Token
        if (!isBearer && IsMutatingMethod(http.Request.Method))
        {
            string csrfHeader = http.Request.Headers["X-CSRF-Token"].ToString();
            if (string.IsNullOrWhiteSpace(csrfHeader) || !TokenCrypto.FixedTimeEqualsString(csrfHeader, principal.CsrfToken))
            {
                return (null, Results.Json(new { error = "csrf_invalid", message = "CSRF 令牌缺失或校验失败。" }, statusCode: StatusCodes.Status403Forbidden));
            }
        }

        if (!allowWhenMustChangePassword && principal.MustChangePassword)
        {
            return (null, Results.Json(new
            {
                error = "must_change_password",
                message = "出于安全要求，首次登录或重置密码后必须先完成修改密码。"
            }, statusCode: StatusCodes.Status403Forbidden));
        }

        return (principal, null);
    }

    private async Task<(AuthenticatedPrincipal? Principal, IResult? ErrorResult)> RequirePermissionAsync(
        HttpContext http,
        string permission,
        CancellationToken ct)
    {
        (AuthenticatedPrincipal? principal, IResult? authErr) = await AuthenticateAsync(http, allowWhenMustChangePassword: false, ct).ConfigureAwait(false);
        if (authErr is not null)
            return (null, authErr);

        if (!principal!.HasPermission(permission))
        {
            await _store.AppendAuditAsync("panel_user", principal.UserId.ToString(), principal.Username, http.Request.Path, permission, null, GetRemoteIp(http), "permission_denied", null, ct).ConfigureAwait(false);
            return (null, Results.Json(new { error = "permission_denied", message = $"缺少所需权限：{permission}" }, statusCode: StatusCodes.Status403Forbidden));
        }

        return (principal, null);
    }

    private async Task<(AuthenticatedPrincipal? Principal, IResult? ErrorResult)> RequireAnyPermissionAsync(
        HttpContext http,
        string[] permissions,
        CancellationToken ct)
    {
        (AuthenticatedPrincipal? principal, IResult? authErr) = await AuthenticateAsync(http, allowWhenMustChangePassword: false, ct).ConfigureAwait(false);
        if (authErr is not null)
            return (null, authErr);

        if (!RbacPolicyEvaluator.HasAnyPermission(principal!.EffectivePermissions, permissions))
        {
            string joined = string.Join("|", permissions);
            await _store.AppendAuditAsync("panel_user", principal.UserId.ToString(), principal.Username, http.Request.Path, joined, null, GetRemoteIp(http), "permission_denied", null, ct).ConfigureAwait(false);
            return (null, Results.Json(new { error = "permission_denied", message = $"缺少所需权限：{joined}" }, statusCode: StatusCodes.Status403Forbidden));
        }

        return (principal, null);
    }

    private bool CanManageRoom(AuthenticatedPrincipal principal, string roomName, string globalPermission)
    {
        if (principal.HasPermission(globalPermission))
            return true;

        if (principal.HasPermission(RbacPermissions.RoomCreateOwn))
        {
            RoomActivityState? state = _coordinator.TryGetRoomState(roomName);
            return state?.OwnerUserId == principal.UserId;
        }

        return false;
    }

    private RawRoomSummaryDto[] BuildRawRoomSummaries()
    {
        IReadOnlyList<RoomSnapshot> snapshots = _serverHost.GetRooms();
        List<RawRoomSummaryDto> list = new(snapshots.Count);
        foreach (RoomSnapshot snap in snapshots)
        {
            if (_serverHost.Server.TryGetRoom(snap.RoomName, out Game? g) && g is not null)
                list.Add(BuildSingleRawRoomSummary(g));
        }
        return list.ToArray();
    }

    private RawRoomSummaryDto BuildSingleRawRoomSummary(Game g)
    {
        RoomSnapshot snap = g.CreateSnapshot();
        RoomActivityState? activity = _coordinator.TryGetRoomState(g.RoomName);
        List<RawSlotDto> slots = new(g.Players.Length);
        for (int i = 0; i < g.Players.Length; i++)
        {
            Player? p = g.Players[i];
            slots.Add(new RawSlotDto(
                SeatIndex: i,
                TeamId: g.IsTag ? (i >= 2 ? 1 : 0) : i,
                PlayerName: p?.Name,
                IsReady: i < g.IsReady.Length && g.IsReady[i],
                IsHost: p is not null && g.IsHost(p),
                SeatOrigin: p?.SeatOrigin ?? "empty",
                ClientIp: p?.RemoteAddress));
        }

        Player[] observerPlayers = RoomConfigHelper.SnapshotObservers(g);
        List<RawObserverDto> observers = new(observerPlayers.Length);
        for (int i = 0; i < observerPlayers.Length; i++)
        {
            Player obs = observerPlayers[i];
            observers.Add(new RawObserverDto(
                obs.Name ?? $"Observer{i + 1}",
                obs.SeatOrigin,
                obs.RemoteAddress));
        }

        string fullHostPw = activity?.RawJoinPassword ?? RoomConfigHelper.FormatPublicRuleHostPassword(g, _serverHost.Server.Options.Game);

        return new RawRoomSummaryDto(
            RoomName: snap.RoomName,
            State: snap.State.ToString(),
            Mode: snap.Mode,
            Region: snap.Region,
            MasterRule: snap.MasterRule,
            StartLp: snap.StartLp,
            Team0Lp: snap.Team0Lp,
            Team1Lp: snap.Team1Lp,
            TurnCount: snap.TurnCount,
            DuelCount: g.DuelCount,
            CurrentPlayer: g.CurrentPlayer,
            DuelistCount: snap.DuelistCount,
            MaxDuelists: snap.MaxDuelists,
            ObserverCount: snap.ObserverCount,
            HostPlayerName: snap.HostPlayerName,
            RandomDeckMode: snap.RandomDeckMode.ToString(),
            IsSuddenDeath: snap.IsSuddenDeath,
            BanlistName: g.Banlist?.Name,
            NoCheckDeck: g.NoCheckDeck,
            NoShuffleDeck: g.NoShuffleDeck,
            JoinPassword: activity?.RawJoinPassword,
            FullHostPassword: fullHostPw,
            OwnerUsername: activity?.OwnerUsername,
            CreatedAtUtc: activity?.CreatedAtUtc ?? DateTime.UtcNow,
            Slots: slots,
            Observers: observers);
    }

    private static object SanitizeRoomSummary(RawRoomSummaryDto raw, AuthenticatedPrincipal principal)
    {
        return new
        {
            raw.RoomName,
            raw.State,
            raw.Mode,
            raw.Region,
            raw.MasterRule,
            raw.StartLp,
            raw.Team0Lp,
            raw.Team1Lp,
            raw.TurnCount,
            raw.DuelCount,
            raw.CurrentPlayer,
            raw.DuelistCount,
            raw.MaxDuelists,
            raw.ObserverCount,
            raw.HostPlayerName,
            raw.RandomDeckMode,
            raw.IsSuddenDeath,
            raw.BanlistName,
            raw.NoCheckDeck,
            raw.NoShuffleDeck,
            joinPassword = principal.IsStreamerOnly ? null : raw.JoinPassword,
            hostPassword = principal.IsStreamerOnly ? null : raw.RoomName,
            fullHostPassword = principal.IsStreamerOnly ? null : raw.FullHostPassword,
            raw.OwnerUsername,
            raw.CreatedAtUtc,
            slots = raw.Slots.Select(s => new
            {
                s.SeatIndex,
                s.TeamId,
                s.PlayerName,
                s.IsReady,
                s.IsHost,
                s.SeatOrigin,
                clientIp = IpPrivacyHelper.SanitizeIp(s.ClientIp, principal.CanViewIp, principal.IsStreamerOnly)
            }),
            observers = raw.Observers.Select(o => new
            {
                o.PlayerName,
                o.SeatOrigin,
                clientIp = IpPrivacyHelper.SanitizeIp(o.ClientIp, principal.CanViewIp, principal.IsStreamerOnly)
            })
        };
    }

    private object SanitizeRoomDetail(Game game, RoomActivityState? activity, AuthenticatedPrincipal principal)
    {
        RawRoomSummaryDto raw = BuildSingleRawRoomSummary(game);
        return new
        {
            summary = SanitizeRoomSummary(raw, principal),
            startHand = game.StartHand,
            drawCount = game.DrawCount,
            timer = game.Timer,
            suddenDeathTargetTurn = game.SuddenDeathTargetTurn,
            extraScripts = game.ExtraScripts,
            chats = activity?.GetChats() ?? [],
            publicEvents = activity?.GetPublicEvents() ?? []
        };
    }

    private static object SanitizeDuelRecord(DuelRecordEntity record, AuthenticatedPrincipal principal)
    {
        return new
        {
            record.Id,
            replayCode = $"R#{record.Id}",
            record.RoomName,
            record.Mode,
            record.Region,
            record.MasterRule,
            record.StartLp,
            record.RandomDeckMode,
            record.IsSuddenDeath,
            record.DuelCount,
            record.TurnCount,
            record.WinnerTeam,
            record.WinReason,
            record.StartedAt,
            record.EndedAt,
            hasYrp = !string.IsNullOrWhiteSpace(record.YrpPath),
            players = record.Players.OrderBy(static p => p.SeatIndex).Select(p => new
            {
                p.SeatIndex,
                p.TeamId,
                p.PlayerName,
                p.SeatType,
                p.IsWinner,
                clientIp = IpPrivacyHelper.SanitizeIp(p.ClientIp, principal.CanViewIp, principal.IsStreamerOnly)
            })
        };
    }

    private static object BuildUserSelfDto(AuthenticatedPrincipal p)
    {
        return new
        {
            p.SessionId,
            p.UserId,
            p.Username,
            p.DisplayName,
            p.MustChangePassword,
            p.CsrfToken,
            p.IsStreamerOnly,
            p.CanViewIp,
            roleCodes = p.RoleCodes,
            permissions = p.EffectivePermissions,
            allowedMcpRolesToIssue = RbacPolicyEvaluator.GetAllowedMcpRolesToIssue(p.RoleCodes, p.EffectivePermissions)
        };
    }

    private object[] SearchCardsCore(string query, int maxResults, bool diyOnly)
    {
        INamedCardRepository? namedRepo = _options.NamedCardRepositoryProvider?.Invoke();
        ICardRepository cardRepo = _serverHost.Server.CardRepository;

        if (int.TryParse(query, out int exactCode) && exactCode > 0)
        {
            if (namedRepo is not null && namedRepo.TryGetCard(exactCode, out NamedCard nc))
            {
                if (!diyOnly || (nc.Ot & 0x4) != 0)
                    return [FormatNamedCard(nc)];
            }
            else if (cardRepo.TryGetCard(exactCode, out Card c))
            {
                if (!diyOnly || (c.Ot & 0x4) != 0)
                    return [new { c.Id, name = $"#{c.Id}", desc = string.Empty, c.Ot, c.Type, c.Level, c.Attack, c.Defense, c.Race, c.Attribute }];
            }
        }

        if (namedRepo is null)
            return [];

        IEnumerable<NamedCard> source = namedRepo.GetAllCards();
        if (diyOnly)
            source = source.Where(static c => (c.Ot & 0x4) != 0);

        if (!string.IsNullOrWhiteSpace(query))
        {
            source = source.Where(c =>
                c.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                c.Description.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        return source.Take(maxResults).Select(FormatNamedCard).ToArray();
    }

    private static object FormatNamedCard(NamedCard c)
    {
        return new
        {
            c.Id,
            c.Name,
            desc = c.Description,
            c.Ot,
            isDiy = (c.Ot & 0x4) != 0,
            c.Type,
            c.Level,
            c.Attack,
            c.Defense,
            c.Race,
            c.Attribute
        };
    }

    private static DiyCardEntry MapCardDtoToEntry(DiyCardEntryDto? dto, int forcedCardId)
    {
        long parsedSetcode = dto?.Setcode ?? 0;
        if (dto?.SetcodeHex is { Length: > 0 } hex)
        {
            string cleaned = hex.Trim();
            if (cleaned.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                cleaned = cleaned[2..];
            if (long.TryParse(cleaned, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out long hexVal))
                parsedSetcode = hexVal;
        }

        return new DiyCardEntry
        {
            Id = forcedCardId,
            Ot = dto?.Ot ?? 0x4,
            Alias = dto?.Alias ?? 0,
            Setcode = parsedSetcode,
            Type = dto?.Type ?? 0x21,
            Level = dto?.Level ?? 4,
            LScale = dto?.LScale ?? 0,
            RScale = dto?.RScale ?? 0,
            Race = dto?.Race ?? 1,
            Attribute = dto?.Attribute ?? 1,
            Attack = dto?.Attack ?? 0,
            Defense = dto?.Defense ?? 0,
            LinkMarker = dto?.LinkMarker ?? 0,
            Category = dto?.Category ?? 0,
            Name = dto?.Name?.Trim() ?? string.Empty,
            Desc = dto?.Desc ?? string.Empty,
            Strings = dto?.Strings ?? []
        };
    }

    private static List<string> PreAuditProposalPayload(DiyProposalPayloadDto payload)
    {
        List<string> errors = new();

        if (payload.Cards is not null)
        {
            foreach (ProposedCardChangeDto item in payload.Cards)
            {
                if (item.Card?.Id is null or <= 0)
                {
                    errors.Add("提案中的卡片必须包含正整数卡密 (id > 0)。");
                    continue;
                }
                if (!string.IsNullOrEmpty(item.LuaScript))
                {
                    LuaScriptSecurityAudit audit = DiyPackageManager.AuditLuaScript(item.LuaScript);
                    if (!audit.IsSafe)
                        errors.AddRange(audit.Violations.Select(v => $"c{item.Card.Id}.lua: {v}"));
                }
            }
        }

        if (payload.Scripts is not null)
        {
            foreach (ProposedScriptFileDto script in payload.Scripts)
            {
                if (string.IsNullOrWhiteSpace(script.FileName))
                {
                    errors.Add("脚本文件名不能为空。");
                    continue;
                }
                LuaScriptSecurityAudit audit = DiyPackageManager.AuditLuaScript(script.Content ?? string.Empty);
                if (!audit.IsSafe)
                    errors.AddRange(audit.Violations.Select(v => $"{script.FileName}: {v}"));
            }
        }

        if (payload.Decks is not null)
        {
            foreach (ProposedDeckFileDto deck in payload.Decks)
            {
                if (string.IsNullOrWhiteSpace(deck.Name) || string.IsNullOrWhiteSpace(deck.YdkContent))
                {
                    errors.Add("提案中的卡组必须包含名称与有效 .ydk 内容。");
                    continue;
                }
                YdkDeckData parsed = YdkSerializer.Parse(deck.YdkContent, deck.Name);
                if (parsed.Main.Count == 0 && parsed.Extra.Count == 0)
                    errors.Add($"卡组 '{deck.Name}' 不包含任何有效卡片。");
            }
        }

        return errors;
    }

    private (bool Applied, string? ErrorMessage) ApplyDiyProposalToDisk(DiyProposalEntity proposal)
    {
        try
        {
            if (!DiyPackageManager.IsValidPackageId(proposal.PackageId))
                return (false, $"非法包 ID '{proposal.PackageId}'。");

            DiyProposalPayloadDto? payload = JsonSerializer.Deserialize<DiyProposalPayloadDto>(
                proposal.DiffJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (payload is null)
                return (false, "提案 DiffJson 为空或格式错误。");

            List<string> auditErrors = PreAuditProposalPayload(payload);
            if (auditErrors.Count > 0)
                return (false, string.Join("; ", auditErrors));

            string pkgDir = _diyManager.GetPackageDirectory(proposal.PackageId);
            if (!Directory.Exists(pkgDir) || !File.Exists(Path.Combine(pkgDir, "manifest.json")))
            {
                _diyManager.UpsertManifest(new DiyPackageManifest
                {
                    Id = proposal.PackageId,
                    Name = proposal.PackageId,
                    Enabled = true
                });
            }

            if (payload.StringsConf is not null)
                _diyManager.SaveStringsConf(proposal.PackageId, payload.StringsConf);

            if (payload.Cards is not null)
            {
                foreach (ProposedCardChangeDto item in payload.Cards)
                {
                    int id = item.Card!.Id!.Value;
                    DiyCardEntry entry = MapCardDtoToEntry(item.Card, id);
                    CardDatabaseValidationResult res = _diyManager.UpsertCard(proposal.PackageId, entry, item.LuaScript);
                    if (!res.IsValid)
                        return (false, $"c{id}: {res.ErrorMessage}");
                }
            }

            if (payload.Scripts is not null)
            {
                foreach (ProposedScriptFileDto script in payload.Scripts)
                {
                    string fileName = script.FileName!.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)
                        ? script.FileName!
                        : script.FileName! + ".lua";
                    LuaScriptSecurityAudit audit = _diyManager.SaveScript(proposal.PackageId, fileName, script.Content ?? string.Empty);
                    if (!audit.IsSafe)
                        return (false, string.Join("; ", audit.Violations));
                }
            }

            if (payload.Decks is not null)
            {
                foreach (ProposedDeckFileDto deck in payload.Decks)
                    _diyManager.SaveDeck(proposal.PackageId, deck.Name!, deck.YdkContent!);
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static string BuildTemplateRulePrefix(string? template, string? customRulePrefix)
    {
        string basePrefix = (template?.Trim().ToLowerInvariant()) switch
        {
            "blind_box" => "RD,MR5,NC",
            "mirror_box" => "RDM,MR5,NC",
            "chaos" => "CHAOS,MR5,NC",
            "cricket_1v1" => "MR5,TM300,NC",
            "cricket_1v1_mirror" => "RDM,MR5,TM300,NC",
            "cricket_2v2" => "2V2,RDM,MR5,TM300,NC",
            "cricket_2v2_chaos" => "2V2,CHAOS,MR5,TM300,NC",
            "diy_test" => "DIY,MR5,NC,NF",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(customRulePrefix))
            return basePrefix;
        if (string.IsNullOrWhiteSpace(basePrefix))
            return customRulePrefix.Trim();

        return $"{basePrefix},{customRulePrefix.Trim()}";
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

    private static bool IsMutatingMethod(string method)
    {
        return HttpMethods.IsPost(method) ||
               HttpMethods.IsPut(method) ||
               HttpMethods.IsPatch(method) ||
               HttpMethods.IsDelete(method);
    }

    private static string? GetRemoteIp(HttpContext http)
    {
        IPAddress? addr = http.Connection.RemoteIpAddress;
        if (addr is null)
            return null;
        return addr.IsIPv4MappedToIPv6 ? addr.MapToIPv4().ToString() : addr.ToString();
    }

    private void HandleCoordinatorEvent(string eventType, object payload)
    {
        _ = _store.Cache.FlushNamespaceAsync("rooms");
        _eventHub.BroadcastEvent(eventType, payload, RbacPermissions.RoomView);
    }

    private void HandleBufferedLogAdded(BufferedLogEntry entry)
    {
        _eventHub.BroadcastLog(entry);
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
        _coordinator.OnPublicEventPublished -= HandleCoordinatorEvent;
        _logBuffer.OnEntryAdded -= HandleBufferedLogAdded;
        _eventHub.Dispose();
        if (_embeddedPortal is not null)
            await _embeddedPortal.DisposeAsync().ConfigureAwait(false);
        await StopAsync().ConfigureAwait(false);
    }

    // ==================== 请求与内部 DTO ====================

    private sealed record RawSlotDto(
        int SeatIndex,
        int TeamId,
        string? PlayerName,
        bool IsReady,
        bool IsHost,
        string SeatOrigin,
        string? ClientIp);

    private sealed record RawObserverDto(
        string PlayerName,
        string SeatOrigin,
        string? ClientIp);

    private sealed record RawRoomSummaryDto(
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
        int CurrentPlayer,
        int DuelistCount,
        int MaxDuelists,
        int ObserverCount,
        string? HostPlayerName,
        string RandomDeckMode,
        bool IsSuddenDeath,
        string? BanlistName,
        bool NoCheckDeck,
        bool NoShuffleDeck,
        string? JoinPassword,
        string FullHostPassword,
        string? OwnerUsername,
        DateTime CreatedAtUtc,
        IReadOnlyList<RawSlotDto> Slots,
        IReadOnlyList<RawObserverDto> Observers);
}

public sealed record LoginRequestDto(string? Username, string? Password);
public sealed record ChangePasswordRequestDto(string? CurrentPassword, string? NewPassword, string? NewUsername = null, string? DisplayName = null);
public sealed record FlushCacheRequestDto(string? Namespace);
public sealed record MaintenanceRequestDto(bool Enabled, string? Reason);
public sealed record MessageRequestDto(string? Message);
public sealed record CreateRoomRequestDto(
    string? RoomName,
    string? Template,
    string? RulePrefix,
    bool IssuePlayerTokens,
    int? AiTokenCount = null,
    string[]? AiPlayerNames = null,
    int? Mode = null,
    int? Region = null,
    int? MasterRule = null,
    int? BanlistIndex = null,
    bool? NoBanlist = null,
    int? StartLp = null,
    int? StartHand = null,
    int? DrawCount = null,
    int? GameTimer = null,
    string? RandomDeckMode = null,
    int? ChaosMainDeckSize = null,
    bool? NoCheckDeck = null,
    bool? NoShuffleDeck = null,
    bool? EnablePriority = null,
    bool? SuddenDeath = null);
public sealed record UpdateRoomRulesRequestDto(
    string? PresetRuleString,
    int? Mode,
    int? Region,
    int? MasterRule,
    int? StartLp,
    int? StartHand,
    int? DrawCount,
    int? GameTimer,
    int? BanlistIndex,
    bool? NoCheckDeck,
    bool? NoShuffleDeck,
    string? RandomDeckMode);
public sealed record SuddenDeathRequestDto(bool Enabled, int? ExtraTurns);
public sealed record SpawnBotRequestDto(string? Deck, string? Dialog, int? Count, bool FillAll);
public sealed record KickPlayerRequestDto(int? SlotIndex, string? PlayerName);
public sealed record CreateBanRequestDto(string? TargetType, string? TargetValue, string? Reason, int? DurationMinutes);
public sealed record UpsertTournamentDeckDto(string? TournamentTag, string? PlayerName, string? YdkContent);
public sealed record UploadPoolDeckDto(string? Name, string? Category, string? YdkContent, string[]? Tags);
public sealed record ChaosPreviewRequestDto(int? MainSize, bool? PureChaos, bool? DiyOnly);
public sealed record UpsertDiyManifestDto(string? Id, string? Name, string? Version, string? Author, bool? Enabled, int? Priority);
public sealed record UpsertRoleRequestDto(string? Name, string? Description, string[]? Permissions);
public sealed record CreateUserRequestDto(
    string? Username,
    string? DisplayName,
    string? InitialPassword,
    string[]? RoleCodes,
    string[]? GrantPermissions,
    string[]? RevokePermissions,
    bool? MustChangePassword);
public sealed record UpdateUserRequestDto(
    string? Username,
    string? DisplayName,
    bool? Enabled,
    bool? UnlockAccount,
    string[]? RoleCodes,
    string[]? GrantPermissions,
    string[]? RevokePermissions);
public sealed record ResetUserPasswordRequestDto(
    string? CustomPassword,
    bool? MustChangePassword);
public sealed record IssueMcpTokenRequestDto(
    string? Name,
    string? McpRole,
    string[]? AllowedRooms,
    int? MaxSessions,
    bool? RequireApproval,
    int? ExpiresInHours,
    string? PreferredPlayerName = null);

public sealed record UpdateMcpTokenNicknameDto(string? PreferredPlayerName);

public sealed record DiyCardEntryDto(
    int? Id,
    int? Ot,
    int? Alias,
    long? Setcode,
    string? SetcodeHex,
    int? Type,
    int? Level,
    int? LScale,
    int? RScale,
    int? Race,
    int? Attribute,
    int? Attack,
    int? Defense,
    int? LinkMarker,
    int? Category,
    string? Name,
    string? Desc,
    string[]? Strings);

public sealed record UpsertDiyCardRequestDto(
    DiyCardEntryDto? Card,
    string? LuaScript,
    bool? AutoReload);

public sealed record SaveStringsConfDto(string? Content);

public sealed record SandboxTestCardRequestDto(
    DiyCardEntryDto? Card,
    string? LuaScript,
    bool? TestExecuteFirstActivation);

public sealed record ProposedCardChangeDto(
    DiyCardEntryDto? Card,
    string? LuaScript);

public sealed record ProposedScriptFileDto(
    string? FileName,
    string? Content);

public sealed record ProposedDeckFileDto(
    string? Name,
    string? YdkContent);

public sealed record DiyProposalPayloadDto(
    ProposedCardChangeDto[]? Cards,
    ProposedScriptFileDto[]? Scripts,
    ProposedDeckFileDto[]? Decks,
    string? StringsConf);

public sealed record SubmitDiyProposalRequestDto(
    string? PackageId,
    string? Title,
    DiyProposalPayloadDto? Payload);

public sealed record ReviewDiyProposalRequestDto(
    bool Approve,
    string? Comment);
