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
