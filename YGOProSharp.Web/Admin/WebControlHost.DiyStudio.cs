using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using YGOProSharp.Core;
using YGOProSharp.Core.Cards;
using YGOProSharp.Persistence;
using YGOProSharp.Persistence.Entities;
using YGOProSharp.Persistence.Security;
using YGOProSharp.Server;
using YGOProSharp.Web.Security;

namespace YGOProSharp.Web;

public sealed partial class WebControlHost
{
    private void MapAdminDiyStudioEndpoints(WebApplication app)
    {
        app.MapGet("/api/v1/diy/packages", () =>
        {
            IReadOnlyList<DiyPackageManifest> packages = _diyManager.ListPackages();
            var items = packages.Select(p => new
            {
                manifest = p,
                validation = _diyManager.ValidatePackage(p.Id)
            }).ToArray();

            string[] trashEntries = Directory.Exists(_diyManager.TrashDirectory)
                ? Directory.GetDirectories(_diyManager.TrashDirectory).Select(Path.GetFileName).Where(static s => s is not null).Cast<string>().ToArray()
                : [];

            return Results.Ok(new { packages = items, trash = trashEntries });
        }).RequireAdminAuth(RbacPermissions.DiyView);

        app.MapPost("/api/v1/diy/packages", async (HttpContext http, UpsertDiyManifestDto body, CancellationToken ct) =>
        {
            AuthenticatedPrincipal principal = http.GetRequiredPrincipal();
            if (!DiyPackageManager.IsValidPackageId(body.Id))
                return Results.BadRequest(new { message = "包 ID 仅允许字母、数字、点、下划线和连字符（1~64 位）。" });

            DiyPackageManifest saved = _diyManager.UpsertManifest(new DiyPackageManifest
            {
                Id = body.Id!.Trim(),
                Name = string.IsNullOrWhiteSpace(body.Name) ? body.Id.Trim() : body.Name.Trim(),
                Version = string.IsNullOrWhiteSpace(body.Version) ? "1.0.0" : body.Version.Trim(),
                Author = body.Author?.Trim() ?? principal.Username,
                Enabled = body.Enabled ?? true,
                Priority = body.Priority ?? 100
            });

            await _store.AppendAuditAsync("panel_user", principal.UserId.ToString(), principal.Username, "diy.package_upsert", RbacPermissions.DiyEdit, saved.Id, http.GetRemoteIpNormalized(), "ok", null, ct).ConfigureAwait(false);
            return Results.Ok(new { success = true, manifest = saved });
        }).RequireAdminAuth(RbacPermissions.DiyEdit);

        app.MapPost("/api/v1/diy/packages/{id}/upload", async (HttpContext http, string id, CancellationToken ct) =>
        {
            AuthenticatedPrincipal principal = http.GetRequiredPrincipal();
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
                        LuaScriptSecurityAudit audit = _diyManager.SaveScript(id, fileName, Encoding.UTF8.GetString(bytes));
                        if (!audit.IsSafe) errors.AddRange(audit.Violations.Select(v => $"{fileName}: {v}"));
                        else savedFiles.Add(fileName);
                    }
                    else if (ext == ".ydk")
                    {
                        _diyManager.SaveDeck(id, fileName, Encoding.UTF8.GetString(bytes));
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
                        string confText = Encoding.UTF8.GetString(bytes);
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
            await _store.AppendAuditAsync("panel_user", principal.UserId.ToString(), principal.Username, "diy.upload", RbacPermissions.DiyUpload, id, http.GetRemoteIpNormalized(), errors.Count == 0 ? "ok" : "partial_error", JsonSerializer.Serialize(new { savedFiles, errors }), ct).ConfigureAwait(false);

            return Results.Ok(new
            {
                success = errors.Count == 0,
                savedFiles,
                errors,
                validation = report
            });
        }).RequireAdminAuth(RbacPermissions.DiyUpload);

        app.MapDelete("/api/v1/diy/packages/{id}", async (HttpContext http, string id, CancellationToken ct) =>
        {
            AuthenticatedPrincipal principal = http.GetRequiredPrincipal();
            try
            {
                string trashName = _diyManager.MoveToTrash(id);
                await _store.AppendAuditAsync("panel_user", principal.UserId.ToString(), principal.Username, "diy.move_to_trash", RbacPermissions.DiyEdit, id, http.GetRemoteIpNormalized(), "ok", trashName, ct).ConfigureAwait(false);
                return Results.Ok(new { success = true, trashEntry = trashName });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message });
            }
        }).RequireAdminAuth(RbacPermissions.DiyEdit);

        app.MapPost("/api/v1/diy/trash/{trashEntry}/restore", async (HttpContext http, string trashEntry, CancellationToken ct) =>
        {
            AuthenticatedPrincipal principal = http.GetRequiredPrincipal();
            try
            {
                string restoredId = _diyManager.RestoreFromTrash(trashEntry);
                await _store.AppendAuditAsync("panel_user", principal.UserId.ToString(), principal.Username, "diy.restore_trash", RbacPermissions.DiyEdit, restoredId, http.GetRemoteIpNormalized(), "ok", null, ct).ConfigureAwait(false);
                return Results.Ok(new { success = true, packageId = restoredId });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message });
            }
        }).RequireAdminAuth(RbacPermissions.DiyEdit);

        app.MapPost("/api/v1/diy/reload", async (HttpContext http, CancellationToken ct) =>
        {
            AuthenticatedPrincipal principal = http.GetRequiredPrincipal();
            ServerReloadResult reloadResult = _serverHost.Server.ReloadAssets();
            await _store.Cache.FlushNamespaceAsync("cards", ct).ConfigureAwait(false);
            await _store.AppendAuditAsync("panel_user", principal.UserId.ToString(), principal.Username, "diy.reload", RbacPermissions.DiyReload, $"v{reloadResult.CatalogVersion}", http.GetRemoteIpNormalized(), "ok", null, ct).ConfigureAwait(false);
            _eventHub.BroadcastEvent("diy.reloaded", reloadResult, RbacPermissions.DashboardView);
            return Results.Ok(new { success = true, reload = reloadResult });
        }).RequireAdminAuth(RbacPermissions.DiyReload);

        app.MapGet("/api/v1/diy/conflicts", () =>
        {
            DiyGlobalConflictReport report = _diyManager.AnalyzeAllPackageConflicts(_serverHost.Server.CardRepository);
            return Results.Ok(report);
        }).RequireAdminAuth(RbacPermissions.DiyView);

        app.MapGet("/api/v1/diy/packages/{id}/cards", (string id) =>
        {
            if (!DiyPackageManager.IsValidPackageId(id))
                return Results.BadRequest(new { message = "非法 DIY 包 ID。" });

            return Results.Ok(new
            {
                packageId = id,
                cards = _diyManager.ListPackageCards(id),
                stringsConf = _diyManager.ReadStringsConf(id)
            });
        }).RequireAdminAuth(RbacPermissions.DiyView);

        app.MapGet("/api/v1/diy/packages/{id}/cards/{cardId:int}", (string id, int cardId) =>
        {
            if (!DiyPackageManager.IsValidPackageId(id))
                return Results.BadRequest(new { message = "非法 DIY 包 ID。" });

            DiyCardDetail? detail = _diyManager.GetCardDetail(id, cardId);
            return detail is not null
                ? Results.Ok(new { detail })
                : Results.NotFound(new { message = $"包 '{id}' 中不存在卡密 {cardId}。" });
        }).RequireAdminAuth(RbacPermissions.DiyView);

        app.MapPut("/api/v1/diy/packages/{id}/cards/{cardId:int}", async (HttpContext http, string id, int cardId, UpsertDiyCardRequestDto body, CancellationToken ct) =>
        {
            AuthenticatedPrincipal principal = http.GetRequiredPrincipal();
            if (!DiyPackageManager.IsValidPackageId(id) || cardId <= 0)
                return Results.BadRequest(new { message = "包 ID 或卡密无效。" });

            try
            {
                DiyCardEntry entry = MapCardDtoToEntry(body.Card, cardId);
                CardDatabaseValidationResult dbVal = _diyManager.UpsertCard(id, entry, body.LuaScript);
                if (!dbVal.IsValid)
                    return Results.BadRequest(new { success = false, message = dbVal.ErrorMessage ?? "写入工作副本校验未通过。" });

                ServerReloadResult? reload = null;
                if (body.AutoReload == true && principal.HasPermission(RbacPermissions.DiyReload))
                {
                    reload = _serverHost.Server.ReloadAssets();
                    await _store.Cache.FlushNamespaceAsync("cards", ct).ConfigureAwait(false);
                    _eventHub.BroadcastEvent("diy.reloaded", reload, RbacPermissions.DashboardView);
                }

                DiyPackageValidationReport pkgReport = _diyManager.ValidatePackage(id);
                DiyCardDetail? detail = _diyManager.GetCardDetail(id, cardId);
                await _store.AppendAuditAsync("panel_user", principal.UserId.ToString(), principal.Username, "diy.card_upsert", RbacPermissions.DiyEdit, $"{id}:{cardId}", http.GetRemoteIpNormalized(), "ok", null, ct).ConfigureAwait(false);

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
        }).RequireAdminAuth(RbacPermissions.DiyEdit);

        app.MapDelete("/api/v1/diy/packages/{id}/cards/{cardId:int}", async (HttpContext http, string id, int cardId, CancellationToken ct) =>
        {
            AuthenticatedPrincipal principal = http.GetRequiredPrincipal();
            if (!DiyPackageManager.IsValidPackageId(id) || cardId <= 0)
                return Results.BadRequest(new { message = "包 ID 或卡密无效。" });

            bool deleted = _diyManager.DeleteCard(id, cardId, deleteAssets: true);
            if (!deleted)
                return Results.NotFound(new { success = false, message = "未找到对应卡片记录。" });

            await _store.AppendAuditAsync("panel_user", principal.UserId.ToString(), principal.Username, "diy.card_delete", RbacPermissions.DiyEdit, $"{id}:{cardId}", http.GetRemoteIpNormalized(), "ok", null, ct).ConfigureAwait(false);
            return Results.Ok(new { success = true });
        }).RequireAdminAuth(RbacPermissions.DiyEdit);

        app.MapPost("/api/v1/diy/packages/{id}/cards/{cardId:int}/image", async (HttpContext http, string id, int cardId, CancellationToken ct) =>
        {
            AuthenticatedPrincipal principal = http.GetRequiredPrincipal();
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
                await _store.AppendAuditAsync("panel_user", principal.UserId.ToString(), principal.Username, "diy.card_image_upload", RbacPermissions.DiyEdit, $"{id}:{cardId}", http.GetRemoteIpNormalized(), "ok", savedFileName, ct).ConfigureAwait(false);
                return Results.Ok(new { success = true, fileName = savedFileName });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message });
            }
        }).RequireAdminAuth(RbacPermissions.DiyEdit, RbacPermissions.DiyUpload);

        app.MapGet("/api/v1/diy/packages/{id}/cards/{cardId:int}/image", (HttpContext http, string id, int cardId) =>
        {
            if (!DiyPackageManager.IsValidPackageId(id) || cardId <= 0)
                return Results.BadRequest(new { message = "包 ID 或卡密无效。" });

            var image = _diyManager.TryReadCardImage(id, cardId);
            if (image is null)
                return Results.NotFound(new { message = "暂无卡图。" });

            http.Response.Headers.CacheControl = "no-cache";
            return Results.Bytes(image.Value.Bytes, image.Value.ContentType);
        }).RequireAdminAuth(RbacPermissions.DiyView);

        app.MapPut("/api/v1/diy/packages/{id}/strings", async (HttpContext http, string id, SaveStringsConfDto body, CancellationToken ct) =>
        {
            AuthenticatedPrincipal principal = http.GetRequiredPrincipal();
            if (!DiyPackageManager.IsValidPackageId(id))
                return Results.BadRequest(new { message = "非法 DIY 包 ID。" });

            StringsConfSummary summary = _diyManager.SaveStringsConf(id, body.Content ?? string.Empty);
            await _store.AppendAuditAsync("panel_user", principal.UserId.ToString(), principal.Username, "diy.strings_save", RbacPermissions.DiyEdit, id, http.GetRemoteIpNormalized(), "ok", null, ct).ConfigureAwait(false);
            return Results.Ok(new { success = true, stringsConf = summary });
        }).RequireAdminAuth(RbacPermissions.DiyEdit);

        app.MapGet("/api/v1/diy/packages/{id}/export.ypk", async (HttpContext http, string id, CancellationToken ct) =>
        {
            AuthenticatedPrincipal principal = http.GetRequiredPrincipal();
            if (!DiyPackageManager.IsValidPackageId(id))
                return Results.BadRequest(new { message = "非法 DIY 包 ID。" });

            try
            {
                byte[] ypkBytes = _diyManager.ExportPackageAsYpk(id);
                await _store.AppendAuditAsync("panel_user", principal.UserId.ToString(), principal.Username, "diy.export_ypk", RbacPermissions.DiyView, id, http.GetRemoteIpNormalized(), "ok", null, ct).ConfigureAwait(false);
                return Results.File(ypkBytes, "application/zip", $"{id}.ypk");
            }
            catch (DirectoryNotFoundException ex)
            {
                return Results.NotFound(new { message = ex.Message });
            }
        }).RequireAdminAuth(RbacPermissions.DiyView);

        app.MapPost("/api/v1/diy/sandbox-test", async (HttpContext http, SandboxTestCardRequestDto body, CancellationToken ct) =>
        {
            AuthenticatedPrincipal principal = http.GetRequiredPrincipal();
            int cardId = body.Card?.Id is > 0 ? body.Card.Id.Value : 99000001;
            DiyCardEntry draftCard = MapCardDtoToEntry(body.Card, cardId);
            CardSandboxResult sandboxResult = _serverHost.Server.RunCardSandboxTest(new CardSandboxRequest(
                draftCard,
                body.LuaScript,
                body.TestExecuteFirstActivation ?? true));

            await _store.AppendAuditAsync(
                "panel_user",
                principal.UserId.ToString(),
                principal.Username,
                "diy.sandbox_test",
                RbacPermissions.DiySandboxTest,
                $"c{cardId}",
                http.GetRemoteIpNormalized(),
                sandboxResult.Success ? "ok" : "error",
                null,
                ct).ConfigureAwait(false);

            return Results.Ok(new { result = sandboxResult });
        }).RequireAdminAuth(RbacPermissions.DiySandboxTest);

        app.MapGet("/api/v1/diy/proposals", async (string? status, string? packageId, int? limit, CancellationToken ct) =>
        {
            IReadOnlyList<DiyProposalEntity> proposals = await _store.ListDiyProposalsAsync(status, packageId, limit ?? 80, ct).ConfigureAwait(false);
            return Results.Ok(new { proposals });
        }).RequireAdminAuth(RbacPermissions.DiyView);

        app.MapPost("/api/v1/diy/proposals", async (HttpContext http, SubmitDiyProposalRequestDto body, CancellationToken ct) =>
        {
            AuthenticatedPrincipal principal = http.GetRequiredPrincipal();
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
                proposerName: principal.Username,
                actorType: "panel_user",
                actorId: principal.UserId.ToString(),
                ip: http.GetRemoteIpNormalized(),
                cancellationToken: ct).ConfigureAwait(false);

            _eventHub.BroadcastEvent("diy.proposal_created", new { created.Id, created.PackageId, created.Title }, RbacPermissions.DiyView);
            return Results.Ok(new { success = true, proposal = created });
        }).RequireAdminAuth(RbacPermissions.DiyEdit, RbacPermissions.DiyUpload);

        app.MapPost("/api/v1/diy/proposals/{proposalId:guid}/review", async (HttpContext http, Guid proposalId, ReviewDiyProposalRequestDto body, CancellationToken ct) =>
        {
            AuthenticatedPrincipal principal = http.GetRequiredPrincipal();
            ServerReloadResult? reloadResult = null;
            (bool ok, string msg, DiyProposalEntity? updated) = await _store.ReviewDiyProposalAsync(
                principal,
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
                http.GetRemoteIpNormalized(),
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
        }).RequireAdminAuth(RbacPermissions.DiyReviewProposal);
    }

    private static DiyCardEntry MapCardDtoToEntry(DiyCardEntryDto? dto, int forcedCardId)
    {
        long parsedSetcode = dto?.Setcode ?? 0;
        if (dto?.SetcodeHex is { Length: > 0 } hex)
        {
            string cleaned = hex.Trim();
            if (cleaned.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                cleaned = cleaned[2..];
            if (long.TryParse(cleaned, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long hexVal))
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
}
