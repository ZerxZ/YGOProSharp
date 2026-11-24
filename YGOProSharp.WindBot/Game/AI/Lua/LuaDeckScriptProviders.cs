using System;
using System.Collections.Generic;
using System.IO;
using YGOProSharp.Abstractions.Scripting;

namespace WindBot.Game.AI.Lua
{
    /// <summary>
    /// 基于内存字典的 Lua 卡组脚本提供者，适用于单元测试、热重载、嵌入式资源或 Unity TextAsset 桥接。
    /// </summary>
    public sealed class InMemoryLuaDeckScriptProvider : ILuaScriptSourceProvider
    {
        private readonly Dictionary<string, string> _scripts;

        public InMemoryLuaDeckScriptProvider()
        {
            _scripts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public InMemoryLuaDeckScriptProvider(IEnumerable<KeyValuePair<string, string>> scripts)
            : this()
        {
            if (scripts == null)
                return;

            foreach (KeyValuePair<string, string> pair in scripts)
                Add(pair.Key, pair.Value);
        }

        public InMemoryLuaDeckScriptProvider Add(string scriptName, string source)
        {
            if (string.IsNullOrWhiteSpace(scriptName))
                throw new ArgumentException("Script name cannot be null or whitespace.", nameof(scriptName));
            ArgumentNullException.ThrowIfNull(source);

            string normalized = NormalizeKey(scriptName);
            _scripts[normalized] = source;
            return this;
        }

        public bool Remove(string scriptName)
        {
            if (string.IsNullOrWhiteSpace(scriptName))
                return false;

            return _scripts.Remove(NormalizeKey(scriptName));
        }

        public bool TryGetScriptSource(string scriptName, out string source, out string chunkName)
        {
            source = null;
            chunkName = null;
            if (string.IsNullOrWhiteSpace(scriptName))
                return false;

            string key = NormalizeKey(scriptName);
            if (!_scripts.TryGetValue(key, out string found))
            {
                string fileName = Path.GetFileNameWithoutExtension(scriptName.Trim());
                if (string.IsNullOrWhiteSpace(fileName) || !_scripts.TryGetValue(fileName, out found))
                    return false;

                key = fileName;
            }

            source = found;
            chunkName = key.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) ? key : key + ".lua";
            return true;
        }

        private static string NormalizeKey(string scriptName)
        {
            string trimmed = scriptName.Trim().Replace('\\', '/');
            if (trimmed.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed[..^4];
            return trimmed;
        }
    }

    /// <summary>
    /// 基于文件系统的 Lua 卡组脚本提供者。不依赖任何第三方库，按候选目录查找 <c>.lua</c> 脚本。
    /// </summary>
    public sealed class FileSystemLuaDeckScriptProvider : ILuaScriptSourceProvider
    {
        private readonly string[] _searchDirectories;

        public FileSystemLuaDeckScriptProvider(params string[] searchDirectories)
        {
            if (searchDirectories != null && searchDirectories.Length > 0)
            {
                List<string> normalized = new List<string>(searchDirectories.Length);
                foreach (string directory in searchDirectories)
                {
                    if (!string.IsNullOrWhiteSpace(directory))
                        normalized.Add(directory.Trim());
                }

                _searchDirectories = normalized.Count > 0 ? normalized.ToArray() : CreateDefaultSearchDirectories();
            }
            else
            {
                _searchDirectories = CreateDefaultSearchDirectories();
            }
        }

        public IReadOnlyList<string> SearchDirectories => _searchDirectories;

        public bool TryGetScriptSource(string scriptName, out string source, out string chunkName)
        {
            source = null;
            chunkName = null;

            if (string.IsNullOrWhiteSpace(scriptName))
                return false;

            string path = ResolvePath(scriptName.Trim());
            if (path == null)
                return false;

            source = File.ReadAllText(path);
            chunkName = Path.GetFileName(path);
            return true;
        }

        /// <summary>
        /// 扫描搜索目录下的所有 <c>.lua</c> 文件，生成对应的 <see cref="LuaDeckDescriptor"/> 列表。
        /// </summary>
        public IReadOnlyList<LuaDeckDescriptor> DiscoverDecks(
            string level = "Normal",
            bool allowOverride = false,
            SearchOption searchOption = SearchOption.TopDirectoryOnly)
        {
            List<LuaDeckDescriptor> result = new List<LuaDeckDescriptor>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string directory in _searchDirectories)
            {
                if (!Directory.Exists(directory))
                    continue;

                foreach (string filePath in Directory.EnumerateFiles(directory, "*.lua", searchOption))
                {
                    string deckName = Path.GetFileNameWithoutExtension(filePath);
                    if (string.IsNullOrWhiteSpace(deckName) || !seen.Add(deckName))
                        continue;

                    result.Add(new LuaDeckDescriptor(
                        name: deckName,
                        file: deckName,
                        scriptName: filePath,
                        level: level,
                        allowOverride: allowOverride));
                }
            }

            return result;
        }

        private string ResolvePath(string scriptName)
        {
            string withExtension = scriptName.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)
                ? scriptName
                : scriptName + ".lua";

            if (Path.IsPathRooted(scriptName))
            {
                if (File.Exists(scriptName))
                    return scriptName;
                if (File.Exists(withExtension))
                    return withExtension;
                return null;
            }

            if (File.Exists(scriptName))
                return Path.GetFullPath(scriptName);
            if (File.Exists(withExtension))
                return Path.GetFullPath(withExtension);

            foreach (string directory in _searchDirectories)
            {
                string direct = Path.Combine(directory, scriptName);
                if (File.Exists(direct))
                    return Path.GetFullPath(direct);

                string extended = Path.Combine(directory, withExtension);
                if (File.Exists(extended))
                    return Path.GetFullPath(extended);
            }

            return null;
        }

        private static string[] CreateDefaultSearchDirectories()
        {
            string baseDir = AppContext.BaseDirectory;
            return
            [
                "LuaDecks",
                Path.Combine("Decks", "Lua"),
                Path.Combine("Data", "WindBot", "LuaDecks"),
                Path.Combine("Scripts", "WindBot"),
                Path.Combine(baseDir, "LuaDecks"),
                Path.Combine(baseDir, "Decks", "Lua"),
                Path.Combine(baseDir, "Data", "WindBot", "LuaDecks"),
                Path.Combine(baseDir, "Scripts", "WindBot")
            ];
        }
    }

    /// <summary>
    /// 组合多个 <see cref="ILuaScriptSourceProvider"/>，按顺序优先查找（常用于内存热更脚本覆盖磁盘脚本）。
    /// </summary>
    public sealed class CompositeLuaDeckScriptProvider : ILuaScriptSourceProvider
    {
        private readonly ILuaScriptSourceProvider[] _providers;

        public CompositeLuaDeckScriptProvider(params ILuaScriptSourceProvider[] providers)
        {
            List<ILuaScriptSourceProvider> list = new List<ILuaScriptSourceProvider>();
            if (providers != null)
            {
                foreach (ILuaScriptSourceProvider provider in providers)
                {
                    if (provider != null)
                        list.Add(provider);
                }
            }

            _providers = list.ToArray();
        }

        public bool TryGetScriptSource(string scriptName, out string source, out string chunkName)
        {
            foreach (ILuaScriptSourceProvider provider in _providers)
            {
                if (provider.TryGetScriptSource(scriptName, out source, out chunkName))
                    return true;
            }

            source = null;
            chunkName = null;
            return false;
        }
    }
}
