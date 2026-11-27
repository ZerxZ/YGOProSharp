using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using YGOProSharp.Abstractions.Ocg.Enums;
using YGOProSharp.Core.Cards;

namespace YGOProSharp.Core;

/// <summary>
/// 受管 DIY 包清单元数据（存储在包目录旁路 <c>manifest.json</c> 中，不污染标准 <c>.cdb</c>）。
/// </summary>
public sealed record DiyPackageManifest
{
    public required string Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Version { get; init; } = "1.0.0";
    public string Author { get; init; } = string.Empty;
    public bool Enabled { get; init; } = true;
    public int Priority { get; init; } = 100;
    public DateTimeOffset UpdatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Lua 脚本静态安全预检结果。
/// </summary>
public sealed record LuaScriptSecurityAudit(
    bool IsSafe,
    IReadOnlyList<string> Violations);

/// <summary>
/// DIY 包综合校验与多库卡密冲突报告。
/// </summary>
public sealed record DiyPackageValidationReport(
    string PackageId,
    bool IsValid,
    int CardCount,
    int CustomCardCount,
    IReadOnlyList<int> OverriddenCardIds,
    IReadOnlyList<int> MissingScriptCardIds,
    IReadOnlyList<string> Errors);

public sealed record DiyCardSummary(
    DiyCardEntry Card,
    bool HasScript,
    bool HasImage);

public sealed record DiyCardDetail(
    string PackageId,
    DiyCardEntry Card,
    string? ScriptContent,
    bool HasImage,
    string? ImageFileName);

public sealed record StringsConfEntry(
    string Kind,
    string CodeHex,
    string Name);

public sealed record StringsConfSummary(
    string RawContent,
    IReadOnlyList<StringsConfEntry> Setnames,
    IReadOnlyList<StringsConfEntry> Counters);

public sealed record DiyCardConflictItem(
    int CardId,
    string CardName,
    IReadOnlyList<string> DefinedInPackages,
    bool OverridesBaseCatalog);

public sealed record DiyGlobalConflictReport(
    IReadOnlyList<DiyCardConflictItem> Conflicts,
    IReadOnlyDictionary<string, IReadOnlyList<int>> MissingScriptsByPackage,
    IReadOnlyDictionary<string, IReadOnlyList<int>> MissingImagesByPackage);

/// <summary>
/// 从 <c>.ypk</c> / <c>.zip</c> 扩展包原子解包导入的汇总结果。
/// </summary>
public sealed record DiyYpkImportResult(
    string PackageId,
    bool IsSuccess,
    int ImportedCdbFiles,
    int TotalCards,
    int ImportedScripts,
    int ImportedImages,
    int ImportedDecks,
    int ImportedConfFiles,
    IReadOnlyList<string> Errors);

public sealed record DiyArchiveFileEntry(
    string Path,
    string Name,
    string Folder,
    string Category,
    long CompressedBytes,
    long UncompressedBytes);

public sealed record DiyDeckBrief(
    string Name,
    string Path,
    int MainCount,
    int ExtraCount,
    int SideCount);

public sealed record DiyCategoryStat(
    string Category,
    int FileCount,
    long TotalBytes);

/// <summary>
/// <c>.ypk</c> 压缩包或已安装受管 DIY 包的完整内部文件清单与可视化结构报告。
/// </summary>
public sealed record DiyYpkInspectionReport(
    string ArchiveName,
    bool IsSafe,
    long CompressedBytes,
    long UncompressedBytes,
    int TotalFiles,
    int CardCount,
    int ScriptCount,
    int ImageCount,
    int DeckCount,
    int ConfCount,
    IReadOnlyList<DiyCategoryStat> Categories,
    IReadOnlyList<DiyArchiveFileEntry> Files,
    IReadOnlyList<DiyCardSummary> Cards,
    IReadOnlyList<DiyDeckBrief> Decks,
    StringsConfSummary StringsConf,
    IReadOnlyList<string> Errors);

/// <summary>
/// 管理 <c>&lt;diyRoot&gt;/packages/&lt;package-id&gt;</c> 的目录规范、<c>Working Copy -&gt; 预检 -&gt; 原子替换</c>、
/// Lua 静态安全审计、多包冲突分析与 <c>.trash</c> 回收站恢复。
/// </summary>
public sealed partial class DiyPackageManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cdb", ".lua", ".ypk", ".zip", ".conf", ".ydk",
        ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif",
        ".json", ".txt", ".md", ".ini", ".toml", ".yml", ".yaml", ".csv",
        ".ogg", ".mp3", ".wav", ".otf", ".ttf", ""
    };

    private static readonly HashSet<string> ForbiddenExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".so", ".dylib", ".bat", ".cmd", ".ps1", ".vbs", ".wsf",
        ".sh", ".bash", ".msi", ".scr", ".com", ".jar", ".py", ".php", ".rb", ".pl"
    };

    private readonly string _diyRootPath;
    private readonly ICardDatabaseManager _databaseManager;

    public DiyPackageManager(string diyRootPath, ICardDatabaseManager databaseManager)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diyRootPath);
        ArgumentNullException.ThrowIfNull(databaseManager);

        _diyRootPath = Path.GetFullPath(diyRootPath);
        _databaseManager = databaseManager;
    }

    public string DiyRootPath => _diyRootPath;

    public string PackagesDirectory => Path.Combine(_diyRootPath, "packages");

    public string TrashDirectory => Path.Combine(_diyRootPath, ".trash");

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafePackageIdRegex();

    [GeneratedRegex(@"(?<![A-Za-z0-9_])(os|io|package|debug|luanet)\s*\.|(?<![A-Za-z0-9_])(dofile|loadfile|require|import)\s*\(", RegexOptions.CultureInvariant)]
    private static partial Regex DangerousLuaCallRegex();

    public static bool IsValidPackageId(string? packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            return false;

        if (packageId.Contains("..", StringComparison.Ordinal))
            return false;

        return SafePackageIdRegex().IsMatch(packageId);
    }

    /// <summary>
    /// 从上传的 <c>.ypk</c> / <c>.zip</c> 文件名推导符合安全规范的受管包 ID（支持中文/特殊字符文件名自动回退稳定短哈希）。
    /// </summary>
    public static string SanitizePackageIdFromFileName(string? fileName)
    {
        string rawName = Path.GetFileNameWithoutExtension(fileName ?? string.Empty).Trim();
        if (IsValidPackageId(rawName))
            return rawName;

        StringBuilder sb = new();
        bool prevDash = false;
        foreach (char ch in rawName)
        {
            if (char.IsAsciiLetterOrDigit(ch) || ch is '.' or '_' or '-')
            {
                if (sb.Length == 0 && !char.IsAsciiLetterOrDigit(ch))
                    continue;
                sb.Append(char.ToLowerInvariant(ch));
                prevDash = false;
            }
            else if (sb.Length > 0 && !prevDash)
            {
                sb.Append('-');
                prevDash = true;
            }
        }

        string candidate = sb.ToString().Trim('-', '.', '_');
        if (candidate.Length > 48)
            candidate = candidate[..48].TrimEnd('-', '.', '_');

        if (IsValidPackageId(candidate))
            return candidate;

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(string.IsNullOrEmpty(rawName) ? Guid.NewGuid().ToString("N") : rawName));
        return "diy-pack-" + Convert.ToHexString(hash[..4]).ToLowerInvariant();
    }

    /// <summary>
    /// 对 Lua 脚本源码执行编码兼容读取（支持 UTF-8 / UTF-8 BOM 及历史扩展包注释回退）与危险系统库/反射调用静态拦截。
    /// </summary>
    public static LuaScriptSecurityAudit AuditLuaScript(ReadOnlySpan<byte> utf8Bytes)
    {
        if (utf8Bytes.Length >= 3 && utf8Bytes[0] == 0xEF && utf8Bytes[1] == 0xBB && utf8Bytes[2] == 0xBF)
            utf8Bytes = utf8Bytes[3..];

        string source;
        try
        {
            UTF8Encoding strictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            source = strictUtf8.GetString(utf8Bytes);
        }
        catch (DecoderFallbackException)
        {
            // 部分老旧社区 .ypk 的 Lua 中文注释使用 ANSI/GBK 编码，回退 Latin1 仍可精确审计全部 ASCII 危险 API 调用
            source = Encoding.Latin1.GetString(utf8Bytes);
        }

        return AuditLuaScript(source);
    }

    public static LuaScriptSecurityAudit AuditLuaScript(string luaSource)
    {
        ArgumentNullException.ThrowIfNull(luaSource);

        // 先剔除多行注释 --[[ ... ]] 防止注释说明文字误触关键字拦截
        string cleanedSource = Regex.Replace(luaSource, @"--\[(=*)\[[\s\S]*?\]\1\]", string.Empty, RegexOptions.CultureInvariant);

        List<string> violations = new();
        string[] lines = cleanedSource.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].Trim();
            if (trimmed.StartsWith("--", StringComparison.Ordinal))
                continue;

            int inlineComment = trimmed.IndexOf("--", StringComparison.Ordinal);
            string codePart = inlineComment >= 0 ? trimmed[..inlineComment] : trimmed;
            foreach (Match match in DangerousLuaCallRegex().Matches(codePart))
            {
                violations.Add($"Line {i + 1}: forbidden Lua token '{match.Value.Trim()}'.");
            }
        }

        return new LuaScriptSecurityAudit(violations.Count == 0, violations);
    }

    /// <summary>
    /// 校验 <c>.ypk</c> / <c>.zip</c> 压缩包内所有条目路径与扩展名，防御路径穿越（ZIP Slip）与危险可执行后缀。
    /// </summary>
    public static IReadOnlyList<string> AuditArchiveEntries(Stream archiveStream, long maxUncompressedBytes = 512L * 1024 * 1024)
    {
        ArgumentNullException.ThrowIfNull(archiveStream);

        List<string> errors = new();
        long totalBytes = 0;
        using ZipArchive archive = new(archiveStream, ZipArchiveMode.Read, leaveOpen: true);

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string entryPath = entry.FullName.Replace('\\', '/');
            if (entryPath.StartsWith('/') ||
                entryPath.Contains(':', StringComparison.Ordinal) ||
                entryPath.Split('/').Any(static seg => seg == ".."))
            {
                errors.Add($"Unsafe archive entry path '{entry.FullName}'.");
                continue;
            }

            if (string.IsNullOrEmpty(entry.Name))
                continue;

            string ext = Path.GetExtension(entry.Name);
            if (ForbiddenExecutableExtensions.Contains(ext) || !AllowedExtensions.Contains(ext))
                errors.Add($"Disallowed file extension '{ext}' in archive entry '{entry.FullName}'.");

            totalBytes += Math.Max(0, entry.Length);
            if (totalBytes > maxUncompressedBytes)
            {
                errors.Add($"Archive uncompressed size exceeds limit ({maxUncompressedBytes / (1024 * 1024)} MB).");
                break;
            }

            if (ext.Equals(".lua", StringComparison.OrdinalIgnoreCase))
            {
                using Stream s = entry.Open();
                using MemoryStream ms = new();
                s.CopyTo(ms);
                LuaScriptSecurityAudit luaAudit = AuditLuaScript(ms.GetBuffer().AsSpan(0, (int)ms.Length));
                if (!luaAudit.IsSafe)
                {
                    foreach (string v in luaAudit.Violations)
                        errors.Add($"{entry.FullName}: {v}");
                }
            }
        }

        return errors;
    }

    public DiyPackageManifest UpsertManifest(DiyPackageManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        EnsureValidPackageId(manifest.Id);

        string pkgDir = GetPackageDirectory(manifest.Id);
        Directory.CreateDirectory(pkgDir);

        DiyPackageManifest updated = manifest with
        {
            Name = string.IsNullOrWhiteSpace(manifest.Name) ? manifest.Id : manifest.Name.Trim(),
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        byte[] jsonBytes = JsonSerializer.SerializeToUtf8Bytes(updated, JsonOptions);
        WriteFileAtomically(Path.Combine(pkgDir, "manifest.json"), jsonBytes);
        return updated;
    }

    public IReadOnlyList<DiyPackageManifest> ListPackages(bool enabledOnly = false)
    {
        if (!Directory.Exists(PackagesDirectory))
            return [];

        List<DiyPackageManifest> list = new();
        foreach (string dir in Directory.EnumerateDirectories(PackagesDirectory))
        {
            string id = Path.GetFileName(dir);
            if (!IsValidPackageId(id))
                continue;

            DiyPackageManifest manifest = ReadOrCreateDefaultManifest(id, dir);
            if (!enabledOnly || manifest.Enabled)
                list.Add(manifest);
        }

        list.Sort(static (a, b) =>
        {
            int cmp = a.Priority.CompareTo(b.Priority);
            return cmp != 0 ? cmp : string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
        });
        return list;
    }

    /// <summary>
    /// 通过临时工作副本校验 <c>.cdb</c> 后原子替换落盘。
    /// </summary>
    public CardDatabaseValidationResult SaveCardDatabase(string packageId, ReadOnlySpan<byte> cdbBytes, string fileName = "cards.cdb")
    {
        EnsureValidPackageId(packageId);
        string safeFile = EnsureSafeChildPath(GetPackageDirectory(packageId), fileName, ".cdb");
        Directory.CreateDirectory(Path.GetDirectoryName(safeFile)!);

        string workingCopy = safeFile + "." + Guid.NewGuid().ToString("N") + ".working.cdb";
        try
        {
            File.WriteAllBytes(workingCopy, cdbBytes.ToArray());
            CardDatabaseValidationResult validation = _databaseManager.ValidateDatabase(workingCopy);
            if (!validation.IsValid)
                return validation;

            File.Move(workingCopy, safeFile, overwrite: true);
            TouchManifestIfExists(packageId);
            return validation;
        }
        finally
        {
            if (File.Exists(workingCopy))
            {
                try { File.Delete(workingCopy); } catch { }
            }
        }
    }

    /// <summary>
    /// 先执行静态安全审计，通过后原子写入 <c>script/*.lua</c>。
    /// </summary>
    public LuaScriptSecurityAudit SaveScript(string packageId, string scriptFileName, string luaSource)
    {
        EnsureValidPackageId(packageId);
        ArgumentNullException.ThrowIfNull(luaSource);

        LuaScriptSecurityAudit audit = AuditLuaScript(luaSource);
        if (!audit.IsSafe)
            return audit;

        string scriptDir = Path.Combine(GetPackageDirectory(packageId), "script");
        string safeFile = EnsureSafeChildPath(scriptDir, scriptFileName, ".lua");
        Directory.CreateDirectory(Path.GetDirectoryName(safeFile)!);

        WriteFileAtomically(safeFile, Encoding.UTF8.GetBytes(luaSource));
        TouchManifestIfExists(packageId);
        return audit;
    }

    public void SaveDeck(string packageId, string deckName, string ydkContent)
    {
        EnsureValidPackageId(packageId);
        string normalizedFile = deckName.EndsWith(".ydk", StringComparison.OrdinalIgnoreCase) ? deckName : deckName + ".ydk";
        string decksDir = Path.Combine(GetPackageDirectory(packageId), "decks");
        string safeFile = EnsureSafeChildPath(decksDir, normalizedFile, ".ydk");

        YdkDeckData parsed = YdkSerializer.Parse(ydkContent, Path.GetFileNameWithoutExtension(safeFile));
        if (parsed.Main.Count == 0 && parsed.Extra.Count == 0)
            throw new InvalidOperationException("Deck must contain at least one Main or Extra card.");

        Directory.CreateDirectory(Path.GetDirectoryName(safeFile)!);
        WriteFileAtomically(safeFile, Encoding.UTF8.GetBytes(ydkContent));
        TouchManifestIfExists(packageId);
    }

    /// <summary>
    /// 列出指定 DIY 包内的全部可编辑卡片及脚本/卡图齐备状态。
    /// </summary>
    public IReadOnlyList<DiyCardSummary> ListPackageCards(string packageId)
    {
        EnsureValidPackageId(packageId);
        string pkgDir = GetPackageDirectory(packageId);
        if (!Directory.Exists(pkgDir))
            return [];

        Dictionary<int, DiyCardEntry> byId = new();
        foreach (string cdbPath in Directory.EnumerateFiles(pkgDir, "*.cdb", SearchOption.TopDirectoryOnly).OrderBy(static p => p, StringComparer.OrdinalIgnoreCase))
        {
            foreach (DiyCardEntry c in _databaseManager.LoadEditableCards(cdbPath))
                byId[c.Id] = c;
        }

        string scriptDir = Path.Combine(pkgDir, "script");
        string picsDir = Path.Combine(pkgDir, "pics");

        return byId.Values
            .OrderBy(static c => c.Id)
            .Select(c => new DiyCardSummary(
                c,
                HasScript: File.Exists(Path.Combine(scriptDir, $"c{c.Id}.lua")),
                HasImage: FindCardImagePath(picsDir, c.Id) is not null))
            .ToArray();
    }

    public DiyCardDetail? GetCardDetail(string packageId, int cardId)
    {
        EnsureValidPackageId(packageId);
        string pkgDir = GetPackageDirectory(packageId);
        if (!Directory.Exists(pkgDir))
            return null;

        DiyCardEntry? found = null;
        foreach (string cdbPath in Directory.EnumerateFiles(pkgDir, "*.cdb", SearchOption.TopDirectoryOnly).OrderBy(static p => p, StringComparer.OrdinalIgnoreCase))
        {
            foreach (DiyCardEntry c in _databaseManager.LoadEditableCards(cdbPath))
            {
                if (c.Id == cardId)
                    found = c;
            }
        }

        if (found is null)
            return null;

        string scriptPath = Path.Combine(pkgDir, "script", $"c{cardId}.lua");
        string? scriptContent = File.Exists(scriptPath) ? File.ReadAllText(scriptPath, Encoding.UTF8) : null;
        string? imgPath = FindCardImagePath(Path.Combine(pkgDir, "pics"), cardId);

        return new DiyCardDetail(
            packageId,
            found,
            scriptContent,
            imgPath is not null,
            imgPath is not null ? Path.GetFileName(imgPath) : null);
    }

    /// <summary>
    /// 通过 Working Copy 原子保存单张 DIY 卡属性与可选配套 <c>c&lt;id&gt;.lua</c> 脚本。
    /// </summary>
    public CardDatabaseValidationResult UpsertCard(
        string packageId,
        DiyCardEntry card,
        string? luaScript = null,
        string cdbFileName = "cards.cdb")
    {
        EnsureValidPackageId(packageId);
        ArgumentNullException.ThrowIfNull(card);
        if (card.Id <= 0)
            throw new ArgumentOutOfRangeException(nameof(card), "Card ID must be positive.");

        if (luaScript is not null)
        {
            LuaScriptSecurityAudit audit = AuditLuaScript(luaScript);
            if (!audit.IsSafe)
                throw new InvalidOperationException("Lua script failed security audit: " + string.Join(" ", audit.Violations));
        }

        string pkgDir = GetPackageDirectory(packageId);
        Directory.CreateDirectory(pkgDir);
        string safeCdb = EnsureSafeChildPath(pkgDir, cdbFileName, ".cdb");

        CardDatabaseValidationResult dbResult = _databaseManager.UpsertCard(safeCdb, card);
        if (!dbResult.IsValid)
            return dbResult;

        if (luaScript is not null)
        {
            string scriptDir = Path.Combine(pkgDir, "script");
            Directory.CreateDirectory(scriptDir);
            string scriptPath = EnsureSafeChildPath(scriptDir, $"c{card.Id}.lua", ".lua");
            if (string.IsNullOrWhiteSpace(luaScript))
            {
                if (File.Exists(scriptPath))
                    File.Delete(scriptPath);
            }
            else
            {
                WriteFileAtomically(scriptPath, Encoding.UTF8.GetBytes(luaScript));
            }
        }

        TouchManifestIfExists(packageId);
        return dbResult;
    }

    public bool DeleteCard(string packageId, int cardId, bool deleteAssets = true)
    {
        EnsureValidPackageId(packageId);
        string pkgDir = GetPackageDirectory(packageId);
        if (!Directory.Exists(pkgDir))
            return false;

        bool removedAny = false;
        foreach (string cdbPath in Directory.EnumerateFiles(pkgDir, "*.cdb", SearchOption.TopDirectoryOnly))
        {
            IReadOnlyList<DiyCardEntry> before = _databaseManager.LoadEditableCards(cdbPath);
            if (before.All(c => c.Id != cardId))
                continue;

            CardDatabaseValidationResult res = _databaseManager.DeleteCard(cdbPath, cardId);
            if (res.IsValid)
                removedAny = true;
        }

        if (deleteAssets)
        {
            string scriptPath = Path.Combine(pkgDir, "script", $"c{cardId}.lua");
            if (File.Exists(scriptPath))
            {
                File.Delete(scriptPath);
                removedAny = true;
            }

            string? imgPath = FindCardImagePath(Path.Combine(pkgDir, "pics"), cardId);
            if (imgPath is not null && File.Exists(imgPath))
            {
                File.Delete(imgPath);
                removedAny = true;
            }
        }

        if (removedAny)
            TouchManifestIfExists(packageId);

        return removedAny;
    }

    public string? ReadScript(string packageId, string scriptFileName)
    {
        EnsureValidPackageId(packageId);
        string normalized = scriptFileName.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) ? scriptFileName : scriptFileName + ".lua";
        string safeFile = EnsureSafeChildPath(Path.Combine(GetPackageDirectory(packageId), "script"), normalized, ".lua");
        return File.Exists(safeFile) ? File.ReadAllText(safeFile, Encoding.UTF8) : null;
    }

    /// <summary>
    /// 校验 PNG / JPEG 魔数头与体积上限（8MB）后，原子保存卡图至 <c>pics/&lt;cardId&gt;.&lt;ext&gt;</c>。
    /// </summary>
    public string SaveCardImage(string packageId, int cardId, ReadOnlySpan<byte> imageBytes)
    {
        EnsureValidPackageId(packageId);
        if (cardId <= 0)
            throw new ArgumentOutOfRangeException(nameof(cardId), "Card ID must be positive.");
        if (imageBytes.Length < 8)
            throw new InvalidOperationException("Image file is empty or truncated.");
        if (imageBytes.Length > 8 * 1024 * 1024)
            throw new InvalidOperationException("Card image exceeds 8MB limit.");

        string ext;
        if (imageBytes[0] == 0x89 && imageBytes[1] == 0x50 && imageBytes[2] == 0x4E && imageBytes[3] == 0x47)
            ext = ".png";
        else if (imageBytes[0] == 0xFF && imageBytes[1] == 0xD8 && imageBytes[2] == 0xFF)
            ext = ".jpg";
        else
            throw new InvalidOperationException("Only valid PNG or JPEG card images are allowed.");

        string picsDir = Path.Combine(GetPackageDirectory(packageId), "pics");
        Directory.CreateDirectory(picsDir);

        foreach (string oldExt in new[] { ".png", ".jpg", ".jpeg" })
        {
            string existing = Path.Combine(picsDir, $"{cardId}{oldExt}");
            if (!oldExt.Equals(ext, StringComparison.OrdinalIgnoreCase) && File.Exists(existing))
                File.Delete(existing);
        }

        string fileName = $"{cardId}{ext}";
        string targetPath = EnsureSafeChildPath(picsDir, fileName, ext);
        WriteFileAtomically(targetPath, imageBytes.ToArray());
        TouchManifestIfExists(packageId);
        return fileName;
    }

    public (byte[] Bytes, string ContentType, string FileName)? TryReadCardImage(string packageId, int cardId)
    {
        EnsureValidPackageId(packageId);
        if (cardId <= 0)
            return null;

        string? imgPath = FindCardImagePath(Path.Combine(GetPackageDirectory(packageId), "pics"), cardId);
        if (imgPath is null || !File.Exists(imgPath))
            return null;

        string ext = Path.GetExtension(imgPath).ToLowerInvariant();
        string contentType = ext == ".png" ? "image/png" : "image/jpeg";
        return (File.ReadAllBytes(imgPath), contentType, Path.GetFileName(imgPath));
    }

    public static StringsConfSummary ParseStringsConf(string? rawContent)
    {
        string content = rawContent ?? string.Empty;
        List<StringsConfEntry> setnames = new();
        List<StringsConfEntry> counters = new();

        foreach (string rawLine in content.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            if (line.StartsWith("!setname ", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = line[9..].Trim().Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                    setnames.Add(new StringsConfEntry("setname", parts[0], parts[1].Trim()));
            }
            else if (line.StartsWith("!counter ", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = line[9..].Trim().Split([' ', '\t'], 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                    counters.Add(new StringsConfEntry("counter", parts[0], parts[1].Trim()));
            }
        }

        return new StringsConfSummary(content, setnames, counters);
    }

    public StringsConfSummary ReadStringsConf(string packageId)
    {
        EnsureValidPackageId(packageId);
        string confPath = Path.Combine(GetPackageDirectory(packageId), "strings.conf");
        string content = File.Exists(confPath) ? File.ReadAllText(confPath, Encoding.UTF8) : string.Empty;
        return ParseStringsConf(content);
    }

    public StringsConfSummary SaveStringsConf(string packageId, string content)
    {
        EnsureValidPackageId(packageId);
        ArgumentNullException.ThrowIfNull(content);
        string pkgDir = GetPackageDirectory(packageId);
        Directory.CreateDirectory(pkgDir);
        string safePath = EnsureSafeChildPath(pkgDir, "strings.conf", ".conf");
        WriteFileAtomically(safePath, Encoding.UTF8.GetBytes(content));
        TouchManifestIfExists(packageId);
        return ParseStringsConf(content);
    }

    /// <summary>
    /// 跨所有已启用 DIY 包与可选基础卡库执行多包卡密覆盖冲突、缺失脚本与缺失卡图巡检（参考 DataEditorY）。
    /// </summary>
    public DiyGlobalConflictReport AnalyzeAllPackageConflicts(ICardRepository? baselineRepository = null)
    {
        IReadOnlyList<DiyPackageManifest> enabledPackages = ListPackages(enabledOnly: true);
        Dictionary<int, (string Name, List<string> Packages)> cardMap = new();
        Dictionary<string, IReadOnlyList<int>> missingScripts = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, IReadOnlyList<int>> missingImages = new(StringComparer.OrdinalIgnoreCase);

        foreach (DiyPackageManifest pkg in enabledPackages)
        {
            IReadOnlyList<DiyCardSummary> cards = ListPackageCards(pkg.Id);
            List<int> pkgMissingScripts = new();
            List<int> pkgMissingImages = new();

            foreach (DiyCardSummary summary in cards)
            {
                DiyCardEntry c = summary.Card;
                if (!cardMap.TryGetValue(c.Id, out (string Name, List<string> Packages) entry))
                {
                    entry = (c.Name, new List<string>());
                    cardMap[c.Id] = entry;
                }
                else if (string.IsNullOrWhiteSpace(entry.Name) && !string.IsNullOrWhiteSpace(c.Name))
                {
                    entry = (c.Name, entry.Packages);
                    cardMap[c.Id] = entry;
                }

                if (!entry.Packages.Contains(pkg.Id, StringComparer.OrdinalIgnoreCase))
                    entry.Packages.Add(pkg.Id);

                bool isNormalOrToken = (c.Type & (int)CardType.Normal) != 0 || (c.Type & (int)CardType.Token) != 0;
                if (!isNormalOrToken && !summary.HasScript)
                    pkgMissingScripts.Add(c.Id);

                if (!summary.HasImage)
                    pkgMissingImages.Add(c.Id);
            }

            if (pkgMissingScripts.Count > 0)
                missingScripts[pkg.Id] = pkgMissingScripts;
            if (pkgMissingImages.Count > 0)
                missingImages[pkg.Id] = pkgMissingImages;
        }

        List<DiyCardConflictItem> conflicts = new();
        foreach ((int cardId, (string name, List<string> pkgs)) in cardMap.OrderBy(static kv => kv.Key))
        {
            bool overridesBase = baselineRepository is not null && baselineRepository.TryGetCard(cardId, out _);
            if (pkgs.Count > 1 || overridesBase)
            {
                conflicts.Add(new DiyCardConflictItem(cardId, name, pkgs.ToArray(), overridesBase));
            }
        }

        return new DiyGlobalConflictReport(conflicts, missingScripts, missingImages);
    }

    /// <summary>
    /// 将受管 DIY 包打包导出为标准客户端可直接放入 <c>expansions/</c> 的 <c>.ypk</c> 压缩包字节数组。
    /// </summary>
    public byte[] ExportPackageAsYpk(string packageId)
    {
        EnsureValidPackageId(packageId);
        string pkgDir = GetPackageDirectory(packageId);
        if (!Directory.Exists(pkgDir))
            throw new DirectoryNotFoundException($"DIY package '{packageId}' does not exist.");

        using MemoryStream ms = new();
        using (ZipArchive zip = new(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (string topFile in Directory.EnumerateFiles(pkgDir, "*.*", SearchOption.TopDirectoryOnly))
            {
                string ext = Path.GetExtension(topFile);
                string fileName = Path.GetFileName(topFile);
                if (fileName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (ext.Equals(".cdb", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals(".conf", StringComparison.OrdinalIgnoreCase))
                {
                    AddFileToZip(zip, topFile, fileName);
                }
            }

            foreach (string subFolder in new[] { "script", "pics", "decks" })
            {
                string subPath = Path.Combine(pkgDir, subFolder);
                if (!Directory.Exists(subPath))
                    continue;

                foreach (string file in Directory.EnumerateFiles(subPath, "*.*", SearchOption.AllDirectories))
                {
                    string ext = Path.GetExtension(file);
                    if (!AllowedExtensions.Contains(ext))
                        continue;

                    string rel = Path.GetRelativePath(pkgDir, file).Replace('\\', '/');
                    AddFileToZip(zip, file, rel);
                }
            }
        }

        return ms.ToArray();
    }

    /// <summary>
    /// 将当前所有已启用（Enabled）的受管 DIY 包按优先级合并打包为单个全服整合 <c>.ypk</c> 字节数组。
    /// </summary>
    public byte[] ExportAllEnabledPackagesAsYpk()
    {
        IReadOnlyList<DiyPackageManifest> enabled = ListPackages(enabledOnly: true);
        if (enabled.Count == 0)
            throw new InvalidOperationException("No enabled DIY packages available to export.");

        if (enabled.Count == 1)
            return ExportPackageAsYpk(enabled[0].Id);

        string stagingDir = Path.Combine(_diyRootPath, ".staging", "export_bundle_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDir);
        try
        {
            string mergedCdbPath = Path.Combine(stagingDir, "cards.cdb");
            bool hasAnyCard = false;
            StringBuilder mergedStrings = new();
            StringBuilder mergedLflist = new();
            Dictionary<string, string> scriptFiles = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> picFiles = new(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> deckFiles = new(StringComparer.OrdinalIgnoreCase);

            // ListPackages 按 Priority 升序排列，低优先级先写入，高优先级后覆盖
            foreach (DiyPackageManifest pkg in enabled)
            {
                string pkgDir = GetPackageDirectory(pkg.Id);
                if (!Directory.Exists(pkgDir))
                    continue;

                foreach (DiyCardSummary item in ListPackageCards(pkg.Id))
                {
                    CardDatabaseValidationResult res = _databaseManager.UpsertCard(mergedCdbPath, item.Card);
                    if (res.IsValid)
                        hasAnyCard = true;
                }

                string stringsPath = Path.Combine(pkgDir, "strings.conf");
                if (File.Exists(stringsPath))
                {
                    string txt = File.ReadAllText(stringsPath, Encoding.UTF8).Trim();
                    if (txt.Length > 0)
                    {
                        mergedStrings.AppendLine($"# --- {pkg.Id} ---");
                        mergedStrings.AppendLine(txt);
                    }
                }

                string lfPath = Path.Combine(pkgDir, "lflist.conf");
                if (File.Exists(lfPath))
                {
                    string txt = File.ReadAllText(lfPath, Encoding.UTF8).Trim();
                    if (txt.Length > 0)
                    {
                        mergedLflist.AppendLine(txt);
                    }
                }

                string sDir = Path.Combine(pkgDir, "script");
                if (Directory.Exists(sDir))
                {
                    foreach (string f in Directory.EnumerateFiles(sDir, "*.lua", SearchOption.AllDirectories))
                    {
                        string rel = "script/" + Path.GetRelativePath(sDir, f).Replace('\\', '/');
                        scriptFiles[rel] = f;
                    }
                }

                string pDir = Path.Combine(pkgDir, "pics");
                if (Directory.Exists(pDir))
                {
                    foreach (string f in Directory.EnumerateFiles(pDir, "*.*", SearchOption.TopDirectoryOnly))
                    {
                        string ext = Path.GetExtension(f);
                        if (ext.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                            ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                            ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
                        {
                            string baseId = Path.GetFileNameWithoutExtension(f);
                            // 移除同名不同后缀的旧覆盖项
                            picFiles.Remove($"pics/{baseId}.png");
                            picFiles.Remove($"pics/{baseId}.jpg");
                            picFiles.Remove($"pics/{baseId}.jpeg");
                            picFiles[$"pics/{Path.GetFileName(f)}"] = f;
                        }
                    }
                }

                string dDir = Path.Combine(pkgDir, "decks");
                if (Directory.Exists(dDir))
                {
                    foreach (string f in Directory.EnumerateFiles(dDir, "*.ydk", SearchOption.TopDirectoryOnly))
                    {
                        deckFiles[$"decks/{Path.GetFileName(f)}"] = f;
                    }
                }
            }

            using MemoryStream ms = new();
            using (ZipArchive zip = new(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                if (hasAnyCard && File.Exists(mergedCdbPath))
                    AddFileToZip(zip, mergedCdbPath, "cards.cdb");

                if (mergedStrings.Length > 0)
                    AddBytesToZip(zip, Encoding.UTF8.GetBytes(mergedStrings.ToString()), "strings.conf");

                if (mergedLflist.Length > 0)
                    AddBytesToZip(zip, Encoding.UTF8.GetBytes(mergedLflist.ToString()), "lflist.conf");

                foreach ((string entryName, string srcPath) in scriptFiles.OrderBy(static kv => kv.Key, StringComparer.OrdinalIgnoreCase))
                    AddFileToZip(zip, srcPath, entryName);

                foreach ((string entryName, string srcPath) in picFiles.OrderBy(static kv => kv.Key, StringComparer.OrdinalIgnoreCase))
                    AddFileToZip(zip, srcPath, entryName);

                foreach ((string entryName, string srcPath) in deckFiles.OrderBy(static kv => kv.Key, StringComparer.OrdinalIgnoreCase))
                    AddFileToZip(zip, srcPath, entryName);
            }

            return ms.ToArray();
        }
        finally
        {
            try
            {
                if (Directory.Exists(stagingDir))
                    Directory.Delete(stagingDir, recursive: true);
            }
            catch
            {
                // 忽略临时目录清理异常
            }
        }
    }

    /// <summary>
    /// 遵循 <c>Working Copy -&gt; 预检 -&gt; 原子替换</c> 安全导入标准 <c>.ypk</c> / <c>.zip</c> 扩展包：
    /// 校验路径穿越（ZIP Slip）、Lua 危险系统调用、SQLite <c>.cdb</c> 表结构、卡图 PNG/JPEG 魔数头与 <c>.ydk</c> 语法。
    /// </summary>
    public DiyYpkImportResult ImportPackageFromYpk(
        string packageId,
        Stream archiveStream,
        string? displayName = null,
        string? author = null,
        bool mergeIntoExisting = true,
        long maxUncompressedBytes = 512L * 1024 * 1024)
    {
        EnsureValidPackageId(packageId);
        ArgumentNullException.ThrowIfNull(archiveStream);

        if (archiveStream.CanSeek)
            archiveStream.Position = 0;

        IReadOnlyList<string> auditErrors;
        try
        {
            auditErrors = AuditArchiveEntries(archiveStream, maxUncompressedBytes);
        }
        catch (InvalidDataException)
        {
            return new DiyYpkImportResult(packageId, false, 0, 0, 0, 0, 0, 0, ["Invalid or corrupted .ypk / ZIP archive."]);
        }

        if (auditErrors.Count > 0)
            return new DiyYpkImportResult(packageId, false, 0, 0, 0, 0, 0, 0, auditErrors);

        if (archiveStream.CanSeek)
            archiveStream.Position = 0;

        string pkgDir = GetPackageDirectory(packageId);
        string stagingRoot = Path.Combine(_diyRootPath, ".staging");
        Directory.CreateDirectory(stagingRoot);
        string stagingDir = Path.Combine(stagingRoot, $"{packageId}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDir);

        List<string> errors = new();
        int importedCdbs = 0;
        int importedScripts = 0;
        int importedImages = 0;
        int importedDecks = 0;
        int importedConfs = 0;

        try
        {
            DiyPackageManifest? existingManifest = null;
            if (Directory.Exists(pkgDir))
            {
                existingManifest = ReadOrCreateDefaultManifest(packageId, pkgDir);
                if (mergeIntoExisting)
                    CopyDirectoryRecursively(pkgDir, stagingDir);
            }

            DiyPackageManifest? archiveManifest = null;
            using (ZipArchive archive = new(archiveStream, ZipArchiveMode.Read, leaveOpen: true))
            {
                string? wrapperPrefix = DetectSingleWrapperFolderPrefix(archive);
                string stagingMergedCdb = Path.Combine(stagingDir, "cards.cdb");

                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name))
                        continue;

                    string relPath = entry.FullName.Replace('\\', '/').TrimStart('/');
                    if (wrapperPrefix is not null && relPath.StartsWith(wrapperPrefix, StringComparison.OrdinalIgnoreCase))
                        relPath = relPath[wrapperPrefix.Length..];

                    if (string.IsNullOrWhiteSpace(relPath))
                        continue;

                    string[] segments = relPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    if (segments.Length == 0)
                        continue;

                    string topSeg = segments[0];
                    string fileName = Path.GetFileName(relPath);
                    string ext = Path.GetExtension(fileName).ToLowerInvariant();
                    byte[] bytes = ReadEntryBytes(entry);

                    if (segments.Length == 1 && fileName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            archiveManifest = JsonSerializer.Deserialize<DiyPackageManifest>(bytes, JsonOptions);
                        }
                        catch
                        {
                            // 忽略非标准 manifest.json
                        }
                        continue;
                    }

                    if (ext == ".cdb" && (segments.Length == 1 || topSeg.Equals("cdb", StringComparison.OrdinalIgnoreCase)))
                    {
                        string tempCdb = Path.Combine(stagingDir, $"__verify_{Guid.NewGuid():N}.cdb");
                        try
                        {
                            File.WriteAllBytes(tempCdb, bytes);
                            CardDatabaseValidationResult val = _databaseManager.ValidateDatabase(tempCdb);
                            if (!val.IsValid)
                            {
                                errors.Add($"{entry.FullName}: {val.ErrorMessage ?? "Invalid SQLite card database."}");
                                continue;
                            }

                            if (!File.Exists(stagingMergedCdb))
                            {
                                File.Move(tempCdb, stagingMergedCdb, overwrite: true);
                            }
                            else
                            {
                                foreach (DiyCardEntry card in _databaseManager.LoadEditableCards(tempCdb))
                                {
                                    CardDatabaseValidationResult upRes = _databaseManager.UpsertCard(stagingMergedCdb, card);
                                    if (!upRes.IsValid)
                                    {
                                        errors.Add($"{entry.FullName} (c{card.Id}): {upRes.ErrorMessage}");
                                        break;
                                    }
                                }
                            }
                            importedCdbs++;
                        }
                        finally
                        {
                            if (File.Exists(tempCdb))
                            {
                                try { File.Delete(tempCdb); } catch { }
                            }
                        }
                    }
                    else if (ext == ".lua" && (segments.Length == 1 || topSeg.Equals("script", StringComparison.OrdinalIgnoreCase) || topSeg.Equals("scripts", StringComparison.OrdinalIgnoreCase)))
                    {
                        string scriptDir = Path.Combine(stagingDir, "script");
                        string subRel = topSeg.Equals("script", StringComparison.OrdinalIgnoreCase) || topSeg.Equals("scripts", StringComparison.OrdinalIgnoreCase)
                            ? string.Join('/', segments.Skip(1))
                            : fileName;
                        string destScript = EnsureSafeChildPath(scriptDir, subRel, ".lua");
                        Directory.CreateDirectory(Path.GetDirectoryName(destScript)!);
                        File.WriteAllBytes(destScript, bytes);
                        importedScripts++;
                    }
                    else if (ext is ".png" or ".jpg" or ".jpeg" or ".webp" &&
                             (segments.Length == 1 || topSeg.Equals("pics", StringComparison.OrdinalIgnoreCase) || topSeg.Equals("picture", StringComparison.OrdinalIgnoreCase)))
                    {
                        // 跳过缩略图子目录（如 pics/thumbnail/*）以免低清图覆盖主卡图
                        if (segments.Length >= 3 && segments[1].Equals("thumbnail", StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (bytes.Length > 8 * 1024 * 1024)
                        {
                            errors.Add($"{entry.FullName}: Card image exceeds 8MB limit.");
                            continue;
                        }

                        string? normalizedImgExt = DetectImageExtension(bytes);
                        if (normalizedImgExt is null)
                        {
                            errors.Add($"{entry.FullName}: Invalid PNG/JPEG/WEBP image header.");
                            continue;
                        }

                        string baseName = Path.GetFileNameWithoutExtension(fileName);
                        if (!int.TryParse(baseName, out int parsedCardId) || parsedCardId <= 0)
                            continue;

                        string picsDir = Path.Combine(stagingDir, "pics");
                        Directory.CreateDirectory(picsDir);
                        foreach (string oldExt in new[] { ".png", ".jpg", ".jpeg", ".webp" })
                        {
                            string oldFile = Path.Combine(picsDir, $"{parsedCardId}{oldExt}");
                            if (File.Exists(oldFile))
                                File.Delete(oldFile);
                        }

                        string destPic = EnsureSafeChildPath(picsDir, $"{parsedCardId}{normalizedImgExt}", normalizedImgExt);
                        File.WriteAllBytes(destPic, bytes);
                        importedImages++;
                    }
                    else if (ext == ".ydk" &&
                             (segments.Length == 1 || topSeg.Equals("deck", StringComparison.OrdinalIgnoreCase) || topSeg.Equals("decks", StringComparison.OrdinalIgnoreCase)))
                    {
                        string ydkText = Encoding.UTF8.GetString(bytes);
                        YdkDeckData parsedDeck = YdkSerializer.Parse(ydkText, Path.GetFileNameWithoutExtension(fileName));
                        if (parsedDeck.Main.Count == 0 && parsedDeck.Extra.Count == 0)
                            continue;

                        string decksDir = Path.Combine(stagingDir, "decks");
                        Directory.CreateDirectory(decksDir);
                        string destDeck = EnsureSafeChildPath(decksDir, fileName, ".ydk");
                        File.WriteAllBytes(destDeck, Encoding.UTF8.GetBytes(ydkText));
                        importedDecks++;
                    }
                    else if (ext == ".conf" && segments.Length == 1 &&
                             (fileName.Equals("strings.conf", StringComparison.OrdinalIgnoreCase) ||
                              fileName.Equals("lflist.conf", StringComparison.OrdinalIgnoreCase)))
                    {
                        string confText = Encoding.UTF8.GetString(bytes);
                        if (fileName.Equals("lflist.conf", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                _ = BanlistManager.ParseText(confText);
                            }
                            catch (Exception ex)
                            {
                                errors.Add($"{entry.FullName}: Invalid lflist.conf ({ex.Message}).");
                                continue;
                            }
                        }

                        string canonicalConf = fileName.Equals("strings.conf", StringComparison.OrdinalIgnoreCase) ? "strings.conf" : "lflist.conf";
                        string destConf = EnsureSafeChildPath(stagingDir, canonicalConf, ".conf");
                        File.WriteAllBytes(destConf, Encoding.UTF8.GetBytes(confText));
                        importedConfs++;
                    }
                }
            }

            if (errors.Count > 0)
                return new DiyYpkImportResult(packageId, false, 0, 0, 0, 0, 0, 0, errors);

            if (importedCdbs == 0 && importedScripts == 0 && importedImages == 0 && importedDecks == 0 && importedConfs == 0)
            {
                return new DiyYpkImportResult(
                    packageId,
                    false,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    ["Archive does not contain any recognizable YGOPro .ypk assets (.cdb, script/*.lua, pics/*, deck/*.ydk, strings.conf, lflist.conf)."]);
            }

            // 若原本存在多个零散 .cdb，在合并进 cards.cdb 后清理其他 .cdb 以保证单卡编辑器一致性
            string finalMergedCdb = Path.Combine(stagingDir, "cards.cdb");
            if (File.Exists(finalMergedCdb))
            {
                foreach (string extraCdb in Directory.EnumerateFiles(stagingDir, "*.cdb", SearchOption.TopDirectoryOnly))
                {
                    if (!string.Equals(Path.GetFileName(extraCdb), "cards.cdb", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (DiyCardEntry c in _databaseManager.LoadEditableCards(extraCdb))
                            _databaseManager.UpsertCard(finalMergedCdb, c);
                        File.Delete(extraCdb);
                    }
                }
            }

            DiyPackageManifest finalManifest = new()
            {
                Id = packageId,
                Name = !string.IsNullOrWhiteSpace(displayName)
                    ? displayName.Trim()
                    : !string.IsNullOrWhiteSpace(archiveManifest?.Name)
                        ? archiveManifest.Name.Trim()
                        : existingManifest?.Name ?? packageId,
                Version = !string.IsNullOrWhiteSpace(archiveManifest?.Version)
                    ? archiveManifest.Version.Trim()
                    : existingManifest?.Version ?? "1.0.0",
                Author = !string.IsNullOrWhiteSpace(author)
                    ? author.Trim()
                    : !string.IsNullOrWhiteSpace(archiveManifest?.Author)
                        ? archiveManifest.Author.Trim()
                        : existingManifest?.Author ?? string.Empty,
                Enabled = existingManifest?.Enabled ?? true,
                Priority = existingManifest?.Priority ?? archiveManifest?.Priority ?? 100,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            File.WriteAllBytes(
                Path.Combine(stagingDir, "manifest.json"),
                JsonSerializer.SerializeToUtf8Bytes(finalManifest, JsonOptions));

            // 原子目录切换
            Directory.CreateDirectory(PackagesDirectory);
            string? backupDir = null;
            if (Directory.Exists(pkgDir))
            {
                backupDir = Path.Combine(stagingRoot, $"{packageId}_bak_{Guid.NewGuid():N}");
                Directory.Move(pkgDir, backupDir);
            }

            try
            {
                Directory.Move(stagingDir, pkgDir);
            }
            catch
            {
                if (backupDir is not null && Directory.Exists(backupDir) && !Directory.Exists(pkgDir))
                {
                    try { Directory.Move(backupDir, pkgDir); } catch { }
                }
                throw;
            }
            finally
            {
                if (backupDir is not null && Directory.Exists(backupDir))
                {
                    try { Directory.Delete(backupDir, recursive: true); } catch { }
                }
            }

            int totalCards = ListPackageCards(packageId).Count;
            return new DiyYpkImportResult(
                packageId,
                true,
                importedCdbs,
                totalCards,
                importedScripts,
                importedImages,
                importedDecks,
                importedConfs,
                []);
        }
        finally
        {
            if (Directory.Exists(stagingDir))
            {
                try { Directory.Delete(stagingDir, recursive: true); } catch { }
            }
        }
    }

    private static void AddBytesToZip(ZipArchive zip, byte[] bytes, string entryName)
    {
        ZipArchiveEntry entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using Stream entryStream = entry.Open();
        entryStream.Write(bytes, 0, bytes.Length);
    }

    private static byte[] ReadEntryBytes(ZipArchiveEntry entry)
    {
        using Stream s = entry.Open();
        using MemoryStream ms = new();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static string? DetectImageExtension(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return ".png";
        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return ".jpg";
        if (bytes.Length >= 12 &&
            bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F' &&
            bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P')
            return ".webp";
        return null;
    }

    private static string ClassifyFileCategory(string relativePath, string ext)
    {
        return ext.ToLowerInvariant() switch
        {
            ".cdb" => "cdb",
            ".lua" => "lua",
            ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".gif" => "image",
            ".ydk" => "ydk",
            ".conf" or ".ini" or ".toml" or ".yml" or ".yaml" or ".json" => "conf",
            ".ogg" or ".mp3" or ".wav" => "audio",
            ".txt" or ".md" or ".csv" => "doc",
            _ => relativePath.Contains("README", StringComparison.OrdinalIgnoreCase) ||
                 relativePath.Contains("LICENSE", StringComparison.OrdinalIgnoreCase)
                ? "doc"
                : "other"
        };
    }

    private static bool IsTextPreviewableCategory(string category, string ext)
    {
        if (category is "lua" or "ydk" or "conf" or "doc")
            return true;
        return ext.ToLowerInvariant() is ".lua" or ".ydk" or ".conf" or ".ini" or ".json" or ".txt" or ".md" or ".toml" or ".yml" or ".yaml" or ".csv";
    }

    private static string DecodeTextTolerant(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            bytes = bytes[3..];
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.UTF8.GetString(bytes);
        }
    }

    /// <summary>
    /// 零副作用解析 <c>.ypk</c> / <c>.zip</c> 压缩包内部全部文件目录树、体积分布、卡片列表、卡组与安全状态。
    /// </summary>
    public DiyYpkInspectionReport InspectYpkArchive(
        Stream archiveStream,
        string archiveName,
        long maxUncompressedBytes = 512L * 1024 * 1024)
    {
        ArgumentNullException.ThrowIfNull(archiveStream);
        long compressedLen = archiveStream.CanSeek ? archiveStream.Length : 0;
        if (archiveStream.CanSeek)
            archiveStream.Position = 0;

        IReadOnlyList<string> auditErrors;
        try
        {
            auditErrors = AuditArchiveEntries(archiveStream, maxUncompressedBytes);
        }
        catch (InvalidDataException)
        {
            return new DiyYpkInspectionReport(
                archiveName,
                false,
                compressedLen,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                [],
                [],
                [],
                [],
                ParseStringsConf(string.Empty),
                ["无法识别的压缩包格式：请确认文件为标准 .ypk 或 .zip。"]);
        }

        if (archiveStream.CanSeek)
            archiveStream.Position = 0;

        List<string> errors = new(auditErrors);
        List<DiyArchiveFileEntry> files = new();
        Dictionary<int, DiyCardEntry> cardsById = new();
        HashSet<int> scriptCardIds = new();
        HashSet<int> imageCardIds = new();
        List<DiyDeckBrief> decks = new();
        StringBuilder stringsBuilder = new();
        long totalUncompressed = 0;
        int scriptCount = 0;
        int imageCount = 0;
        int deckCount = 0;
        int confCount = 0;

        string tempDir = Path.Combine(_diyRootPath, ".staging", "inspect_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            using ZipArchive archive = new(archiveStream, ZipArchiveMode.Read, leaveOpen: true);
            string? wrapperPrefix = DetectSingleWrapperFolderPrefix(archive);

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue;

                string rawRel = entry.FullName.Replace('\\', '/').TrimStart('/');
                if (rawRel.Split('/').Any(static s => s == "..") || rawRel.Contains(':', StringComparison.Ordinal))
                    continue;

                string logicalRel = wrapperPrefix is not null && rawRel.StartsWith(wrapperPrefix, StringComparison.OrdinalIgnoreCase)
                    ? rawRel[wrapperPrefix.Length..]
                    : rawRel;
                if (string.IsNullOrWhiteSpace(logicalRel))
                    logicalRel = rawRel;

                string fileName = Path.GetFileName(logicalRel);
                string folder = Path.GetDirectoryName(logicalRel)?.Replace('\\', '/') ?? string.Empty;
                string ext = Path.GetExtension(fileName).ToLowerInvariant();
                string category = ClassifyFileCategory(logicalRel, ext);
                long uncomp = Math.Max(0, entry.Length);
                long comp = Math.Max(0, entry.CompressedLength);
                totalUncompressed += uncomp;

                files.Add(new DiyArchiveFileEntry(logicalRel, fileName, folder, category, comp, uncomp));

                if (category == "lua")
                {
                    scriptCount++;
                    if (fileName.StartsWith('c') && int.TryParse(Path.GetFileNameWithoutExtension(fileName)[1..], out int scId) && scId > 0)
                        scriptCardIds.Add(scId);
                }
                else if (category == "image")
                {
                    imageCount++;
                    string[] segs = logicalRel.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    bool isThumb = segs.Length >= 3 && segs[1].Equals("thumbnail", StringComparison.OrdinalIgnoreCase);
                    if (!isThumb && int.TryParse(Path.GetFileNameWithoutExtension(fileName), out int imgId) && imgId > 0)
                        imageCardIds.Add(imgId);
                }
                else if (category == "ydk" && entry.Length <= 512 * 1024)
                {
                    deckCount++;
                    byte[] ydkBytes = ReadEntryBytes(entry);
                    string ydkTxt = DecodeTextTolerant(ydkBytes);
                    YdkDeckData parsed = YdkSerializer.Parse(ydkTxt, Path.GetFileNameWithoutExtension(fileName));
                    decks.Add(new DiyDeckBrief(
                        Path.GetFileNameWithoutExtension(fileName),
                        logicalRel,
                        parsed.Main.Count,
                        parsed.Extra.Count,
                        parsed.Side.Count));
                }
                else if (category == "conf" && entry.Length <= 2 * 1024 * 1024)
                {
                    confCount++;
                    if (fileName.Equals("strings.conf", StringComparison.OrdinalIgnoreCase))
                    {
                        byte[] confBytes = ReadEntryBytes(entry);
                        stringsBuilder.AppendLine(DecodeTextTolerant(confBytes));
                    }
                }
                else if (category == "cdb" && entry.Length <= 64L * 1024 * 1024)
                {
                    string tempCdb = Path.Combine(tempDir, $"__insp_{Guid.NewGuid():N}.cdb");
                    try
                    {
                        File.WriteAllBytes(tempCdb, ReadEntryBytes(entry));
                        CardDatabaseValidationResult val = _databaseManager.ValidateDatabase(tempCdb);
                        if (!val.IsValid)
                        {
                            errors.Add($"{logicalRel}: {val.ErrorMessage ?? "SQLite 卡表结构不合法"}");
                        }
                        else
                        {
                            foreach (DiyCardEntry c in _databaseManager.LoadEditableCards(tempCdb))
                                cardsById[c.Id] = c;
                        }
                    }
                    finally
                    {
                        if (File.Exists(tempCdb))
                        {
                            try { File.Delete(tempCdb); } catch { }
                        }
                    }
                }
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }

        DiyCardSummary[] cardList = cardsById.Values
            .OrderBy(static c => c.Id)
            .Take(800)
            .Select(c => new DiyCardSummary(c, scriptCardIds.Contains(c.Id), imageCardIds.Contains(c.Id)))
            .ToArray();

        DiyCategoryStat[] categoryStats = files
            .GroupBy(static f => f.Category, StringComparer.OrdinalIgnoreCase)
            .Select(static g => new DiyCategoryStat(g.Key, g.Count(), g.Sum(static x => x.UncompressedBytes)))
            .OrderByDescending(static s => s.TotalBytes)
            .ToArray();

        return new DiyYpkInspectionReport(
            archiveName,
            errors.Count == 0,
            compressedLen,
            totalUncompressed,
            files.Count,
            cardsById.Count,
            scriptCount,
            imageCount,
            deckCount,
            confCount,
            categoryStats,
            files.OrderBy(static f => f.Path, StringComparer.OrdinalIgnoreCase).ToArray(),
            cardList,
            decks,
            ParseStringsConf(stringsBuilder.ToString()),
            errors);
    }

    /// <summary>
    /// 生成已安装受管 DIY 包的完整文件树与可视化结构报告（与 <see cref="InspectYpkArchive"/> 返回同构模型）。
    /// </summary>
    public DiyYpkInspectionReport InspectInstalledPackage(string packageId)
    {
        EnsureValidPackageId(packageId);
        string pkgDir = GetPackageDirectory(packageId);
        if (!Directory.Exists(pkgDir))
            throw new DirectoryNotFoundException($"DIY package '{packageId}' does not exist.");

        List<DiyArchiveFileEntry> files = new();
        List<DiyDeckBrief> decks = new();
        long totalBytes = 0;
        int scriptCount = 0;
        int imageCount = 0;
        int deckCount = 0;
        int confCount = 0;

        foreach (string fullPath in Directory.EnumerateFiles(pkgDir, "*.*", SearchOption.AllDirectories))
        {
            if (HasReparsePointInChain(pkgDir, fullPath))
                continue;

            string rel = Path.GetRelativePath(pkgDir, fullPath).Replace('\\', '/');
            string fileName = Path.GetFileName(fullPath);
            string folder = Path.GetDirectoryName(rel)?.Replace('\\', '/') ?? string.Empty;
            string ext = Path.GetExtension(fileName).ToLowerInvariant();
            string category = ClassifyFileCategory(rel, ext);
            long len = new FileInfo(fullPath).Length;
            totalBytes += len;

            files.Add(new DiyArchiveFileEntry(rel, fileName, folder, category, len, len));
            if (category == "lua") scriptCount++;
            else if (category == "image") imageCount++;
            else if (category == "conf") confCount++;
            else if (category == "ydk" && len <= 512 * 1024)
            {
                deckCount++;
                YdkDeckData parsed = YdkSerializer.Parse(File.ReadAllText(fullPath, Encoding.UTF8), Path.GetFileNameWithoutExtension(fileName));
                decks.Add(new DiyDeckBrief(
                    Path.GetFileNameWithoutExtension(fileName),
                    rel,
                    parsed.Main.Count,
                    parsed.Extra.Count,
                    parsed.Side.Count));
            }
        }

        IReadOnlyList<DiyCardSummary> cards = ListPackageCards(packageId);
        DiyPackageValidationReport val = ValidatePackage(packageId);
        DiyCategoryStat[] categoryStats = files
            .GroupBy(static f => f.Category, StringComparer.OrdinalIgnoreCase)
            .Select(static g => new DiyCategoryStat(g.Key, g.Count(), g.Sum(static x => x.UncompressedBytes)))
            .OrderByDescending(static s => s.TotalBytes)
            .ToArray();

        return new DiyYpkInspectionReport(
            $"{packageId}.ypk",
            val.IsValid,
            totalBytes,
            totalBytes,
            files.Count,
            cards.Count,
            scriptCount,
            imageCount,
            deckCount,
            confCount,
            categoryStats,
            files.OrderBy(static f => f.Path, StringComparer.OrdinalIgnoreCase).ToArray(),
            cards,
            decks,
            ReadStringsConf(packageId),
            val.Errors);
    }

    /// <summary>
    /// 按路径或卡密按需安全读取 <c>.ypk</c> 压缩包内的单个文本或卡图条目（供前端在线可视化预览）。
    /// </summary>
    public static (byte[] Bytes, string ContentType, bool IsText, string TextContent, string ResolvedPath)? TryReadYpkArchiveEntry(
        Stream archiveStream,
        string? entryPath = null,
        int? cardImageId = null)
    {
        ArgumentNullException.ThrowIfNull(archiveStream);
        if (archiveStream.CanSeek)
            archiveStream.Position = 0;

        using ZipArchive archive = new(archiveStream, ZipArchiveMode.Read, leaveOpen: true);
        string? wrapperPrefix = DetectSingleWrapperFolderPrefix(archive);

        string normalizedTarget = (entryPath ?? string.Empty).Replace('\\', '/').TrimStart('/');
        if (normalizedTarget.Contains("..", StringComparison.Ordinal) || normalizedTarget.Contains(':', StringComparison.Ordinal))
            return null;

        ZipArchiveEntry? matched = null;
        string resolvedLogical = string.Empty;

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            string rawRel = entry.FullName.Replace('\\', '/').TrimStart('/');
            if (rawRel.Split('/').Any(static s => s == "..") || rawRel.Contains(':', StringComparison.Ordinal))
                continue;

            string logicalRel = wrapperPrefix is not null && rawRel.StartsWith(wrapperPrefix, StringComparison.OrdinalIgnoreCase)
                ? rawRel[wrapperPrefix.Length..]
                : rawRel;

            if (cardImageId is > 0)
            {
                string[] segs = logicalRel.Split('/', StringSplitOptions.RemoveEmptyEntries);
                bool isThumb = segs.Length >= 3 && segs[1].Equals("thumbnail", StringComparison.OrdinalIgnoreCase);
                string ext = Path.GetExtension(entry.Name).ToLowerInvariant();
                if (!isThumb &&
                    ext is ".png" or ".jpg" or ".jpeg" or ".webp" &&
                    int.TryParse(Path.GetFileNameWithoutExtension(entry.Name), out int cid) &&
                    cid == cardImageId.Value)
                {
                    matched = entry;
                    resolvedLogical = logicalRel;
                    break;
                }
            }
            else if (!string.IsNullOrEmpty(normalizedTarget) &&
                     (string.Equals(logicalRel, normalizedTarget, StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(rawRel, normalizedTarget, StringComparison.OrdinalIgnoreCase)))
            {
                matched = entry;
                resolvedLogical = logicalRel;
                break;
            }
        }

        if (matched is null || matched.Length > 12 * 1024 * 1024)
            return null;

        byte[] bytes = ReadEntryBytes(matched);
        string mExt = Path.GetExtension(matched.Name).ToLowerInvariant();
        string cat = ClassifyFileCategory(resolvedLogical, mExt);
        if (cat == "image")
        {
            string? detected = DetectImageExtension(bytes);
            string mime = detected == ".png" ? "image/png" : detected == ".webp" ? "image/webp" : "image/jpeg";
            return (bytes, mime, false, string.Empty, resolvedLogical);
        }

        if (IsTextPreviewableCategory(cat, mExt))
        {
            return (bytes, "text/plain; charset=utf-8", true, DecodeTextTolerant(bytes), resolvedLogical);
        }

        return null;
    }

    public (byte[] Bytes, string ContentType, bool IsText, string TextContent, string ResolvedPath)? TryReadInstalledPackageFile(
        string packageId,
        string relativePath)
    {
        EnsureValidPackageId(packageId);
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        string normalized = relativePath.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        string pkgDir = GetPackageDirectory(packageId);
        string safeFull = EnsureSafeDirectoryChild(pkgDir, normalized);
        if (!File.Exists(safeFull))
            return null;

        FileInfo fi = new(safeFull);
        if (fi.Length > 12 * 1024 * 1024)
            return null;

        byte[] bytes = File.ReadAllBytes(safeFull);
        string ext = Path.GetExtension(safeFull).ToLowerInvariant();
        string relSlash = Path.GetRelativePath(pkgDir, safeFull).Replace('\\', '/');
        string cat = ClassifyFileCategory(relSlash, ext);

        if (cat == "image")
        {
            string? detected = DetectImageExtension(bytes);
            string mime = detected == ".png" ? "image/png" : detected == ".webp" ? "image/webp" : "image/jpeg";
            return (bytes, mime, false, string.Empty, relSlash);
        }

        if (IsTextPreviewableCategory(cat, ext))
        {
            return (bytes, "text/plain; charset=utf-8", true, DecodeTextTolerant(bytes), relSlash);
        }

        return null;
    }

    /// <summary>
    /// 将上传预览的 <c>.ypk</c> 暂存至 <c>.staging/previews/&lt;token&gt;.ypk</c>（自动清理超过 30 分钟的临时预览文件），支持先在线可视化查看再零重传一键导入/投稿。
    /// </summary>
    public (string PreviewToken, DiyYpkInspectionReport Report) SavePreviewYpk(Stream archiveStream, string originalFileName)
    {
        ArgumentNullException.ThrowIfNull(archiveStream);
        string previewsDir = Path.Combine(_diyRootPath, ".staging", "previews");
        Directory.CreateDirectory(previewsDir);
        CleanupExpiredPreviews(previewsDir, TimeSpan.FromMinutes(30));

        string token = Guid.NewGuid().ToString("N");
        string ypkPath = Path.Combine(previewsDir, token + ".ypk");
        string metaPath = Path.Combine(previewsDir, token + ".name");

        if (archiveStream.CanSeek)
            archiveStream.Position = 0;

        using (FileStream fs = new(ypkPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            archiveStream.CopyTo(fs);
        }

        string safeDisplayFile = Path.GetFileName(string.IsNullOrWhiteSpace(originalFileName) ? "upload.ypk" : originalFileName);
        File.WriteAllText(metaPath, safeDisplayFile, Encoding.UTF8);

        using FileStream readFs = new(ypkPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        DiyYpkInspectionReport report = InspectYpkArchive(readFs, safeDisplayFile);
        return (token, report);
    }

    public bool TryGetPreviewYpkPath(string? previewToken, out string ypkPath, out string originalFileName)
    {
        ypkPath = string.Empty;
        originalFileName = "package.ypk";
        if (string.IsNullOrWhiteSpace(previewToken) || !Guid.TryParseExact(previewToken.Trim(), "N", out Guid parsed))
            return false;

        string previewsDir = Path.Combine(_diyRootPath, ".staging", "previews");
        string normalizedToken = parsed.ToString("N");
        string candidate = EnsureSafeChildPath(previewsDir, normalizedToken + ".ypk", ".ypk");
        if (!File.Exists(candidate))
            return false;

        ypkPath = candidate;
        string metaPath = Path.Combine(previewsDir, normalizedToken + ".name");
        if (File.Exists(metaPath))
        {
            string savedName = File.ReadAllText(metaPath, Encoding.UTF8).Trim();
            if (!string.IsNullOrEmpty(savedName))
                originalFileName = savedName;
        }
        return true;
    }

    public string SaveStagedProposalYpk(string sourceYpkPath)
    {
        string proposalsDir = Path.Combine(_diyRootPath, ".proposals");
        Directory.CreateDirectory(proposalsDir);
        string fileName = $"prop_{Guid.NewGuid():N}.ypk";
        string dest = EnsureSafeChildPath(proposalsDir, fileName, ".ypk");
        File.Copy(sourceYpkPath, dest, overwrite: true);
        return fileName;
    }

    public bool TryGetStagedProposalYpkPath(string? stagedFileName, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(stagedFileName) ||
            stagedFileName.Contains("..", StringComparison.Ordinal) ||
            stagedFileName.IndexOfAny(['/', '\\', ':']) >= 0)
        {
            return false;
        }

        string proposalsDir = Path.Combine(_diyRootPath, ".proposals");
        string candidate = EnsureSafeChildPath(proposalsDir, stagedFileName.Trim(), ".ypk");
        if (!File.Exists(candidate))
            return false;

        fullPath = candidate;
        return true;
    }

    private static void CleanupExpiredPreviews(string previewsDir, TimeSpan ttl)
    {
        try
        {
            DateTime cutoff = DateTime.UtcNow - ttl;
            foreach (string file in Directory.EnumerateFiles(previewsDir))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch
        {
            // 忽略后台清理冲突
        }
    }

    private static string? DetectSingleWrapperFolderPrefix(ZipArchive archive)
    {
        string? commonFirstSeg = null;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            string rel = entry.FullName.Replace('\\', '/').TrimStart('/');
            string[] parts = rel.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length <= 1)
                return null;

            string first = parts[0];
            if (first.Equals("script", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("scripts", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("pics", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("picture", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("deck", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("decks", StringComparison.OrdinalIgnoreCase) ||
                first.Equals("cdb", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (commonFirstSeg is null)
                commonFirstSeg = first;
            else if (!string.Equals(commonFirstSeg, first, StringComparison.OrdinalIgnoreCase))
                return null;
        }

        return commonFirstSeg is not null ? commonFirstSeg + "/" : null;
    }

    private static void CopyDirectoryRecursively(string sourceDir, string destinationDir)
    {
        if (IsReparsePoint(sourceDir))
            throw new InvalidOperationException("Symbolic link or junction directory is forbidden in DIY storage.");

        Directory.CreateDirectory(destinationDir);
        foreach (string file in Directory.EnumerateFiles(sourceDir))
        {
            if (IsReparsePoint(file))
                throw new InvalidOperationException("Symbolic link file is forbidden in DIY storage.");

            string destFile = Path.Combine(destinationDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (string subDir in Directory.EnumerateDirectories(sourceDir))
        {
            if (IsReparsePoint(subDir))
                throw new InvalidOperationException("Symbolic link or junction subdirectory is forbidden in DIY storage.");

            string destSub = Path.Combine(destinationDir, Path.GetFileName(subDir));
            CopyDirectoryRecursively(subDir, destSub);
        }
    }

    private static void AddFileToZip(ZipArchive zip, string sourceFilePath, string entryName)
    {
        if (IsReparsePoint(sourceFilePath))
            throw new InvalidOperationException("Symbolic link file cannot be exported into .ypk archive.");

        ZipArchiveEntry entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using Stream entryStream = entry.Open();
        byte[] bytes = File.ReadAllBytes(sourceFilePath);
        entryStream.Write(bytes, 0, bytes.Length);
    }

    private static string? FindCardImagePath(string picsDirectory, int cardId)
    {
        if (!Directory.Exists(picsDirectory))
            return null;

        foreach (string ext in new[] { ".png", ".jpg", ".jpeg" })
        {
            string p = Path.Combine(picsDirectory, $"{cardId}{ext}");
            if (File.Exists(p))
                return p;
        }

        return null;
    }

    /// <summary>
    /// 对指定包执行完整性检查、缺失脚本巡检以及与基础卡库/其他启用包的卡密覆盖冲突预分析。
    /// </summary>
    public DiyPackageValidationReport ValidatePackage(string packageId, ICardRepository? baselineRepository = null)
    {
        EnsureValidPackageId(packageId);
        string pkgDir = GetPackageDirectory(packageId);
        if (!Directory.Exists(pkgDir))
            return new DiyPackageValidationReport(packageId, false, 0, 0, [], [], ["Package directory does not exist."]);

        List<string> errors = new();
        List<int> overriddenIds = new();
        List<int> missingScripts = new();
        int totalCards = 0;
        int customCards = 0;

        string scriptDir = Path.Combine(pkgDir, "script");
        foreach (string cdbPath in Directory.EnumerateFiles(pkgDir, "*.cdb", SearchOption.TopDirectoryOnly))
        {
            CardDatabaseValidationResult res = _databaseManager.ValidateDatabase(cdbPath);
            if (!res.IsValid)
            {
                errors.Add($"{Path.GetFileName(cdbPath)}: {res.ErrorMessage}");
                continue;
            }

            totalCards += res.TotalCards;
            customCards += res.CustomCards;

            ICardRepository pkgRepo = _databaseManager.LoadCards(cdbPath);
            foreach (int id in res.CardIds)
            {
                if (baselineRepository is not null && baselineRepository.TryGetCard(id, out _))
                    overriddenIds.Add(id);

                if (pkgRepo.TryGetCard(id, out Card card) &&
                    !card.HasType(CardType.Normal) &&
                    !card.HasType(CardType.Token))
                {
                    string expectedScript = Path.Combine(scriptDir, $"c{id}.lua");
                    if (!File.Exists(expectedScript))
                        missingScripts.Add(id);
                }
            }
        }

        if (Directory.Exists(scriptDir))
        {
            foreach (string luaFile in Directory.EnumerateFiles(scriptDir, "*.lua", SearchOption.AllDirectories))
            {
                LuaScriptSecurityAudit audit = AuditLuaScript(File.ReadAllBytes(luaFile));
                if (!audit.IsSafe)
                {
                    foreach (string v in audit.Violations)
                        errors.Add($"{Path.GetFileName(luaFile)}: {v}");
                }
            }
        }

        return new DiyPackageValidationReport(
            packageId,
            errors.Count == 0,
            totalCards,
            customCards,
            overriddenIds.Distinct().ToArray(),
            missingScripts.Distinct().ToArray(),
            errors);
    }

    /// <summary>
    /// 软卸载 DIY 包至 <c>.trash</c> 回收站目录，返回回收站条目名，支持 <see cref="RestoreFromTrash"/> 恢复。
    /// </summary>
    public string MoveToTrash(string packageId)
    {
        EnsureValidPackageId(packageId);
        string pkgDir = GetPackageDirectory(packageId);
        if (!Directory.Exists(pkgDir))
            throw new DirectoryNotFoundException($"DIY package '{packageId}' does not exist.");

        Directory.CreateDirectory(TrashDirectory);
        string trashEntryName = $"{packageId}_{DateTime.UtcNow:yyyyMMddHHmmss}_{ Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant()}";
        string dest = Path.Combine(TrashDirectory, trashEntryName);
        Directory.Move(pkgDir, dest);
        return trashEntryName;
    }

    public string RestoreFromTrash(string trashEntryName, string? targetPackageId = null)
    {
        if (string.IsNullOrWhiteSpace(trashEntryName) || trashEntryName.Contains("..", StringComparison.Ordinal))
            throw new ArgumentException("Invalid trash entry name.", nameof(trashEntryName));

        string trashPath = EnsureSafeDirectoryChild(TrashDirectory, trashEntryName);
        if (!Directory.Exists(trashPath))
            throw new DirectoryNotFoundException($"Trash entry '{trashEntryName}' does not exist.");

        string inferredId = targetPackageId ?? InferPackageIdFromTrashEntry(trashEntryName);
        EnsureValidPackageId(inferredId);

        string destPkgDir = GetPackageDirectory(inferredId);
        if (Directory.Exists(destPkgDir))
            throw new IOException($"Target package '{inferredId}' already exists.");

        Directory.CreateDirectory(PackagesDirectory);
        Directory.Move(trashPath, destPkgDir);
        return inferredId;
    }

    public string GetPackageDirectory(string packageId)
    {
        EnsureValidPackageId(packageId);
        return EnsureSafeDirectoryChild(PackagesDirectory, packageId);
    }

    private DiyPackageManifest ReadOrCreateDefaultManifest(string id, string pkgDir)
    {
        string manifestPath = Path.Combine(pkgDir, "manifest.json");
        if (File.Exists(manifestPath))
        {
            try
            {
                DiyPackageManifest? parsed = JsonSerializer.Deserialize<DiyPackageManifest>(File.ReadAllBytes(manifestPath), JsonOptions);
                if (parsed is not null)
                    return parsed with { Id = id };
            }
            catch
            {
                // 回退默认 manifest
            }
        }

        return new DiyPackageManifest
        {
            Id = id,
            Name = id,
            Enabled = true,
            Priority = 100
        };
    }

    private void TouchManifestIfExists(string packageId)
    {
        string pkgDir = GetPackageDirectory(packageId);
        string manifestPath = Path.Combine(pkgDir, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            UpsertManifest(new DiyPackageManifest { Id = packageId, Name = packageId });
            return;
        }

        DiyPackageManifest current = ReadOrCreateDefaultManifest(packageId, pkgDir);
        UpsertManifest(current);
    }

    private static string InferPackageIdFromTrashEntry(string trashEntryName)
    {
        int secondLastUnderscore = -1;
        int lastUnderscore = trashEntryName.LastIndexOf('_');
        if (lastUnderscore > 0)
            secondLastUnderscore = trashEntryName.LastIndexOf('_', lastUnderscore - 1);

        return secondLastUnderscore > 0 ? trashEntryName[..secondLastUnderscore] : trashEntryName;
    }

    private static void EnsureValidPackageId(string packageId)
    {
        if (!IsValidPackageId(packageId))
            throw new ArgumentException($"Invalid DIY package id '{packageId}'.", nameof(packageId));
    }

    private static string EnsureSafeDirectoryChild(string baseDirectory, string childRelative)
    {
        string fullBase = Path.GetFullPath(baseDirectory);
        if (!fullBase.EndsWith(Path.DirectorySeparatorChar))
            fullBase += Path.DirectorySeparatorChar;

        string candidate = Path.GetFullPath(Path.Combine(fullBase, childRelative));
        if (!candidate.StartsWith(fullBase, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Path traversal detected.");

        if (HasReparsePointInChain(fullBase, candidate))
            throw new InvalidOperationException("Symbolic link or reparse point traversal is forbidden.");

        return candidate;
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
                return false;
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool HasReparsePointInChain(string baseDirectory, string targetPath)
    {
        string root = baseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (IsReparsePoint(root))
            return true;

        string? current = targetPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        while (!string.IsNullOrEmpty(current) &&
               current.Length > root.Length &&
               current.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            if (IsReparsePoint(current))
                return true;
            current = Path.GetDirectoryName(current);
        }

        return false;
    }

    private static string EnsureSafeChildPath(string baseDirectory, string relativePath, string requiredExtension)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("File name cannot be empty.", nameof(relativePath));

        string candidate = EnsureSafeDirectoryChild(baseDirectory, relativePath);
        string ext = Path.GetExtension(candidate);
        if (!ext.Equals(requiredExtension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Expected '{requiredExtension}' file extension.", nameof(relativePath));

        return candidate;
    }

    private static void WriteFileAtomically(string destinationPath, byte[] content)
    {
        string tempPath = destinationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(tempPath, content);
            File.Move(tempPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
    }
}
