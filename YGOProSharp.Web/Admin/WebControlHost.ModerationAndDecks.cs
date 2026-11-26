using Microsoft.AspNetCore.Http;
using YGOProSharp.Abstractions.Ocg.Enums;
using YGOProSharp.Core;
using YGOProSharp.Core.Cards;
using YGOProSharp.Persistence;
using YGOProSharp.Persistence.Entities;
using YGOProSharp.Persistence.Security;
using YGOProSharp.Web.Routing;
using YGOProSharp.Web.Security;

namespace YGOProSharp.Web;

public sealed partial class WebControlHost
{
    [HttpGet("/api/v1/bans")]
    [AdminAuth(RbacPermissions.BanManage)]
    private async Task<IResult> ListBansAsync(CancellationToken ct)
    {
        IReadOnlyList<ActiveBanRule> bans = await _store.GetActiveBansAsync(ct).ConfigureAwait(false);
        return Results.Ok(new { bans });
    }

    [HttpPost("/api/v1/bans")]
    [AdminAuth(RbacPermissions.BanManage)]
    private async Task<IResult> CreateBanAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        CreateBanRequestDto body,
        CancellationToken ct)
    {
        (bool ok, string msg, BanEntity? ban) = await _store.CreateBanAsync(
            principal,
            body.TargetType ?? "ip",
            body.TargetValue ?? string.Empty,
            body.Reason ?? "违反服务器规则",
            body.DurationMinutes,
            http.GetRemoteIpNormalized(),
            ct).ConfigureAwait(false);

        return ok ? Results.Ok(new { success = true, message = msg, ban }) : Results.BadRequest(new { success = false, message = msg });
    }

    [HttpDelete("/api/v1/bans/{id:guid}")]
    [AdminAuth(RbacPermissions.BanManage)]
    private async Task<IResult> DeleteBanAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        Guid id,
        CancellationToken ct)
    {
        bool ok = await _store.RemoveBanAsync(principal, id, http.GetRemoteIpNormalized(), ct).ConfigureAwait(false);
        return ok ? Results.Ok(new { success = true }) : Results.NotFound(new { success = false, message = "封禁记录不存在。" });
    }

    [HttpGet("/api/v1/tournament-decks")]
    [AdminAuth(RbacPermissions.TournamentDeckRead)]
    private async Task<IResult> ListTournamentDecksAsync(string? tag, CancellationToken ct)
    {
        IReadOnlyList<TournamentDeckEntity> decks = await _store.ListTournamentDecksAsync(tag, ct).ConfigureAwait(false);
        return Results.Ok(new { decks });
    }

    [HttpPost("/api/v1/tournament-decks")]
    [AdminAuth(RbacPermissions.TournamentDeckWrite)]
    private async Task<IResult> UpsertTournamentDeckAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        UpsertTournamentDeckDto body,
        CancellationToken ct)
    {
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
    }

    [HttpGet("/api/v1/replays")]
    [AdminAuth(RbacPermissions.DuelLogView, RbacPermissions.DuelLogViewOwn)]
    private async Task<IResult> ListReplaysAsync(
        AuthenticatedPrincipal principal,
        int? limit,
        long? beforeId,
        string? q,
        CancellationToken ct)
    {
        IReadOnlyList<DuelRecordEntity> records = await _store.ListDuelRecordsAsync(limit ?? 50, beforeId, q, ct).ConfigureAwait(false);
        object[] sanitized = records.Select(r => SanitizeDuelRecord(r, principal)).ToArray();
        return Results.Ok(new { records = sanitized });
    }

    [HttpGet("/api/v1/replays/{id:long}/detail")]
    [AdminAuth(RbacPermissions.DuelLogView, RbacPermissions.DuelLogViewOwn)]
    private async Task<IResult> GetAdminReplayDetailAsync(long id, CancellationToken ct)
    {
        object? detail = await _mcpEngine.AppService.GetArchivedReplayDetailAsync(id, includeFullExtraAndAllMain: true, ct).ConfigureAwait(false);
        return detail is null
            ? Results.NotFound(new { message = $"未找到已完结归档的对局记录 R#{id}（进行中对局不开放全视角复盘）。" })
            : Results.Ok(detail);
    }

    [HttpGet("/api/v1/replays/{id:long}.yrp")]
    [AdminAuth(RbacPermissions.ReplayDownload)]
    private async Task<IResult> DownloadReplayYrpAsync(long id, CancellationToken ct)
    {
        byte[]? yrp = await _store.GetReplayYrpBytesAsync(id, ct).ConfigureAwait(false);
        if (yrp is null || yrp.Length == 0)
            return Results.NotFound(new { message = $"未找到录像 R#{id} 的 .yrp 文件。" });

        return Results.File(yrp, "application/octet-stream", $"replay_{id}.yrp");
    }

    [HttpGet("/api/v1/decks/presets")]
    [AdminAuth(RbacPermissions.DeckPoolView, RbacPermissions.CardSearch)]
    private IResult ListAdminDeckPresets()
    {
        IReadOnlyList<PresetDeckEntry> all = _mcpEngine.AppService.Presets.GetCombinedPresetDecks(_serverHost.Server);
        return Results.Ok(new
        {
            presets = all.Select(static d => new
            {
                d.Id,
                d.Name,
                d.HasDiyCards,
                mainCount = d.Data.Main.Count,
                extraCount = d.Data.Extra.Count,
                tags = d.Data.Tags
            }).ToArray()
        });
    }

    [HttpGet("/api/v1/decks/evaluations")]
    [AdminAuth(RbacPermissions.DeckPoolView, RbacPermissions.CardSearch)]
    private async Task<IResult> ListAdminDeckEvaluationsAsync(int? limit, CancellationToken ct)
    {
        IReadOnlyList<DeckEvaluationEntity> list = await _store.ListDeckEvaluationsAsync(Math.Clamp(limit ?? 20, 1, 50), ct).ConfigureAwait(false);
        return Results.Ok(new
        {
            evaluations = list.Select(Mcp.Services.DeckAndReplayAppService.FormatDeckEvaluationDto).ToArray()
        });
    }

    [HttpPost("/api/v1/decks/analyze")]
    [AdminAuth(RbacPermissions.DeckPoolView, RbacPermissions.CardSearch)]
    private async Task<IResult> AnalyzeAdminDeckAsync(PortalAnalyzeDeckRequestDto body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.YdkContent) && string.IsNullOrWhiteSpace(body.PresetName))
        {
            if (!string.IsNullOrWhiteSpace(body.DeckHash))
            {
                DeckEvaluationEntity? byHashOnly = await _store.GetLatestDeckEvaluationByHashAsync(body.DeckHash, ct).ConfigureAwait(false);
                if (byHashOnly is null)
                    return Results.NotFound(new { message = $"尚未收录 DeckHash={body.DeckHash} 的评测数据。" });
                return Results.Ok(new
                {
                    sourceType = byHashOnly.SourceType,
                    profile = (object?)null,
                    evaluation = Mcp.Services.DeckAndReplayAppService.FormatDeckEvaluationDto(byHashOnly)
                });
            }

            return Results.BadRequest(new { message = "请选择一套预设卡组或粘贴 .ydk 文本内容。" });
        }

        (bool ok, string err, YdkDeckData? target, string sourceType, _) =
            await _mcpEngine.AppService.ResolveTargetDeckAsync(body.PresetName, body.YdkContent, body.DeckName, null, 0, ct).ConfigureAwait(false);
        if (!ok || target is null)
        {
            return sourceType == "preset"
                ? Results.NotFound(new { message = err })
                : Results.BadRequest(new { message = err });
        }

        (DeckStaticProfile profile, object? evaluation) = await _mcpEngine.AppService.AnalyzeDeckAsync(target, ct).ConfigureAwait(false);
        return Results.Ok(new
        {
            sourceType,
            profile,
            evaluation
        });
    }

    [HttpGet("/api/v1/decks/pool")]
    [AdminAuth(RbacPermissions.DeckPoolView)]
    private IResult ListDeckPool()
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
    }

    [HttpPost("/api/v1/decks/pool")]
    [AdminAuth(RbacPermissions.DeckPoolManage)]
    private async Task<IResult> UploadPoolDeckAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        UploadPoolDeckDto body,
        CancellationToken ct)
    {
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
    }

    [HttpPost("/api/v1/decks/chaos-preview")]
    [AdminAuth(RbacPermissions.DeckPoolView)]
    private IResult GenerateChaosPreview(ChaosPreviewRequestDto? body)
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
    }

    [HttpGet("/api/v1/cards/search")]
    [AdminAuth(RbacPermissions.CardSearch)]
    private async Task<IResult> SearchCardsAsync(string? q, int? limit, bool? diyOnly, CancellationToken ct)
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
