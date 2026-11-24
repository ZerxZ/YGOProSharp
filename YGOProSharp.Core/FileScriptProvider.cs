using YGOProSharp.Abstractions.Ocg;

namespace YGOProSharp.Core;

/// <summary>
/// 基于本地目录的 Lua 卡脚本 provider，支持按优先级叠加多个目录（例如 DIY / expansions / script）。
/// </summary>
public sealed class FileScriptProvider : IScriptProvider
{
    private readonly string[] _scriptDirectories;

    public FileScriptProvider(string rootPath, string scriptDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptDirectory);

        string resolved = Path.IsPathRooted(scriptDirectory)
            ? Path.GetFullPath(scriptDirectory)
            : Path.GetFullPath(Path.Combine(rootPath, scriptDirectory));
        _scriptDirectories = [resolved];
    }

    public FileScriptProvider(IEnumerable<string> scriptDirectories)
    {
        ArgumentNullException.ThrowIfNull(scriptDirectories);
        _scriptDirectories = scriptDirectories
            .Where(static dir => !string.IsNullOrWhiteSpace(dir))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<string> ScriptDirectories => _scriptDirectories;

    public bool TryGetScript(string scriptName, out byte[] script)
    {
        string? relativePath = NormalizeScriptRelativePath(scriptName);
        if (relativePath is null)
        {
            script = [];
            return false;
        }

        for (int i = 0; i < _scriptDirectories.Length; i++)
        {
            string baseDir = _scriptDirectories[i];
            string candidate = Path.GetFullPath(Path.Combine(baseDir, relativePath));
            if (!IsWithinDirectory(candidate, baseDir))
                continue;

            if (File.Exists(candidate))
            {
                script = File.ReadAllBytes(candidate);
                return true;
            }
        }

        script = [];
        return false;
    }

    internal static string? NormalizeScriptRelativePath(string? scriptName)
    {
        if (string.IsNullOrWhiteSpace(scriptName))
            return null;

        string normalized = scriptName.Trim().Replace('\\', '/');
        while (normalized.StartsWith("./", StringComparison.Ordinal))
            normalized = normalized[2..];

        if (normalized.StartsWith("script/", StringComparison.OrdinalIgnoreCase))
            normalized = normalized["script/".Length..];

        normalized = normalized.TrimStart('/');
        if (normalized.Length == 0 || normalized.Contains("..", StringComparison.Ordinal))
            return null;

        return normalized;
    }

    private static bool IsWithinDirectory(string candidateFullPath, string baseDirectoryFullPath)
    {
        string baseWithSeparator = baseDirectoryFullPath.EndsWith(Path.DirectorySeparatorChar)
            ? baseDirectoryFullPath
            : baseDirectoryFullPath + Path.DirectorySeparatorChar;
        return candidateFullPath.StartsWith(baseWithSeparator, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(candidateFullPath, baseDirectoryFullPath, StringComparison.OrdinalIgnoreCase);
    }
}
