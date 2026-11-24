using System;
using System.Collections.Generic;
using System.IO;
using WindBot.Game.AI.Lua;
using YGOProSharp.Abstractions.Scripting;

namespace WindBot.Game.AI
{
    /// <summary>
    /// WindBot Lua 对话管理器（与 <see cref="DecksManager"/> 对齐）。
    /// 负责装配泛用 <see cref="ILuaRuntime"/>、对话脚本源码提供者与静态注册的 <see cref="LuaDialogDescriptor"/>。
    /// </summary>
    public static class DialogsManager
    {
        private static readonly object _syncRoot = new();
        private static readonly Dictionary<string, LuaDialogDescriptor> _luaDialogs =
            new(StringComparer.OrdinalIgnoreCase);

        private static ILuaRuntime _luaRuntime;
        private static ILuaScriptSourceProvider _luaScriptProvider;
        private static bool _preferLuaDialogs;
        private static bool _throwOnLuaScriptError;

        /// <summary>
        /// 配置泛用 Lua 运行时与对话脚本提供者，使 <see cref="Dialogs"/> 能够加载 xmake 风格或模块式的 Lua 对话脚本。
        /// </summary>
        public static void ConfigureLua(
            ILuaRuntime runtime,
            ILuaScriptSourceProvider scriptProvider = null,
            bool preferLuaDialogs = false,
            bool throwOnScriptError = false)
        {
            lock (_syncRoot)
            {
                _luaRuntime = runtime;
                _luaScriptProvider = scriptProvider ?? new FileSystemLuaDialogScriptProvider();
                _preferLuaDialogs = preferLuaDialogs;
                _throwOnLuaScriptError = throwOnScriptError;
            }
        }

        /// <summary>
        /// 注册一个 Lua 对话描述符。
        /// </summary>
        public static void RegisterLuaDialog(LuaDialogDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(descriptor);

            lock (_syncRoot)
            {
                _luaDialogs[descriptor.Name] = descriptor;
            }
        }

        /// <summary>
        /// 批量注册 Lua 对话描述符。
        /// </summary>
        public static void RegisterLuaDialogs(IEnumerable<LuaDialogDescriptor> descriptors)
        {
            if (descriptors == null)
                return;

            lock (_syncRoot)
            {
                foreach (LuaDialogDescriptor descriptor in descriptors)
                {
                    if (descriptor != null)
                        _luaDialogs[descriptor.Name] = descriptor;
                }
            }
        }

        /// <summary>
        /// 注销指定名称的 Lua 对话描述符。
        /// </summary>
        public static bool UnregisterLuaDialog(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            lock (_syncRoot)
            {
                return _luaDialogs.Remove(name.Trim());
            }
        }

        /// <summary>
        /// 清空所有已注册的 Lua 对话描述符（保留当前配置的 Lua 运行时）。
        /// </summary>
        public static void ClearLuaDialogs()
        {
            lock (_syncRoot)
            {
                _luaDialogs.Clear();
            }
        }

        /// <summary>
        /// 重置 Lua 对话配置与注册表（常用于单元测试隔离或运行时重新装配）。
        /// </summary>
        public static void ResetLua()
        {
            lock (_syncRoot)
            {
                _luaRuntime = null;
                _luaScriptProvider = null;
                _preferLuaDialogs = false;
                _throwOnLuaScriptError = false;
                _luaDialogs.Clear();
            }
        }

        /// <summary>
        /// 返回当前已注册的所有 Lua 对话描述符快照。
        /// </summary>
        public static IReadOnlyList<LuaDialogDescriptor> GetRegisteredLuaDialogs()
        {
            List<LuaDialogDescriptor> items = new();
            lock (_syncRoot)
            {
                items.AddRange(_luaDialogs.Values);
            }

            items.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return items;
        }

        /// <summary>
        /// 返回当前是否已配置泛用 Lua 运行时。
        /// </summary>
        public static bool HasLuaRuntime
        {
            get
            {
                lock (_syncRoot)
                    return _luaRuntime != null;
            }
        }

        internal static bool TryLoadLuaDialog(
            Dialogs dialogs,
            GameClient game,
            Duel duel,
            string dialogName,
            string locale,
            bool jsonExists,
            out LuaDialogScript script,
            out LuaDialogDescriptor descriptor)
        {
            script = null;
            descriptor = null;

            if (string.IsNullOrWhiteSpace(dialogName))
                return false;

            string trimmedName = dialogName.Trim();
            bool explicitLuaExtension = trimmedName.EndsWith(".lua", StringComparison.OrdinalIgnoreCase);
            IReadOnlyList<string> candidates = LuaDialogDsl.ExpandLocalizedDialogCandidates(trimmedName, locale);

            ILuaRuntime runtime;
            ILuaScriptSourceProvider provider;
            bool preferLua;
            bool throwOnError;
            LuaDialogDescriptor registered = null;

            lock (_syncRoot)
            {
                runtime = _luaRuntime;
                provider = _luaScriptProvider;
                preferLua = _preferLuaDialogs;
                throwOnError = _throwOnLuaScriptError;

                for (int i = 0; i < candidates.Count; i++)
                {
                    if (_luaDialogs.TryGetValue(candidates[i], out registered))
                        break;
                }

                if (registered == null && explicitLuaExtension)
                    _luaDialogs.TryGetValue(Path.GetFileNameWithoutExtension(trimmedName), out registered);
            }

            descriptor = registered;

            bool shouldUseRegisteredLua = registered != null &&
                (explicitLuaExtension || registered.AllowOverride || preferLua || !jsonExists);

            if (shouldUseRegisteredLua)
            {
                if (runtime == null)
                {
                    throw new InvalidOperationException(
                        $"Lua dialog '{registered.Name}' is registered, but no ILuaRuntime has been configured in DialogsManager or WindBotRuntimeOptions.");
                }

                ILuaScriptSourceProvider resolvedProvider = provider ?? new FileSystemLuaDialogScriptProvider();
                if (!TryResolveLocalizedScript(resolvedProvider, registered.ScriptName, locale, out string source, out string chunkName))
                {
                    throw new FileNotFoundException(
                        $"Could not find Lua dialog script '{registered.ScriptName}' for dialog '{registered.Name}'.",
                        registered.ScriptName);
                }

                script = new LuaDialogScript(
                    dialogs,
                    game,
                    duel,
                    runtime,
                    source,
                    registered.Name,
                    locale ?? registered.Locale,
                    chunkName ?? registered.ScriptName,
                    throwOnError,
                    resolvedProvider);
                return true;
            }

            bool shouldTryDynamicLua = runtime != null &&
                provider != null &&
                (explicitLuaExtension || preferLua || !jsonExists);

            if (shouldTryDynamicLua &&
                TryResolveLocalizedScript(provider, trimmedName, locale, out string dynamicSource, out string dynamicChunk))
            {
                script = new LuaDialogScript(
                    dialogs,
                    game,
                    duel,
                    runtime,
                    dynamicSource,
                    trimmedName,
                    locale,
                    dynamicChunk ?? trimmedName,
                    throwOnError,
                    provider);
                return true;
            }

            return false;
        }

        private static bool TryResolveLocalizedScript(
            ILuaScriptSourceProvider provider,
            string scriptName,
            string locale,
            out string source,
            out string chunkName)
        {
            IReadOnlyList<string> candidates = LuaDialogDsl.ExpandLocalizedDialogCandidates(scriptName, locale);
            bool explicitLua = scriptName.EndsWith(".lua", StringComparison.OrdinalIgnoreCase);

            for (int i = 0; i < candidates.Count; i++)
            {
                string candidate = explicitLua ? candidates[i] + ".lua" : candidates[i];
                if (provider.TryGetScriptSource(candidate, out source, out chunkName))
                    return true;
            }

            return provider.TryGetScriptSource(scriptName, out source, out chunkName);
        }
    }
}
