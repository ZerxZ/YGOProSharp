using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using YGOProSharp.Abstractions.Ocg.Enums;
using YGOProSharp.Core;
using YGOProSharp.Core.Cards;
using YGOProSharp.Persistence;
using YGOProSharp.Persistence.Entities;
using YGOProSharp.Persistence.Security;
using YGOProSharp.Web.Security;

namespace YGOProSharp.Web;

public sealed partial class WebControlHost
{
    private void MapAdminModerationAndDeckEndpoints(WebApplication app)
    {
        app.MapGet("/api/v1/bans", async (CancellationToken ct) =>
        {
            IReadOnlyList<ActiveBanRule> bans = await _store.GetActiveBansAsync(ct).ConfigureAwait(false);
            return Results.Ok(new { bans });
        }).RequireAdminAuth(RbacPermissions.BanManage);

        app.MapPost("/api/v1/bans", async (HttpContext http, CreateBanRequestDto body, CancellationToken ct) =>
        {
            AuthenticatedPrincipal principal = http.GetRequiredPrincipal();
            (bool ok, string msg, BanEntity? ban) = await _store.CreateBanAsync(
                principal,
                body.TargetType ?? "ip",
                body.TargetValue ?? string.Empty,
                body.Reason ?? "违反服务器规则",
                body.DurationMinutes,
                http.GetRemoteIpNormalized(),
                ct).ConfigureAwait(false);

            return ok ? Results.Ok(new { success = true, message = msg, ban }) : Results.BadRequest(new { success = false, message = msg });
        }).RequireAdminAuth(RbacPermissions.BanManage);

        app.MapDelete("/api/v1/bans/{id:guid}", async (HttpContext http, Guid id, CancellationToken ct) =>
        {
            AuthenticatedPrincipal principal = http.GetRequiredPrincipal();
            bool ok = await _store.RemoveBanAsync(principal, id, http.GetRemoteIpNormalized(), ct).ConfigureAwait(false);
            return ok ? Results.Ok(new { success = true }) : Results.NotFound(new { success = false, message = "封禁记录不存在。" });
        }).RequireAdminAuth(RbacPermissions.BanManage);

        app.MapGet("/api/v1/tournament-decks", async (string? tag, CancellationToken ct) =>
        {
            IReadOnlyList<TournamentDeckEntity> decks = await _store.ListTournamentDecksAsync(tag, ct).ConfigureAwait(false);
            return Results.Ok(new { decks });
        }).RequireAdminAuth(RbacPermissions.TournamentDeckRead);

        app.MapPost("/api/v1/tournament-decks", async (HttpContext http, UpsertTournamentDeckDto body, CancellationToken ct) =>
        {
            AuthenticatedPrincipal principal = http.GetRequiredPrincipal();
            if (string.IsNullOrWhiteSpace(body.PlayerName) || string.IsNullOrWhiteSpace(body.YdkContent))
                return Results.BadRequest(new { message = "选手名与卡组内容不能为空。" });

            YdkDeckData parsed = YdkSerializer.Parse(body.YdkContent, body.PlayerName);
            TournamentDeckEntity saved = await _store.UpsertTournamentDeckAsync(
                principal,
                body.TournamentTag ?? "default",
                body.PlayerName,
                body.YdkContent,
                parsed.Main.Count,
                parsed.Extra.Count,
                parsed.Side.Count,
                http.GetRemoteIpNormalized(),
                ct).ConfigureAwait(false);

            return Results.Ok(new { success = true, deck = saved });
        }).RequireAdminAuth(RbacPermissions.TournamentDeckWrite);

        app.MapGet("/api/v1/replays", async (HttpContext http, int? limit, long? beforeId, string? q, CancellationToken ct) =>
        {
            AuthenticatedPrincipal principal = http.GetRequiredPrincipal();
            IReadOnlyList<DuelRecordEntity> records = await _store.ListDuelRecordsAsync(limit ?? 50, beforeId, q, ct).ConfigureAwait(false);
            object[] sanitized = records.Select(r => SanitizeDuelRecord(r, principal)).ToArray();
            return Results.Ok(new { records = sanitized });
        }).RequireAdminAuth(RbacPermissions.DuelLogView, RbacPermissions.DuelLogViewOwn);

        app.MapGet("/api/v1/replays/{id:long}.yrp", async (long id, CancellationToken ct) =>
        {
            byte[]? yrp = await _store.GetReplayYrpBytesAsync(id, ct).ConfigureAwait(false);
            if (yrp is null || yrp.Length == 0)
                return Results.NotFound(new { message = $"未找到录像 R#{id} 的 .yrp 文件。" });

            return Results.File(yrp, "application/octet-stream", $"replay_{id}.yrp");
        }).RequireAdminAuth(RbacPermissions.ReplayDownload);

        app.MapGet("/api/v1/decks/pool", () =>
        {
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
        }).RequireAdminAuth(RbacPermissions.DeckPoolView);

        app.MapPost("/api/v1/decks/pool", async (HttpContext http, UploadPoolDeckDto body, CancellationToken ct) =>
        {
            AuthenticatedPrincipal principal = http.GetRequiredPrincipal();
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
            await _store.AppendAuditAsync("panel_user", principal.UserId.ToString(), principal.Username, "deck_pool.upload", RbacPermissions.DeckPoolManage, safeName, http.GetRemoteIpNormalized(), "ok", null, ct).ConfigureAwait(false);
            return Results.Ok(new { success = true, totalPoolDecks = _serverHost.Server.PresetDecks.Count });
        }).RequireAdminAuth(RbacPermissions.DeckPoolManage);

        app.MapPost("/api/v1/decks/chaos-preview", (ChaosPreviewRequestDto? body) =>
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
            int normalOrEffect = 0, highLevel = 0, spells = 0, traps = 0;
            foreach (int id in generated.Main)
            {
                if (_serverHost.Server.CardRepository.TryGetCard(id, out Card c))
                {
                    if ((c.Type & (int)CardType.Spell) != 0) spells++;
                    else if ((c.Type & (int)CardType.Trap) != 0) traps++;
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
        }).RequireAdminAuth(RbacPermissions.DeckPoolView);

        app.MapGet("/api/v1/cards/search", async (string? q, int? limit, bool? diyOnly, CancellationToken ct) =>
        {
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
        }).RequireAdminAuth(RbacPermissions.CardSearch);
    }

    private static object SanitizeDuelRecord(DuelRecordEntity record, AuthenticatedPrincipal principal) =>
        new
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

    private static object FormatNamedCard(NamedCard c) =>
        new
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
