using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using YGOProSharp.Core;
using YGOProSharp.Core.Cards;
using YGOProSharp.Mcp.Services;
using YGOProSharp.Persistence.Entities;
using YGOProSharp.Web.Routing;
using YGOProSharp.Web.Security;

namespace YGOProSharp.Web;

public sealed partial class UserPortalHost
{
    [HttpGet("/api/portal/replays", Group = "PortalCore")]
    private async Task<IResult> ListPortalReplaysAsync(int? limit, string? player, CancellationToken ct)
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
    }

    [HttpGet("/api/portal/replays/{id:long}.yrp", Group = "PortalCore")]
    private async Task<IResult> DownloadPortalReplayYrpAsync(long id, CancellationToken ct)
    {
        byte[]? yrp = await _store.GetReplayYrpBytesAsync(id, ct).ConfigureAwait(false);
        return yrp is null
            ? Results.NotFound(new { message = $"录像 R#{id} 不存在。" })
            : Results.File(yrp, "application/octet-stream", $"replay_{id}.yrp");
    }

    [HttpGet("/api/portal/replays/{id:long}/detail", Group = "PortalCore")]
    private async Task<IResult> GetPortalReplayDetailAsync(long id, CancellationToken ct)
    {
        object? detail = await _mcpEngine.AppService.GetArchivedReplayDetailAsync(id, includeFullExtraAndAllMain: false, ct).ConfigureAwait(false);
        return detail is null
            ? Results.NotFound(new { message = $"未找到已完结归档的对局记录 R#{id}（进行中对局不公开全视角复盘）。" })
            : Results.Ok(detail);
    }

    [HttpGet("/api/portal/cards/search", Group = "PortalCore")]
    private async Task<IResult> SearchPortalCardsAsync(string? q, int? limit, bool? diyOnly, CancellationToken ct)
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
    }

    [HttpGet("/api/portal/diy/packages", Group = "PortalCore")]
    private IResult ListPortalDiyPackages()
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
    }

    [HttpGet("/api/portal/diy/export-all.ypk", Group = "PortalCore")]
    private IResult ExportPortalAllDiyYpk()
    {
        try
        {
            byte[] ypk = _diyManager.ExportAllEnabledPackagesAsYpk();
            return Results.File(ypk, "application/zip", "ygoprosharp-all-diy.ypk");
        }
        catch (InvalidOperationException)
        {
            return Results.NotFound(new { message = "当前暂无已启用的 DIY 扩展包。" });
        }
    }

    [HttpGet("/api/portal/diy/packages/{id}/export.ypk", Group = "PortalCore")]
    private IResult ExportPortalDiyYpk(string id)
    {
        if (!DiyPackageManager.IsValidPackageId(id))
            return Results.BadRequest(new { message = "非法包 ID。" });

        DiyPackageManifest? pkg = _diyManager.ListPackages(enabledOnly: true)
            .FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        if (pkg is null)
            return Results.NotFound(new { message = "未找到已启用的 DIY 扩展包。" });

        byte[] ypk = _diyManager.ExportPackageAsYpk(pkg.Id);
        return Results.File(ypk, "application/zip", $"{pkg.Id}.ypk");
    }

    [HttpGet("/api/portal/diy/packages/{id}/cards/{cardId:int}/image", Group = "PortalCore")]
    private IResult GetPortalDiyCardImage(HttpContext http, string id, int cardId)
    {
        if (!DiyPackageManager.IsValidPackageId(id) || cardId <= 0)
            return Results.BadRequest();

        var img = _diyManager.TryReadCardImage(id, cardId);
        if (img is null)
            return Results.NotFound();

        http.Response.Headers.CacheControl = "public, max-age=300";
        return Results.Bytes(img.Value.Bytes, img.Value.ContentType);
    }

    [HttpPost("/api/portal/decks/chaos-preview", Group = "PortalCore")]
    private IResult PreviewPortalChaosDeck(ChaosPreviewRequestDto? body)
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
    }

    [HttpGet("/api/portal/decks/presets", Group = "PortalCore")]
    private IResult ListPortalDeckPresets()
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

    [HttpGet("/api/portal/decks/evaluations", Group = "PortalCore")]
    private async Task<IResult> ListPortalDeckEvaluationsAsync(int? limit, CancellationToken ct)
    {
        IReadOnlyList<DeckEvaluationEntity> list = await _store.ListDeckEvaluationsAsync(Math.Clamp(limit ?? 20, 1, 50), ct).ConfigureAwait(false);
        return Results.Ok(new
        {
            evaluations = list.Select(DeckAndReplayAppService.FormatDeckEvaluationDto).ToArray()
        });
    }

    [HttpPost("/api/portal/decks/analyze", Group = "PortalCore")]
    private async Task<IResult> AnalyzePortalDeckAsync(PortalAnalyzeDeckRequestDto body, CancellationToken ct)
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
                    evaluation = DeckAndReplayAppService.FormatDeckEvaluationDto(byHashOnly)
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

    [HttpPost("/api/portal/player/vpass", Group = "PortalCore")]
    [RateLimit(WebRateLimitPolicies.PortalVpass)]
    private async Task<IResult> BindPortalPlayerVpassAsync(PortalVpassRequestDto body, CancellationToken ct)
    {
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
    }

    [HttpPost("/api/portal/tournament-decks", Group = "PortalCore")]
    [RateLimit(WebRateLimitPolicies.PortalTournamentDeck)]
    private async Task<IResult> SubmitPortalTournamentDeckAsync(
        HttpContext http,
        UpsertTournamentDeckDto body,
        CancellationToken ct)
    {
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
                http.GetRemoteIpNormalized() ?? "unknown",
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
    }

    [HttpPost("/api/portal/diy/proposals", Group = "PortalCore")]
    [RateLimit(WebRateLimitPolicies.PortalDiyProposal)]
    private async Task<IResult> SubmitPortalDiyProposalAsync(
        HttpContext http,
        PortalSubmitProposalDto body,
        CancellationToken ct)
    {
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
            ip: http.GetRemoteIpNormalized() ?? "unknown",
            cancellationToken: ct).ConfigureAwait(false);

        return Results.Ok(new
        {
            success = true,
            proposalId = created.Id,
            status = created.Status,
            message = "投稿已进入待审队列，待服主在管理后台沙盒验证并批准后即可全服热重载生效！"
        });
    }

    [HttpPost("/api/portal/diy/proposals/import-ypk", Group = "PortalCore")]
    [RateLimit(WebRateLimitPolicies.PortalDiyProposal)]
    private async Task<IResult> SubmitPortalYpkProposalAsync(
        HttpContext http,
        CancellationToken ct)
    {
        if (!http.Request.HasFormContentType)
            return Results.BadRequest(new { message = "请使用 multipart/form-data 上传 .ypk 扩展包文件。" });

        IFormCollection form = await http.Request.ReadFormAsync(ct).ConfigureAwait(false);
        if (form.Files.Count == 0)
            return Results.BadRequest(new { message = "请选择要上传投稿的 .ypk 或 .zip 文件。" });

        IFormFile file = form.Files[0];
        string ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".ypk" or ".zip"))
            return Results.BadRequest(new { message = "仅支持上传标准 .ypk 或 .zip 扩展包。" });

        if (file.Length > 24L * 1024 * 1024)
            return Results.BadRequest(new { message = "社区单次投稿 .ypk 体积上限为 24MB。" });

        string rawPkgId = form["packageId"].ToString().Trim();
        string pkgId = !string.IsNullOrWhiteSpace(rawPkgId)
            ? rawPkgId
            : DiyPackageManager.SanitizePackageIdFromFileName(file.FileName);
        if (!DiyPackageManager.IsValidPackageId(pkgId))
            return Results.BadRequest(new { message = "目标包 ID 仅允许字母、数字、点、下划线和连字符。" });

        string rawAuthor = form["authorName"].ToString().Trim();
        string author = string.IsNullOrWhiteSpace(rawAuthor) ? "community_player" : rawAuthor;

        string tempRoot = Path.Combine(Path.GetTempPath(), "ygp_portal_ypk_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            using MemoryStream ms = new();
            await file.CopyToAsync(ms, ct).ConfigureAwait(false);
            ms.Position = 0;

            DiyPackageManager tempMgr = new(tempRoot, new SqliteCardDatabaseManager());
            DiyYpkImportResult importRes = tempMgr.ImportPackageFromYpk(
                pkgId,
                ms,
                author: author,
                mergeIntoExisting: false,
                maxUncompressedBytes: 24L * 1024 * 1024);

            if (!importRes.IsSuccess)
            {
                return Results.BadRequest(new
                {
                    success = false,
                    message = "YPK 扩展包未通过安全与结构预检：" + string.Join("; ", importRes.Errors),
                    errors = importRes.Errors
                });
            }

            IReadOnlyList<DiyCardSummary> summaries = tempMgr.ListPackageCards(pkgId);
            if (summaries.Count > 200)
                return Results.BadRequest(new { message = $"社区单次 .ypk 投稿最多支持 200 张卡片（当前包内共 {summaries.Count} 张），大包请联系管理员在后台直接导入。" });

            HashSet<string> cardScriptNames = new(StringComparer.OrdinalIgnoreCase);
            List<ProposedCardChangeDto> proposedCards = new(summaries.Count);
            List<ProposedCardImageDto> proposedImages = new();

            foreach (DiyCardSummary item in summaries)
            {
                DiyCardDetail? detail = tempMgr.GetCardDetail(pkgId, item.Card.Id);
                if (detail is null)
                    continue;

                DiyCardEntry c = detail.Card;
                if (!string.IsNullOrEmpty(detail.ScriptContent))
                    cardScriptNames.Add($"c{c.Id}.lua");

                proposedCards.Add(new ProposedCardChangeDto(
                    new DiyCardEntryDto(
                        c.Id,
                        c.Ot,
                        c.Alias,
                        c.Setcode,
                        $"0x{c.Setcode:x}",
                        c.Type,
                        c.Level,
                        c.LScale,
                        c.RScale,
                        c.Race,
                        c.Attribute,
                        c.Attack,
                        c.Defense,
                        c.LinkMarker,
                        c.Category,
                        c.Name,
                        c.Desc,
                        c.Strings.ToArray()),
                    detail.ScriptContent));

                if (proposedImages.Count < 40)
                {
                    var img = tempMgr.TryReadCardImage(pkgId, c.Id);
                    if (img is not null && img.Value.Bytes.Length <= 2 * 1024 * 1024)
                        proposedImages.Add(new ProposedCardImageDto(c.Id, Convert.ToBase64String(img.Value.Bytes)));
                }
            }

            string extractedDir = tempMgr.GetPackageDirectory(pkgId);
            List<ProposedScriptFileDto> extraScripts = new();
            string scriptDir = Path.Combine(extractedDir, "script");
            if (Directory.Exists(scriptDir))
            {
                foreach (string luaPath in Directory.EnumerateFiles(scriptDir, "*.lua", SearchOption.TopDirectoryOnly))
                {
                    string fn = Path.GetFileName(luaPath);
                    if (!cardScriptNames.Contains(fn))
                        extraScripts.Add(new ProposedScriptFileDto(fn, File.ReadAllText(luaPath, Encoding.UTF8)));
                }
            }

            List<ProposedDeckFileDto> decks = new();
            string decksDir = Path.Combine(extractedDir, "decks");
            if (Directory.Exists(decksDir))
            {
                foreach (string ydkPath in Directory.EnumerateFiles(decksDir, "*.ydk", SearchOption.TopDirectoryOnly))
                    decks.Add(new ProposedDeckFileDto(Path.GetFileNameWithoutExtension(ydkPath), File.ReadAllText(ydkPath, Encoding.UTF8)));
            }

            StringsConfSummary stringsSummary = tempMgr.ReadStringsConf(pkgId);
            string? stringsConf = string.IsNullOrWhiteSpace(stringsSummary.RawContent) ? null : stringsSummary.RawContent;

            DiyProposalPayloadDto payload = new(
                Cards: proposedCards.Count > 0 ? proposedCards.ToArray() : null,
                Scripts: extraScripts.Count > 0 ? extraScripts.ToArray() : null,
                Decks: decks.Count > 0 ? decks.ToArray() : null,
                StringsConf: stringsConf,
                Images: proposedImages.Count > 0 ? proposedImages.ToArray() : null);

            string archiveTitle = Path.GetFileNameWithoutExtension(file.FileName);
            DiyProposalEntity created = await _store.CreateDiyProposalAsync(
                pkgId,
                $"[YPK整包投稿:{author}] {archiveTitle} (卡:{proposedCards.Count} 脚本:{importRes.ImportedScripts} 图:{proposedImages.Count})",
                JsonSerializer.Serialize(payload),
                proposerTokenId: null,
                proposerName: author,
                actorType: "anonymous",
                actorId: null,
                ip: http.GetRemoteIpNormalized() ?? "unknown",
                cancellationToken: ct).ConfigureAwait(false);

            return Results.Ok(new
            {
                success = true,
                proposalId = created.Id,
                packageId = pkgId,
                cardCount = proposedCards.Count,
                scriptCount = importRes.ImportedScripts,
                imageCount = proposedImages.Count,
                deckCount = decks.Count,
                message = $"已成功解析 {file.FileName}（{proposedCards.Count} 张卡、{importRes.ImportedScripts} 个脚本、{proposedImages.Count} 张卡图）并投递至后台待审队列！"
            });
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // 忽略临时目录清理异常
            }
        }
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
}
