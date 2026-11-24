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
        ".cdb", ".lua", ".ypk", ".zip", ".conf", ".ydk", ".jpg", ".jpeg", ".png", ".json"
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
    /// 对 Lua 脚本源码执行 UTF-8 编码检查与危险系统库/反射调用静态拦截（自动跳过单行注释行以减少误报）。
    /// </summary>
    public static LuaScriptSecurityAudit AuditLuaScript(ReadOnlySpan<byte> utf8Bytes)
    {
        string source;
        try
        {
            UTF8Encoding strictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            source = strictUtf8.GetString(utf8Bytes);
        }
        catch (DecoderFallbackException)
        {
            return new LuaScriptSecurityAudit(false, ["Invalid UTF-8 byte sequence."]);
        }

        return AuditLuaScript(source);
    }

    public static LuaScriptSecurityAudit AuditLuaScript(string luaSource)
    {
        ArgumentNullException.ThrowIfNull(luaSource);

        List<string> violations = new();
        string[] lines = luaSource.Split('\n');
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
    /// 校验 <c>.ypk</c> / <c>.zip</c> 压缩包内所有条目路径与扩展名，防御路径穿越（ZIP Slip）与非法后缀。
    /// </summary>
    public static IReadOnlyList<string> AuditArchiveEntries(Stream archiveStream, long maxUncompressedBytes = 64L * 1024 * 1024)
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
            if (!AllowedExtensions.Contains(ext))
                errors.Add($"Disallowed file extension '{ext}' in archive entry '{entry.FullName}'.");

            totalBytes += Math.Max(0, entry.Length);
            if (totalBytes > maxUncompressedBytes)
            {
                errors.Add($"Archive uncompressed size exceeds limit ({maxUncompressedBytes} bytes).");
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

    private static void AddFileToZip(ZipArchive zip, string sourceFilePath, string entryName)
    {
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

        return candidate;
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
