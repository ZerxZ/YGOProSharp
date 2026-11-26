using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using YGOProSharp.Core;
using YGOProSharp.Core.Cards;
using YGOProSharp.Persistence;
using YGOProSharp.Persistence.Entities;
using YGOProSharp.Persistence.Security;
using YGOProSharp.Server;
using YGOProSharp.Web.Routing;
using YGOProSharp.Web.Security;

namespace YGOProSharp.Web;

public sealed partial class WebControlHost
{
    [HttpGet("/api/v1/diy/packages")]
    [AdminAuth(RbacPermissions.DiyView)]
    private IResult ListDiyPackages()
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
    }

    [HttpPost("/api/v1/diy/packages")]
    [AdminAuth(RbacPermissions.DiyEdit)]
    private async Task<IResult> UpsertDiyPackageManifestAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        UpsertDiyManifestDto body,
        CancellationToken ct)
    {
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
    }

    [HttpPost("/api/v1/diy/packages/{id}/upload")]
    [AdminAuth(RbacPermissions.DiyUpload)]
    private async Task<IResult> UploadDiyPackageFilesAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        string id,
        CancellationToken ct)
    {
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
                    DiyYpkImportResult ypkImport = _diyManager.ImportPackageFromYpk(
                        id,
                        ms,
                        author: principal.Username,
                        mergeIntoExisting: true);
                    if (!ypkImport.IsSuccess)
                    {
                        errors.AddRange(ypkImport.Errors.Select(e => $"{fileName}: {e}"));
                    }
                    else
                    {
                        savedFiles.Add($"{fileName} (cards:{ypkImport.TotalCards}, lua:{ypkImport.ImportedScripts}, pics:{ypkImport.ImportedImages}, ydk:{ypkImport.ImportedDecks})");
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
    }

    [HttpDelete("/api/v1/diy/packages/{id}")]
    [AdminAuth(RbacPermissions.DiyEdit)]
    private async Task<IResult> DeleteDiyPackageAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        string id,
        CancellationToken ct)
    {
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
    }

    [HttpPost("/api/v1/diy/trash/{trashEntry}/restore")]
    [AdminAuth(RbacPermissions.DiyEdit)]
    private async Task<IResult> RestoreDiyTrashAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        string trashEntry,
        CancellationToken ct)
    {
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
    }

    [HttpPost("/api/v1/diy/reload")]
    [AdminAuth(RbacPermissions.DiyReload)]
    private async Task<IResult> ReloadDiyAssetsAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        CancellationToken ct)
    {
        ServerReloadResult reloadResult = _serverHost.Server.ReloadAssets();
        await _store.Cache.FlushNamespaceAsync("cards", ct).ConfigureAwait(false);
        await _store.AppendAuditAsync("panel_user", principal.UserId.ToString(), principal.Username, "diy.reload", RbacPermissions.DiyReload, $"v{reloadResult.CatalogVersion}", http.GetRemoteIpNormalized(), "ok", null, ct).ConfigureAwait(false);
        _eventHub.BroadcastEvent("diy.reloaded", reloadResult, RbacPermissions.DashboardView);
        return Results.Ok(new { success = true, reload = reloadResult });
    }

    [HttpGet("/api/v1/diy/conflicts")]
    [AdminAuth(RbacPermissions.DiyView)]
    private IResult AnalyzeDiyConflicts() =>
        Results.Ok(_diyManager.AnalyzeAllPackageConflicts(_serverHost.Server.CardRepository));

    [HttpGet("/api/v1/diy/packages/{id}/cards")]
    [AdminAuth(RbacPermissions.DiyView)]
    private IResult ListDiyPackageCards(string id)
    {
        if (!DiyPackageManager.IsValidPackageId(id))
            return Results.BadRequest(new { message = "非法 DIY 包 ID。" });

        return Results.Ok(new
        {
            packageId = id,
            cards = _diyManager.ListPackageCards(id),
            stringsConf = _diyManager.ReadStringsConf(id)
        });
    }

    [HttpGet("/api/v1/diy/packages/{id}/cards/{cardId:int}")]
    [AdminAuth(RbacPermissions.DiyView)]
    private IResult GetDiyCardDetail(string id, int cardId)
    {
        if (!DiyPackageManager.IsValidPackageId(id))
            return Results.BadRequest(new { message = "非法 DIY 包 ID。" });

        DiyCardDetail? detail = _diyManager.GetCardDetail(id, cardId);
        return detail is not null
            ? Results.Ok(new { detail })
            : Results.NotFound(new { message = $"包 '{id}' 中不存在卡密 {cardId}。" });
    }

    [HttpPut("/api/v1/diy/packages/{id}/cards/{cardId:int}")]
    [AdminAuth(RbacPermissions.DiyEdit)]
    private async Task<IResult> UpsertDiyCardAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        string id,
        int cardId,
        UpsertDiyCardRequestDto body,
        CancellationToken ct)
    {
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
    }

    [HttpDelete("/api/v1/diy/packages/{id}/cards/{cardId:int}")]
    [AdminAuth(RbacPermissions.DiyEdit)]
    private async Task<IResult> DeleteDiyCardAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        string id,
        int cardId,
        CancellationToken ct)
    {
        if (!DiyPackageManager.IsValidPackageId(id) || cardId <= 0)
            return Results.BadRequest(new { message = "包 ID 或卡密无效。" });

        bool deleted = _diyManager.DeleteCard(id, cardId, deleteAssets: true);
        if (!deleted)
            return Results.NotFound(new { success = false, message = "未找到对应卡片记录。" });

        await _store.AppendAuditAsync("panel_user", principal.UserId.ToString(), principal.Username, "diy.card_delete", RbacPermissions.DiyEdit, $"{id}:{cardId}", http.GetRemoteIpNormalized(), "ok", null, ct).ConfigureAwait(false);
        return Results.Ok(new { success = true });
    }

    [HttpPost("/api/v1/diy/packages/{id}/cards/{cardId:int}/image")]
    [AdminAuth(RbacPermissions.DiyEdit, RbacPermissions.DiyUpload)]
    private async Task<IResult> UploadDiyCardImageAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        string id,
        int cardId,
        CancellationToken ct)
    {
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
    }

    [HttpGet("/api/v1/diy/packages/{id}/cards/{cardId:int}/image")]
    [AdminAuth(RbacPermissions.DiyView)]
    private IResult GetDiyCardImage(HttpContext http, string id, int cardId)
    {
        if (!DiyPackageManager.IsValidPackageId(id) || cardId <= 0)
            return Results.BadRequest(new { message = "包 ID 或卡密无效。" });

        var image = _diyManager.TryReadCardImage(id, cardId);
        if (image is null)
            return Results.NotFound(new { message = "暂无卡图。" });

        http.Response.Headers.CacheControl = "no-cache";
        return Results.Bytes(image.Value.Bytes, image.Value.ContentType);
    }

    [HttpPut("/api/v1/diy/packages/{id}/strings")]
    [AdminAuth(RbacPermissions.DiyEdit)]
    private async Task<IResult> SaveDiyStringsConfAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        string id,
        SaveStringsConfDto body,
        CancellationToken ct)
    {
        if (!DiyPackageManager.IsValidPackageId(id))
            return Results.BadRequest(new { message = "非法 DIY 包 ID。" });

        StringsConfSummary summary = _diyManager.SaveStringsConf(id, body.Content ?? string.Empty);
        await _store.AppendAuditAsync("panel_user", principal.UserId.ToString(), principal.Username, "diy.strings_save", RbacPermissions.DiyEdit, id, http.GetRemoteIpNormalized(), "ok", null, ct).ConfigureAwait(false);
        return Results.Ok(new { success = true, stringsConf = summary });
    }

    [HttpPost("/api/v1/diy/inspect-ypk")]
    [AdminAuth(RbacPermissions.DiyView, RbacPermissions.DiyUpload, RbacPermissions.DiyEdit)]
    private async Task<IResult> InspectAdminYpkAsync(HttpContext http, CancellationToken ct)
    {
        if (!http.Request.HasFormContentType)
            return Results.BadRequest(new { message = "请使用 multipart/form-data 上传 .ypk 或 .zip 文件。" });

        IFormCollection form = await http.Request.ReadFormAsync(ct).ConfigureAwait(false);
        if (form.Files.Count == 0)
            return Results.BadRequest(new { message = "未检测到上传文件。" });

        IFormFile file = form.Files[0];
        string ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".ypk" or ".zip"))
            return Results.BadRequest(new { message = "仅支持解析 .ypk 或 .zip 扩展包。" });

        await using Stream uploadStream = file.OpenReadStream();
        (string previewToken, DiyYpkInspectionReport report) = _diyManager.SavePreviewYpk(uploadStream, file.FileName);

        return Results.Ok(new
        {
            previewToken,
            suggestedPackageId = DiyPackageManager.SanitizePackageIdFromFileName(file.FileName),
            suggestedName = Path.GetFileNameWithoutExtension(file.FileName),
            report
        });
    }

    [HttpGet("/api/v1/diy/ypk-preview/{token}/entry")]
    [AdminAuth(RbacPermissions.DiyView, RbacPermissions.DiyUpload, RbacPermissions.DiyEdit)]
    private IResult GetAdminYpkPreviewEntry(string token, string? path, int? cardId)
    {
        if (!_diyManager.TryGetPreviewYpkPath(token, out string ypkPath, out _))
            return Results.NotFound(new { message = "预览会话已过期，请重新选择 .ypk 文件。" });

        using FileStream fs = new(ypkPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var entry = DiyPackageManager.TryReadYpkArchiveEntry(fs, path, cardId);
        if (entry is null)
            return Results.NotFound(new { message = "未找到对应包内文件。" });

        if (!entry.Value.IsText)
            return Results.Bytes(entry.Value.Bytes, entry.Value.ContentType);

        return Results.Ok(new
        {
            path = entry.Value.ResolvedPath,
            content = entry.Value.TextContent
        });
    }

    [HttpGet("/api/v1/diy/packages/{id}/inspect")]
    [AdminAuth(RbacPermissions.DiyView)]
    private IResult InspectAdminInstalledPackage(string id)
    {
        if (!DiyPackageManager.IsValidPackageId(id))
            return Results.BadRequest(new { message = "非法包 ID。" });

        try
        {
            return Results.Ok(new { packageId = id, report = _diyManager.InspectInstalledPackage(id) });
        }
        catch (DirectoryNotFoundException ex)
        {
            return Results.NotFound(new { message = ex.Message });
        }
    }

    [HttpGet("/api/v1/diy/packages/{id}/file")]
    [AdminAuth(RbacPermissions.DiyView)]
    private IResult GetAdminInstalledPackageFile(string id, string? path)
    {
        if (!DiyPackageManager.IsValidPackageId(id) || string.IsNullOrWhiteSpace(path))
            return Results.BadRequest(new { message = "参数无效。" });

        try
        {
            var file = _diyManager.TryReadInstalledPackageFile(id, path);
            if (file is null)
                return Results.NotFound(new { message = "未找到对应文件或该二进制类型不支持直接文本预览。" });

            return file.Value.IsText
                ? Results.Ok(new { path = file.Value.ResolvedPath, content = file.Value.TextContent })
                : Results.Bytes(file.Value.Bytes, file.Value.ContentType);
        }
        catch (InvalidOperationException)
        {
            return Results.BadRequest(new { message = "非法文件路径。" });
        }
    }

    [HttpPost("/api/v1/diy/packages/import-ypk")]
    [AdminAuth(RbacPermissions.DiyEdit, RbacPermissions.DiyUpload)]
    private async Task<IResult> ImportDiyPackageYpkAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        CancellationToken ct)
    {
        string? requestedId = null;
        string? requestedName = null;
        string? previewToken = null;
        bool merge = true;
        bool autoReload = true;
        string sourceFileName = "package.ypk";
        Stream? directUploadStream = null;

        if (http.Request.HasFormContentType)
        {
            IFormCollection form = await http.Request.ReadFormAsync(ct).ConfigureAwait(false);
            requestedId = form["packageId"].ToString().Trim();
            requestedName = form["name"].ToString().Trim();
            previewToken = form["previewToken"].ToString().Trim();
            merge = !string.Equals(form["merge"].ToString(), "false", StringComparison.OrdinalIgnoreCase);
            autoReload = !string.Equals(form["autoReload"].ToString(), "false", StringComparison.OrdinalIgnoreCase);

            if (form.Files.Count > 0)
            {
                IFormFile file = form.Files[0];
                string ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (ext is not (".ypk" or ".zip"))
                    return Results.BadRequest(new { message = "仅支持导入 .ypk 或标准 .zip 扩展包。" });
                if (file.Length > 256L * 1024 * 1024)
                    return Results.BadRequest(new { message = "单个 .ypk 文件不能超过 256MB。" });

                sourceFileName = file.FileName;
                directUploadStream = file.OpenReadStream();
            }
        }
        else
        {
            ConfirmYpkPreviewImportDto? dto = await http.Request.ReadFromJsonAsync<ConfirmYpkPreviewImportDto>(cancellationToken: ct).ConfigureAwait(false);
            if (dto is not null)
            {
                previewToken = dto.PreviewToken?.Trim();
                requestedId = dto.PackageId?.Trim();
                requestedName = dto.Name?.Trim();
                merge = dto.Merge ?? true;
                autoReload = dto.AutoReload ?? true;
            }
        }

        try
        {
            Stream? ypkStreamToImport = directUploadStream;
            FileStream? previewFs = null;

            if (ypkStreamToImport is null)
            {
                if (!_diyManager.TryGetPreviewYpkPath(previewToken, out string cachedPath, out string origName))
                    return Results.BadRequest(new { message = "未检测到上传文件或预览会话已过期，请重新选择 .ypk 文件。" });

                sourceFileName = origName;
                previewFs = new FileStream(cachedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                ypkStreamToImport = previewFs;
            }

            try
            {
                string pkgId = !string.IsNullOrWhiteSpace(requestedId)
                    ? requestedId
                    : DiyPackageManager.SanitizePackageIdFromFileName(sourceFileName);

                if (!DiyPackageManager.IsValidPackageId(pkgId))
                    return Results.BadRequest(new { message = "包 ID 仅允许字母、数字、点、下划线和连字符（1~64 位）。" });

                string defaultName = Path.GetFileNameWithoutExtension(sourceFileName).Trim();
                string? displayName = !string.IsNullOrWhiteSpace(requestedName)
                    ? requestedName
                    : !string.IsNullOrWhiteSpace(defaultName)
                        ? defaultName
                        : null;

                DiyYpkImportResult importRes = _diyManager.ImportPackageFromYpk(
                    pkgId,
                    ypkStreamToImport,
                    displayName: displayName,
                    author: principal.Username,
                    mergeIntoExisting: merge);

                if (!importRes.IsSuccess)
                {
                    await _store.AppendAuditAsync("panel_user", principal.UserId.ToString(), principal.Username, "diy.import_ypk", RbacPermissions.DiyUpload, pkgId, http.GetRemoteIpNormalized(), "rejected", string.Join("; ", importRes.Errors), ct).ConfigureAwait(false);
                    return Results.BadRequest(new
                    {
                        success = false,
                        message = "导入 .ypk 安全预检未通过：" + string.Join("; ", importRes.Errors),
                        errors = importRes.Errors
                    });
                }

                ServerReloadResult? reload = null;
                if (autoReload && principal.HasPermission(RbacPermissions.DiyReload))
                {
                    reload = _serverHost.Server.ReloadAssets();
                    await _store.Cache.FlushNamespaceAsync("cards", ct).ConfigureAwait(false);
                    _eventHub.BroadcastEvent("diy.reloaded", reload, RbacPermissions.DashboardView);
                }

                DiyPackageValidationReport report = _diyManager.ValidatePackage(pkgId, _serverHost.Server.CardRepository);
                DiyPackageManifest? manifest = _diyManager.ListPackages()
                    .FirstOrDefault(p => string.Equals(p.Id, pkgId, StringComparison.OrdinalIgnoreCase));

                await _store.AppendAuditAsync(
                    "panel_user",
                    principal.UserId.ToString(),
                    principal.Username,
                    "diy.import_ypk",
                    RbacPermissions.DiyUpload,
                    pkgId,
                    http.GetRemoteIpNormalized(),
                    "ok",
                    JsonSerializer.Serialize(new { importRes.TotalCards, importRes.ImportedScripts, importRes.ImportedImages, importRes.ImportedDecks }),
                    ct).ConfigureAwait(false);

                return Results.Ok(new
                {
                    success = true,
                    packageId = pkgId,
                    manifest,
                    importResult = importRes,
                    validation = report,
                    reload
                });
            }
            finally
            {
                previewFs?.Dispose();
            }
        }
        finally
        {
            if (directUploadStream is not null)
                await directUploadStream.DisposeAsync().ConfigureAwait(false);
        }
    }

    [HttpGet("/api/v1/diy/export-all.ypk")]
    [AdminAuth(RbacPermissions.DiyView)]
    private async Task<IResult> ExportAllDiyPackagesYpkAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        CancellationToken ct)
    {
        try
        {
            byte[] ypkBytes = _diyManager.ExportAllEnabledPackagesAsYpk();
            await _store.AppendAuditAsync("panel_user", principal.UserId.ToString(), principal.Username, "diy.export_all_ypk", RbacPermissions.DiyView, "all-enabled", http.GetRemoteIpNormalized(), "ok", null, ct).ConfigureAwait(false);
            return Results.File(ypkBytes, "application/zip", "ygoprosharp-all-diy.ypk");
        }
        catch (InvalidOperationException ex)
        {
            return Results.NotFound(new { message = ex.Message });
        }
    }

    [HttpGet("/api/v1/diy/packages/{id}/export.ypk")]
    [AdminAuth(RbacPermissions.DiyView)]
    private async Task<IResult> ExportDiyPackageYpkAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        string id,
        CancellationToken ct)
    {
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
    }

    [HttpPost("/api/v1/diy/sandbox-test")]
    [AdminAuth(RbacPermissions.DiySandboxTest)]
    private async Task<IResult> RunDiySandboxTestAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        SandboxTestCardRequestDto body,
        CancellationToken ct)
    {
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
    }

    [HttpGet("/api/v1/diy/proposals")]
    [AdminAuth(RbacPermissions.DiyView)]
    private async Task<IResult> ListDiyProposalsAsync(string? status, string? packageId, int? limit, CancellationToken ct)
    {
        IReadOnlyList<DiyProposalEntity> proposals = await _store.ListDiyProposalsAsync(status, packageId, limit ?? 80, ct).ConfigureAwait(false);
        return Results.Ok(new { proposals });
    }

    [HttpGet("/api/v1/diy/proposals/{proposalId:guid}/inspect-ypk")]
    [AdminAuth(RbacPermissions.DiyView)]
    private async Task<IResult> InspectProposalStagedYpkAsync(Guid proposalId, CancellationToken ct)
    {
        IReadOnlyList<DiyProposalEntity> list = await _store.ListDiyProposalsAsync(null, null, 200, ct).ConfigureAwait(false);
        DiyProposalEntity? found = list.FirstOrDefault(p => p.Id == proposalId);
        if (found is null)
            return Results.NotFound(new { message = "未找到该提案。" });

        DiyProposalPayloadDto? payload = JsonSerializer.Deserialize<DiyProposalPayloadDto>(
            found.DiffJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (payload is null || string.IsNullOrWhiteSpace(payload.StagedYpkFileName) ||
            !_diyManager.TryGetStagedProposalYpkPath(payload.StagedYpkFileName, out string stagedPath))
        {
            return Results.NotFound(new { message = "该提案未关联暂存 .ypk 整包文件。" });
        }

        using FileStream fs = new(stagedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Results.Ok(new
        {
            proposalId,
            packageId = found.PackageId,
            report = _diyManager.InspectYpkArchive(fs, $"{found.PackageId}.ypk")
        });
    }

    [HttpGet("/api/v1/diy/proposals/{proposalId:guid}/ypk-entry")]
    [AdminAuth(RbacPermissions.DiyView)]
    private async Task<IResult> GetProposalStagedYpkEntryAsync(Guid proposalId, string? path, int? cardId, CancellationToken ct)
    {
        IReadOnlyList<DiyProposalEntity> list = await _store.ListDiyProposalsAsync(null, null, 200, ct).ConfigureAwait(false);
        DiyProposalEntity? found = list.FirstOrDefault(p => p.Id == proposalId);
        if (found is null)
            return Results.NotFound(new { message = "未找到该提案。" });

        DiyProposalPayloadDto? payload = JsonSerializer.Deserialize<DiyProposalPayloadDto>(
            found.DiffJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (payload is null || string.IsNullOrWhiteSpace(payload.StagedYpkFileName) ||
            !_diyManager.TryGetStagedProposalYpkPath(payload.StagedYpkFileName, out string stagedPath))
        {
            return Results.NotFound(new { message = "未找到暂存 .ypk 文件。" });
        }

        using FileStream fs = new(stagedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var entry = DiyPackageManager.TryReadYpkArchiveEntry(fs, path, cardId);
        if (entry is null)
            return Results.NotFound(new { message = "未找到对应包内文件。" });

        return entry.Value.IsText
            ? Results.Ok(new { path = entry.Value.ResolvedPath, content = entry.Value.TextContent })
            : Results.Bytes(entry.Value.Bytes, entry.Value.ContentType);
    }

    [HttpPost("/api/v1/diy/proposals")]
    [AdminAuth(RbacPermissions.DiyEdit, RbacPermissions.DiyUpload)]
    private async Task<IResult> SubmitDiyProposalAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        SubmitDiyProposalRequestDto body,
        CancellationToken ct)
    {
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
    }

    [HttpPost("/api/v1/diy/proposals/{proposalId:guid}/review")]
    [AdminAuth(RbacPermissions.DiyReviewProposal)]
    private async Task<IResult> ReviewDiyProposalAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        Guid proposalId,
        ReviewDiyProposalRequestDto body,
        CancellationToken ct)
    {
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

        if (payload.Images is not null)
        {
            foreach (ProposedCardImageDto img in payload.Images)
            {
                if (img.CardId <= 0 || string.IsNullOrWhiteSpace(img.Base64Data))
                {
                    errors.Add("提案中的卡图必须包含正整数卡密与有效图像数据。");
                    continue;
                }

                try
                {
                    byte[] raw = Convert.FromBase64String(img.Base64Data);
                    if (raw.Length < 8 || raw.Length > 8 * 1024 * 1024)
                    {
                        errors.Add($"卡图 #{img.CardId} 体积超出限制（最大 8MB）。");
                        continue;
                    }
                    bool isPng = raw[0] == 0x89 && raw[1] == 0x50 && raw[2] == 0x4E && raw[3] == 0x47;
                    bool isJpg = raw[0] == 0xFF && raw[1] == 0xD8 && raw[2] == 0xFF;
                    if (!isPng && !isJpg)
                        errors.Add($"卡图 #{img.CardId} 仅支持标准 PNG 或 JPEG 格式。");
                }
                catch (FormatException)
                {
                    errors.Add($"卡图 #{img.CardId} Base64 数据损坏。");
                }
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

            if (!string.IsNullOrWhiteSpace(payload.StagedYpkFileName))
            {
                if (!_diyManager.TryGetStagedProposalYpkPath(payload.StagedYpkFileName, out string stagedPath))
                    return (false, $"未找到待审暂存的扩展包文件 '{payload.StagedYpkFileName}'。");

                using FileStream ypkFs = new(stagedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                DiyYpkImportResult imp = _diyManager.ImportPackageFromYpk(
                    proposal.PackageId,
                    ypkFs,
                    author: payload.AuthorName,
                    mergeIntoExisting: true);
                return imp.IsSuccess
                    ? (true, null)
                    : (false, string.Join("; ", imp.Errors));
            }

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

            if (payload.Images is not null)
            {
                foreach (ProposedCardImageDto img in payload.Images)
                {
                    byte[] raw = Convert.FromBase64String(img.Base64Data);
                    _diyManager.SaveCardImage(proposal.PackageId, img.CardId, raw);
                }
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
