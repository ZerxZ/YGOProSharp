using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using YGOProSharp.Abstractions.Ocg;

namespace YGOProSharp.Core;

/// <summary>
/// 内存脚本 provider，支持运行时动态注册或覆盖 DIY 卡脚本与全局规则脚本（如 <c>PreloadScript</c>）。
/// </summary>
public sealed class InMemoryScriptProvider : IScriptProvider
{
    private readonly ConcurrentDictionary<string, byte[]> _scripts = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryScriptProvider()
    {
    }

    public InMemoryScriptProvider(IEnumerable<KeyValuePair<string, byte[]>> scripts)
    {
        ArgumentNullException.ThrowIfNull(scripts);
        foreach (KeyValuePair<string, byte[]> entry in scripts)
            Register(entry.Key, entry.Value);
    }

    public void Register(string scriptName, byte[] scriptBytes)
    {
        ArgumentNullException.ThrowIfNull(scriptBytes);
        string key = FileScriptProvider.NormalizeScriptRelativePath(scriptName)
            ?? throw new ArgumentException("Invalid script name.", nameof(scriptName));
        _scripts[key] = scriptBytes;
    }

    public void RegisterText(string scriptName, string luaSource)
    {
        ArgumentNullException.ThrowIfNull(luaSource);
        Register(scriptName, Encoding.UTF8.GetBytes(luaSource));
    }

    public bool Remove(string scriptName)
    {
        string? key = FileScriptProvider.NormalizeScriptRelativePath(scriptName);
        return key is not null && _scripts.TryRemove(key, out _);
    }

    public bool TryGetScript(string scriptName, out byte[] script)
    {
        string? key = FileScriptProvider.NormalizeScriptRelativePath(scriptName);
        if (key is not null && _scripts.TryGetValue(key, out byte[]? found))
        {
            script = found;
            return true;
        }

        script = [];
        return false;
    }
}

/// <summary>
/// 基于 <c>.ypk</c> / <c>.zip</c> 压缩包的脚本 provider（复用 BCL <see cref="ZipArchive"/>，不引入第三方解压依赖）。
/// </summary>
public sealed class ZipArchiveScriptProvider : IScriptProvider, IDisposable
{
    private readonly object _gate = new();
    private readonly ZipArchive _archive;
    private readonly Dictionary<string, ZipArchiveEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public string ArchivePath { get; }

    public ZipArchiveScriptProvider(string archivePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArchivePath = Path.GetFullPath(archivePath);
        _archive = ZipFile.OpenRead(ArchivePath);

        foreach (ZipArchiveEntry entry in _archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            string? normalized = FileScriptProvider.NormalizeScriptRelativePath(entry.FullName);
            if (normalized is not null)
                _entries[normalized] = entry;
        }
    }

    public bool TryGetScript(string scriptName, out byte[] script)
    {
        string? key = FileScriptProvider.NormalizeScriptRelativePath(scriptName);
        if (key is null)
        {
            script = [];
            return false;
        }

        lock (_gate)
        {
            if (_disposed || !_entries.TryGetValue(key, out ZipArchiveEntry? entry))
            {
                script = [];
                return false;
            }

            using Stream stream = entry.Open();
            using MemoryStream ms = new();
            stream.CopyTo(ms);
            script = ms.ToArray();
            return true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _entries.Clear();
            _archive.Dispose();
        }
    }
}

/// <summary>
/// 按优先级串联多个 <see cref="IScriptProvider"/>（越靠前优先级越高），用于实现
/// 内存覆写 → DIY 目录 → expansions（目录与 <c>.ypk</c>/<c>.zip</c>）→ 基础 <c>script/</c> 的分层加载。
/// </summary>
public sealed class CompositeScriptProvider : IScriptProvider, IDisposable
{
    private readonly IScriptProvider[] _providers;
    private readonly bool _disposeChildren;
    private bool _disposed;

    public CompositeScriptProvider(IEnumerable<IScriptProvider> providers, bool disposeChildren = true)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers.Where(static p => p is not null).ToArray();
        _disposeChildren = disposeChildren;
    }

    public CompositeScriptProvider(params IScriptProvider[] providers)
        : this((IEnumerable<IScriptProvider>)providers, disposeChildren: true)
    {
    }

    public IReadOnlyList<IScriptProvider> Providers => _providers;

    public bool TryGetScript(string scriptName, out byte[] script)
    {
        if (_disposed)
        {
            script = [];
            return false;
        }

        for (int i = 0; i < _providers.Length; i++)
        {
            if (_providers[i].TryGetScript(scriptName, out script))
                return true;
        }

        script = [];
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (!_disposeChildren)
            return;

        for (int i = 0; i < _providers.Length; i++)
        {
            if (_providers[i] is IDisposable disposable)
                disposable.Dispose();
        }
    }
}
