using System;
using System.Collections.Generic;
using System.IO;
using YGOProSharp.Abstractions.Scripting;

namespace WindBot.Game.AI.Lua
{
    /// <summary>
    /// 基于内存字典的 Lua 对话脚本提供者，适用于单元测试、热重载、嵌入式资源或 Unity TextAsset 桥接。
    /// </summary>
    public sealed class InMemoryLuaDialogScriptProvider : ILuaScriptSourceProvider
    {
        private readonly Dictionary<string, string> _scripts;

        public InMemoryLuaDialogScriptProvider()
        {
            _scripts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public InMemoryLuaDialogScriptProvider(IEnumerable<KeyValuePair<string, string>> scripts)
            : this()
        {
            if (scripts == null)
                return;

            foreach (KeyValuePair<string, string> pair in scripts)
                Add(pair.Key, pair.Value);
        }

        public InMemoryLuaDialogScriptProvider Add(string scriptName, string source)
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
    /// 基于文件系统的 Lua 对话脚本提供者。按候选目录查找 <c>.lua</c> 对话脚本。
    /// </summary>
    public sealed class FileSystemLuaDialogScriptProvider : ILuaScriptSourceProvider
    {
        private readonly string[] _searchDirectories;

        public FileSystemLuaDialogScriptProvider(params string[] searchDirectories)
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
        /// 扫描搜索目录下的所有 <c>.lua</c> 文件，生成对应的 <see cref="LuaDialogDescriptor"/> 列表。
        /// </summary>
        public IReadOnlyList<LuaDialogDescriptor> DiscoverDialogs(
            bool allowOverride = false,
            SearchOption searchOption = SearchOption.TopDirectoryOnly)
        {
            List<LuaDialogDescriptor> result = new List<LuaDialogDescriptor>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string directory in _searchDirectories)
            {
                if (!Directory.Exists(directory))
                    continue;

                foreach (string filePath in Directory.EnumerateFiles(directory, "*.lua", searchOption))
                {
                    string dialogName = Path.GetFileNameWithoutExtension(filePath);
                    if (string.IsNullOrWhiteSpace(dialogName) || !seen.Add(dialogName))
                        continue;

                    result.Add(new LuaDialogDescriptor(
                        name: dialogName,
                        scriptName: filePath,
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

                // 支持按角色分子目录存放多语言文件：例如 "ExampleBlueEyes.zh-CN" -> "<dir>/ExampleBlueEyes/zh-CN.lua"
                string baseWithoutExt = scriptName.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)
                    ? scriptName[..^4]
                    : scriptName;
                int dotIndex = baseWithoutExt.IndexOf('.');
                if (dotIndex > 0 && dotIndex + 1 < baseWithoutExt.Length)
                {
                    string folder = baseWithoutExt[..dotIndex];
                    string leaf = baseWithoutExt[(dotIndex + 1)..] + ".lua";
                    string nested = Path.Combine(directory, folder, leaf);
                    if (File.Exists(nested))
                        return Path.GetFullPath(nested);
                }
                else if (!baseWithoutExt.Contains('/') && !baseWithoutExt.Contains('\\'))
                {
                    // 基础名同时兼容子目录入口 "<dir>/<name>/default.lua" 或 "<dir>/<name>/index.lua"
                    string nestedDefault = Path.Combine(directory, baseWithoutExt, "default.lua");
                    if (File.Exists(nestedDefault))
                        return Path.GetFullPath(nestedDefault);

                    string nestedIndex = Path.Combine(directory, baseWithoutExt, "index.lua");
                    if (File.Exists(nestedIndex))
                        return Path.GetFullPath(nestedIndex);
                }
            }

            return null;
        }

        private static string[] CreateDefaultSearchDirectories()
        {
            string baseDir = AppContext.BaseDirectory;
            return
            [
                Path.Combine("Dialogs", "Lua"),
                Path.Combine("Dialog", "Lua"),
                "Dialogs",
                Path.Combine("Data", "WindBot", "Dialogs", "Lua"),
                Path.Combine("Data", "WindBot", "Dialogs"),
                "LuaDialogs",
                Path.Combine("Data", "WindBot", "LuaDialogs"),
                Path.Combine(baseDir, "Dialogs", "Lua"),
                Path.Combine(baseDir, "Dialog", "Lua"),
                Path.Combine(baseDir, "Dialogs"),
                Path.Combine(baseDir, "Data", "WindBot", "Dialogs", "Lua"),
                Path.Combine(baseDir, "Data", "WindBot", "Dialogs"),
                Path.Combine(baseDir, "LuaDialogs"),
                Path.Combine(baseDir, "Data", "WindBot", "LuaDialogs")
            ];
        }
    }

    /// <summary>
    /// 组合多个 <see cref="ILuaScriptSourceProvider"/>，按顺序优先查找对话脚本。
    /// </summary>
    public sealed class CompositeLuaDialogScriptProvider : ILuaScriptSourceProvider
    {
        private readonly ILuaScriptSourceProvider[] _providers;

        public CompositeLuaDialogScriptProvider(params ILuaScriptSourceProvider[] providers)
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
