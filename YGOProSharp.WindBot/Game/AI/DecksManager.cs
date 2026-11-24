using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using WindBot.Game.AI.Lua;
using YGOProSharp.Abstractions.Scripting;

namespace WindBot.Game.AI
{
    public static class DecksManager
    {
        private class DeckInstance
        {
            public string Deck { get; private set; }
            public Type Type { get; private set; }
            public string Level { get; private set; }

            public DeckInstance(string deck, Type type, string level)
            {
                Deck = deck;
                Type = type;
                Level = level;
            }
        }

        private static readonly object _syncRoot = new();
        private static Dictionary<string, DeckInstance> _decks;
        private static Dictionary<string, DeckInstance> _decksByFile;
        private static List<DeckInstance> _list;
        private static bool _initialized;

        private static readonly Dictionary<string, LuaDeckDescriptor> _luaDecks =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, LuaDeckDescriptor> _luaDecksByFile =
            new(StringComparer.OrdinalIgnoreCase);
        private static ILuaRuntime _luaRuntime;
        private static ILuaScriptSourceProvider _luaScriptProvider;
        private static bool _preferLuaDecks;
        private static bool _throwOnLuaScriptError;

        public static void Init()
        {
            lock (_syncRoot)
            {
                if (_initialized)
                    return;

                _decks = new Dictionary<string, DeckInstance>(StringComparer.OrdinalIgnoreCase);
                _decksByFile = new Dictionary<string, DeckInstance>(StringComparer.OrdinalIgnoreCase);

                Assembly asm = Assembly.GetExecutingAssembly();
                Type[] types = asm.GetTypes();

                foreach (Type type in types)
                {
                    MemberInfo info = type;
                    object[] attributes = info.GetCustomAttributes(false);
                    foreach (object attribute in attributes)
                    {
                        if (attribute is DeckAttribute deck)
                        {
                            DeckInstance instance = new DeckInstance(deck.File, type, deck.Level);
                            _decks.TryAdd(deck.Name, instance);
                            if (!string.IsNullOrWhiteSpace(deck.File))
                                _decksByFile.TryAdd(deck.File, instance);
                        }
                    }
                }

                _list = new List<DeckInstance>();
                _list.AddRange(_decks.Values);
                _initialized = true;

                Logger.WriteLine("Decks initialized, " + _decks.Count + " found.");
            }
        }

        /// <summary>
        /// 配置泛用 Lua 运行时与脚本源码提供者，使 WindBot 能实例化 Lua 卡组执行器。
        /// </summary>
        public static void ConfigureLua(
            ILuaRuntime runtime,
            ILuaScriptSourceProvider scriptProvider = null,
            bool preferLuaDecks = false,
            bool throwOnScriptError = false)
        {
            lock (_syncRoot)
            {
                _luaRuntime = runtime;
                _luaScriptProvider = scriptProvider ?? new FileSystemLuaDeckScriptProvider();
                _preferLuaDecks = preferLuaDecks;
                _throwOnLuaScriptError = throwOnScriptError;
            }
        }

        /// <summary>
        /// 注册一个 Lua 卡组执行器描述符。
        /// </summary>
        public static void RegisterLuaDeck(LuaDeckDescriptor descriptor)
        {
            ArgumentNullException.ThrowIfNull(descriptor);

            lock (_syncRoot)
            {
                _luaDecks[descriptor.Name] = descriptor;
                if (!string.IsNullOrWhiteSpace(descriptor.File))
                    _luaDecksByFile[descriptor.File] = descriptor;
            }
        }

        /// <summary>
        /// 批量注册 Lua 卡组执行器描述符。
        /// </summary>
        public static void RegisterLuaDecks(IEnumerable<LuaDeckDescriptor> descriptors)
        {
            if (descriptors == null)
                return;

            lock (_syncRoot)
            {
                foreach (LuaDeckDescriptor descriptor in descriptors)
                {
                    if (descriptor == null)
                        continue;

                    _luaDecks[descriptor.Name] = descriptor;
                    if (!string.IsNullOrWhiteSpace(descriptor.File))
                        _luaDecksByFile[descriptor.File] = descriptor;
                }
            }
        }

        /// <summary>
        /// 注销指定名称的 Lua 卡组执行器描述符。
        /// </summary>
        public static bool UnregisterLuaDeck(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            lock (_syncRoot)
            {
                if (!_luaDecks.Remove(name.Trim(), out LuaDeckDescriptor removed))
                    return false;

                if (!string.IsNullOrWhiteSpace(removed.File) &&
                    _luaDecksByFile.TryGetValue(removed.File, out LuaDeckDescriptor mapped) &&
                    ReferenceEquals(mapped, removed))
                {
                    _luaDecksByFile.Remove(removed.File);
                }

                return true;
            }
        }

        /// <summary>
        /// 清空所有已注册的 Lua 卡组描述符（保留当前配置的 Lua 运行时）。
        /// </summary>
        public static void ClearLuaDecks()
        {
            lock (_syncRoot)
            {
                _luaDecks.Clear();
                _luaDecksByFile.Clear();
            }
        }

        /// <summary>
        /// 重置 Lua 运行时配置与全部已注册 Lua 卡组（主要用于测试隔离或运行时重新装配）。
        /// </summary>
        public static void ResetLua()
        {
            lock (_syncRoot)
            {
                _luaRuntime = null;
                _luaScriptProvider = null;
                _preferLuaDecks = false;
                _throwOnLuaScriptError = false;
                _luaDecks.Clear();
                _luaDecksByFile.Clear();
            }
        }

        /// <summary>
        /// 已注册卡组的只读目录条目（包含内置 C# 卡组与已注册 Lua 卡组）。
        /// </summary>
        public sealed record DeckCatalogItem(
            string Name,
            string File,
            string Level,
            string Kind,
            string ScriptName,
            bool AllowOverride);

        /// <summary>
        /// 返回当前所有已注册的 C# 卡组与 Lua 卡组快照。
        /// </summary>
        public static IReadOnlyList<DeckCatalogItem> GetRegisteredDecks()
        {
            EnsureInitialized();

            List<DeckCatalogItem> items = new List<DeckCatalogItem>();
            lock (_syncRoot)
            {
                foreach (KeyValuePair<string, DeckInstance> pair in _decks)
                {
                    items.Add(new DeckCatalogItem(
                        Name: pair.Key,
                        File: pair.Value.Deck,
                        Level: pair.Value.Level,
                        Kind: "csharp",
                        ScriptName: null,
                        AllowOverride: false));
                }

                foreach (LuaDeckDescriptor lua in _luaDecks.Values)
                {
                    items.Add(new DeckCatalogItem(
                        Name: lua.Name,
                        File: lua.File,
                        Level: lua.Level,
                        Kind: "lua",
                        ScriptName: lua.ScriptName,
                        AllowOverride: lua.AllowOverride));
                }
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

        private static void EnsureInitialized()
        {
            if (!_initialized)
                Init();
        }

        public static Executor Instantiate(GameAI ai, Duel duel)
        {
            EnsureInitialized();

            string deck = ai.Game.Deck;
            DeckInstance managedDeck = null;
            LuaDeckDescriptor luaDeck = null;
            ILuaRuntime luaRuntime;
            ILuaScriptSourceProvider luaScriptProvider;
            bool preferLuaDecks;
            bool throwOnLuaScriptError;

            lock (_syncRoot)
            {
                luaRuntime = _luaRuntime;
                luaScriptProvider = _luaScriptProvider;
                preferLuaDecks = _preferLuaDecks;
                throwOnLuaScriptError = _throwOnLuaScriptError;

                if (!string.IsNullOrWhiteSpace(deck))
                {
                    if (!_decks.TryGetValue(deck, out managedDeck))
                        _decksByFile.TryGetValue(deck, out managedDeck);

                    if (!_luaDecks.TryGetValue(deck, out luaDeck))
                        _luaDecksByFile.TryGetValue(deck, out luaDeck);
                }
            }

            if (luaDeck != null)
            {
                bool shouldUseRegisteredLua = managedDeck == null || luaDeck.AllowOverride || preferLuaDecks;
                if (shouldUseRegisteredLua)
                    return InstantiateLuaDeck(ai, duel, luaDeck, luaRuntime, luaScriptProvider, throwOnLuaScriptError);
            }

            if (managedDeck != null)
            {
                if (preferLuaDecks &&
                    luaRuntime != null &&
                    luaScriptProvider != null &&
                    !string.IsNullOrWhiteSpace(deck) &&
                    luaScriptProvider.TryGetScriptSource(deck, out string overrideSource, out string overrideChunk))
                {
                    LuaDeckExecutor overrideExecutor = new LuaDeckExecutor(
                        ai,
                        duel,
                        luaRuntime,
                        overrideSource,
                        overrideChunk,
                        throwOnLuaScriptError,
                        targetDeckName: deck);
                    if (string.IsNullOrWhiteSpace(overrideExecutor.Deck))
                        overrideExecutor.Deck = managedDeck.Deck;
                    return overrideExecutor;
                }

                Executor managedExecutor = (Executor)Activator.CreateInstance(managedDeck.Type, ai, duel);
                managedExecutor.Deck = managedDeck.Deck;
                return managedExecutor;
            }

            if (!string.IsNullOrWhiteSpace(deck) &&
                luaRuntime != null &&
                luaScriptProvider != null &&
                luaScriptProvider.TryGetScriptSource(deck, out string dynamicSource, out string dynamicChunk))
            {
                LuaDeckExecutor dynamicExecutor = new LuaDeckExecutor(
                    ai,
                    duel,
                    luaRuntime,
                    dynamicSource,
                    dynamicChunk,
                    throwOnLuaScriptError,
                    targetDeckName: deck);
                if (string.IsNullOrWhiteSpace(dynamicExecutor.Deck))
                    dynamicExecutor.Deck = Path.GetFileNameWithoutExtension(deck);
                return dynamicExecutor;
            }

            return InstantiateRandomDeck(ai, duel, luaRuntime, luaScriptProvider, throwOnLuaScriptError);
        }

        private static Executor InstantiateLuaDeck(
            GameAI ai,
            Duel duel,
            LuaDeckDescriptor descriptor,
            ILuaRuntime luaRuntime,
            ILuaScriptSourceProvider luaScriptProvider,
            bool throwOnLuaScriptError)
        {
            if (luaRuntime == null)
            {
                throw new InvalidOperationException(
                    $"Lua deck '{descriptor.Name}' is registered, but no ILuaRuntime has been configured in DecksManager or WindBotRuntimeOptions.");
            }

            ILuaScriptSourceProvider provider = luaScriptProvider ?? new FileSystemLuaDeckScriptProvider();
            if (!provider.TryGetScriptSource(descriptor.ScriptName, out string source, out string chunkName))
            {
                throw new FileNotFoundException(
                    $"Could not find Lua deck script '{descriptor.ScriptName}' for deck '{descriptor.Name}'.",
                    descriptor.ScriptName);
            }

            LuaDeckExecutor executor = new LuaDeckExecutor(
                ai,
                duel,
                luaRuntime,
                source,
                chunkName ?? descriptor.ScriptName,
                throwOnLuaScriptError,
                targetDeckName: descriptor.Name);
            if (string.IsNullOrWhiteSpace(executor.Deck))
                executor.Deck = descriptor.File;
            return executor;
        }

        private static Executor InstantiateRandomDeck(
            GameAI ai,
            Duel duel,
            ILuaRuntime luaRuntime,
            ILuaScriptSourceProvider luaScriptProvider,
            bool throwOnLuaScriptError)
        {
            List<DeckInstance> normalManaged = new List<DeckInstance>();
            List<DeckInstance> allManaged = new List<DeckInstance>();
            List<LuaDeckDescriptor> normalLua = new List<LuaDeckDescriptor>();
            List<LuaDeckDescriptor> allLua = new List<LuaDeckDescriptor>();

            lock (_syncRoot)
            {
                allManaged.AddRange(_list);
                for (int i = 0; i < _list.Count; i++)
                {
                    if (_list[i].Level == "Normal")
                        normalManaged.Add(_list[i]);
                }

                if (luaRuntime != null)
                {
                    foreach (LuaDeckDescriptor descriptor in _luaDecks.Values)
                    {
                        allLua.Add(descriptor);
                        if (string.Equals(descriptor.Level, "Normal", StringComparison.OrdinalIgnoreCase))
                            normalLua.Add(descriptor);
                    }
                }
            }

            int normalTotal = normalManaged.Count + normalLua.Count;
            if (normalTotal > 0)
            {
                int index = Random.Shared.Next(normalTotal);
                if (index < normalManaged.Count)
                {
                    DeckInstance picked = normalManaged[index];
                    Executor executor = (Executor)Activator.CreateInstance(picked.Type, ai, duel);
                    executor.Deck = picked.Deck;
                    return executor;
                }

                return InstantiateLuaDeck(ai, duel, normalLua[index - normalManaged.Count], luaRuntime, luaScriptProvider, throwOnLuaScriptError);
            }

            int total = allManaged.Count + allLua.Count;
            if (total == 0)
                throw new InvalidOperationException("No WindBot decks are registered.");

            int fallbackIndex = Random.Shared.Next(total);
            if (fallbackIndex < allManaged.Count)
            {
                DeckInstance picked = allManaged[fallbackIndex];
                Executor executor = (Executor)Activator.CreateInstance(picked.Type, ai, duel);
                executor.Deck = picked.Deck;
                return executor;
            }

            return InstantiateLuaDeck(ai, duel, allLua[fallbackIndex - allManaged.Count], luaRuntime, luaScriptProvider, throwOnLuaScriptError);
        }
    }
}
