using System;
using System.Collections;
using System.Collections.Generic;
using YGOProSharp.Abstractions.Scripting;

namespace WindBot.Game.AI.Lua
{
    /// <summary>
    /// 面向 Lua 对话 DSL 的条件判断委托（如 <c>is_locale("zh-CN", "zh")</c>），采用固定参确保 XLua / NLua 兼容。
    /// </summary>
    public delegate bool LuaDslPredicate3(object arg1 = null, object arg2 = null, object arg3 = null);

    /// <summary>
    /// 类似 xmake.lua 的声明式 Lua 对话 DSL 宿主。
    /// 支持 <c>dialog("Name") ... dialog_end()</c> 作用域、<c>locale("zh-CN") ... locale_end()</c> 本地化分区、
    /// <c>is_locale(...)</c> 条件判断、<c>set_*</c> 元数据与回退配置、
    /// <c>add_*</c>/<c>set_*</c>/<c>clear_lines</c> 台词声明，以及 <c>on_summon</c>/<c>on_attack</c> 等动态台词钩子。
    /// 全局函数均通过 <see cref="LuaDslAction0"/>~<see cref="LuaDslAction3"/> 与 <see cref="LuaDslPredicate3"/> 固定参委托注入，兼容 XLua / NLua / MoonSharp / Lua-CSharp。
    /// </summary>
    public sealed class LuaDialogDsl
    {
        private readonly ILuaScriptContext _context;
        private readonly string _targetDialogName;
        private readonly string _activeLocale;
        private readonly Action<string> _includeExecutor;

        private bool _hasMatchedDialogScope;
        private bool _dialogScopeActive = true;
        private bool _localeScopeActive = true;

        private bool IsActiveScope => _dialogScopeActive && _localeScopeActive;

        private readonly Dictionary<string, List<string>> _linesByEvent =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly HashSet<string> _definedEvents =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<string, object> _registeredHooks =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 脚本中通过 <c>dialog("...")</c> 声明的生效对话名称。
        /// </summary>
        public string DeclaredDialogName { get; private set; }

        /// <summary>
        /// 脚本中通过 <c>set_locale("...")</c> 声明的语言区域。
        /// </summary>
        public string DeclaredLocale { get; private set; }

        /// <summary>
        /// 脚本中通过 <c>set_description("...")</c> 声明的说明文本。
        /// </summary>
        public string DeclaredDescription { get; private set; }

        /// <summary>
        /// 脚本中通过 <c>set_author("...")</c> 声明的作者信息。
        /// </summary>
        public string DeclaredAuthor { get; private set; }

        /// <summary>
        /// 脚本中通过 <c>set_fallback("...")</c> 声明的回退对话名（如 <c>"zh-CN"</c> 或 <c>"default"</c>）。
        /// </summary>
        public string DeclaredFallback { get; private set; }

        /// <summary>
        /// 脚本中通过 <c>set_override(bool)</c> 声明的覆盖标记。
        /// </summary>
        public bool? DeclaredAllowOverride { get; private set; }

        /// <summary>
        /// 脚本中通过 <c>set_throw_on_error(bool)</c> 覆盖的异常处理策略。
        /// </summary>
        public bool? DeclaredThrowOnError { get; private set; }

        /// <summary>
        /// 脚本中通过 <c>set_facedown_monster_name("...")</c> 声明的里侧怪兽称呼。
        /// </summary>
        public string DeclaredFacedownMonsterName { get; private set; }

        /// <summary>
        /// 当前加载对话时生效的本地化区域标识（来自 <see cref="GameClient.Locale"/> 或 <see cref="WindBotRuntime.Locale"/>）。
        /// </summary>
        public string ActiveLocale => _activeLocale;

        internal LuaDialogDsl(
            ILuaScriptContext context,
            string targetDialogName,
            string activeLocale = null,
            Action<string> includeExecutor = null)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _targetDialogName = NormalizeTargetName(targetDialogName);
            _activeLocale = NormalizeLocale(activeLocale);
            _includeExecutor = includeExecutor;
        }

        internal IReadOnlyDictionary<string, List<string>> LinesByEvent => _linesByEvent;
        internal IReadOnlySet<string> DefinedEvents => _definedEvents;
        internal IReadOnlyDictionary<string, object> RegisteredHooks => _registeredHooks;

        /// <summary>
        /// 将 xmake 风格的全局对话 DSL 函数注入到当前 Lua 上下文。
        /// </summary>
        internal void BindGlobals()
        {
            _context.SetGlobal("Dsl", this);

            // 作用域、多文件组合、本地化与元数据（类似 xmake 的 target / includes / is_plat / set_*）
            _context.SetGlobal("dialog", (LuaDslAction1)Dialog);
            _context.SetGlobal("dialog_end", (LuaDslAction0)DialogEnd);
            _context.SetGlobal("includes", (LuaDslAction3)Includes);
            _context.SetGlobal("include", (LuaDslAction1)Include);
            _context.SetGlobal("locale", (LuaDslAction3)LocaleScope);
            _context.SetGlobal("locale_end", (LuaDslAction0)LocaleEnd);
            _context.SetGlobal("is_locale", (LuaDslPredicate3)IsLocale);
            _context.SetGlobal("set_locale", (LuaDslAction1)SetLocale);
            _context.SetGlobal("set_description", (LuaDslAction1)SetDescription);
            _context.SetGlobal("set_author", (LuaDslAction1)SetAuthor);
            _context.SetGlobal("set_fallback", (LuaDslAction1)SetFallback);
            _context.SetGlobal("set_override", (LuaDslAction1)SetOverride);
            _context.SetGlobal("set_throw_on_error", (LuaDslAction1)SetThrowOnError);
            _context.SetGlobal("set_facedown_monster_name", (LuaDslAction1)SetFacedownMonsterName);
            _context.SetGlobal("set_facedownmonstername", (LuaDslAction1)SetFacedownMonsterName);

            // 通用台词操作
            _context.SetGlobal("add_lines", (LuaDslAction3)AddLines);
            _context.SetGlobal("set_lines", (LuaDslAction3)SetLines);
            _context.SetGlobal("clear_lines", (LuaDslAction1)ClearLines);

            // 按事件类型提供的静态台词追加/覆盖简写（支持 1~3 个字符串或 Lua 字符串数组表）
            BindEventLineHelpers(LuaDialogHookNames.Welcome, "welcome");
            BindEventLineHelpers(LuaDialogHookNames.DeckError, "deckerror", "deck_error");
            BindEventLineHelpers(LuaDialogHookNames.DuelStart, "duelstart", "duel_start");
            BindEventLineHelpers(LuaDialogHookNames.NewTurn, "newturn", "new_turn");
            BindEventLineHelpers(LuaDialogHookNames.EndTurn, "endturn", "end_turn");
            BindEventLineHelpers(LuaDialogHookNames.DirectAttack, "directattack", "direct_attack");
            BindEventLineHelpers(LuaDialogHookNames.Attack, "attack");
            BindEventLineHelpers(LuaDialogHookNames.OnDirectAttack, "ondirectattack", "on_direct_attack");
            BindEventLineHelpers(LuaDialogHookNames.Activate, "activate");
            BindEventLineHelpers(LuaDialogHookNames.Summon, "summon");
            BindEventLineHelpers(LuaDialogHookNames.SetMonster, "setmonster", "set_monster");
            BindEventLineHelpers(LuaDialogHookNames.Chaining, "chaining");
            BindEventLineHelpers(LuaDialogHookNames.Surrender, "surrender");
            BindEventLineHelpers(LuaDialogHookNames.Custom, "custom");

            // 动态台词与生命周期钩子（类似 xmake 的 on_load / on_build / on_clean）
            BindHook("on_init", LuaDialogHookNames.OnInit);
            BindHook("on_dispose", LuaDialogHookNames.OnDispose);
            BindHook("on_message", LuaDialogHookNames.OnMessage);

            BindHook("on_welcome", LuaDialogHookNames.OnWelcome);
            BindHook("on_deckerror", LuaDialogHookNames.OnDeckError);
            BindHook("on_deck_error", LuaDialogHookNames.OnDeckError);
            BindHook("on_duelstart", LuaDialogHookNames.OnDuelStart);
            BindHook("on_duel_start", LuaDialogHookNames.OnDuelStart);
            BindHook("on_newturn", LuaDialogHookNames.OnNewTurn);
            BindHook("on_new_turn", LuaDialogHookNames.OnNewTurn);
            BindHook("on_endturn", LuaDialogHookNames.OnEndTurn);
            BindHook("on_end_turn", LuaDialogHookNames.OnEndTurn);
            BindHook("on_directattack", LuaDialogHookNames.OnDirectAttackHook);
            BindHook("on_direct_attack", LuaDialogHookNames.OnDirectAttackHook);
            BindHook("on_attack", LuaDialogHookNames.OnAttack);
            BindHook("on_ondirectattack", LuaDialogHookNames.OnReceiveDirectAttack);
            BindHook("on_on_direct_attack", LuaDialogHookNames.OnReceiveDirectAttack);
            BindHook("on_receive_direct_attack", LuaDialogHookNames.OnReceiveDirectAttack);
            BindHook("on_activate", LuaDialogHookNames.OnActivate);
            BindHook("on_summon", LuaDialogHookNames.OnSummon);
            BindHook("on_setmonster", LuaDialogHookNames.OnSetMonster);
            BindHook("on_set_monster", LuaDialogHookNames.OnSetMonster);
            BindHook("on_chaining", LuaDialogHookNames.OnChaining);
            BindHook("on_surrender", LuaDialogHookNames.OnSurrender);
            BindHook("on_custom", LuaDialogHookNames.OnCustom);
        }

        public void Dialog(object name)
        {
            string scopeName = name?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(scopeName))
                throw new ArgumentException("dialog(name) requires a non-empty dialog name.", nameof(name));

            _localeScopeActive = true;

            if (_targetDialogName == null)
            {
                if (!_hasMatchedDialogScope)
                {
                    _hasMatchedDialogScope = true;
                    _dialogScopeActive = true;
                    DeclaredDialogName = scopeName;
                }
                else
                {
                    _dialogScopeActive = string.Equals(DeclaredDialogName, scopeName, StringComparison.OrdinalIgnoreCase);
                }
            }
            else
            {
                bool matched = string.Equals(_targetDialogName, scopeName, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrEmpty(_activeLocale) &&
                        string.Equals(_targetDialogName + "." + _activeLocale, scopeName, StringComparison.OrdinalIgnoreCase));
                _dialogScopeActive = matched;
                if (matched)
                {
                    _hasMatchedDialogScope = true;
                    DeclaredDialogName = scopeName;
                }
            }
        }

        public void DialogEnd()
        {
            _dialogScopeActive = true;
            _localeScopeActive = true;
        }

        /// <summary>
        /// 引入单个子对话 Lua 文件（xmake 风格 <c>include("common.lua")</c>），在当前上下文中顺序执行。
        /// </summary>
        public void Include(object scriptName = null)
        {
            Includes(scriptName, null, null);
        }

        /// <summary>
        /// 批量引入多个子对话 Lua 文件（xmake 风格 <c>includes("a.lua", "b.lua")</c> 或 <c>includes({ "a.lua", "b.lua" })</c>），
        /// 便于按角色、模块或语言将对话拆分为多个文件维护。
        /// </summary>
        public void Includes(object script1 = null, object script2 = null, object script3 = null)
        {
            if (!IsActiveScope)
                return;

            if (_includeExecutor == null)
                throw new InvalidOperationException("includes(...) requires an ILuaScriptSourceProvider configured in DialogsManager.");

            List<string> scripts = new List<string>(3);
            AppendResolvedStrings(scripts, script1);
            AppendResolvedStrings(scripts, script2);
            AppendResolvedStrings(scripts, script3);

            for (int i = 0; i < scripts.Count; i++)
            {
                string item = scripts[i]?.Trim();
                if (!string.IsNullOrWhiteSpace(item))
                    _includeExecutor(item);
            }
        }

        /// <summary>
        /// 开启本地化覆盖作用域（类似 xmake 条件块），仅当当前生效的 <see cref="ActiveLocale"/> 匹配任意传入标签时内部配置生效。
        /// 支持 <c>locale("zh-CN")</c>、<c>locale("zh-CN", "zh")</c> 或 <c>locale({ "zh-CN", "zh" })</c>。
        /// </summary>
        public void LocaleScope(object tag1 = null, object tag2 = null, object tag3 = null)
        {
            if (tag1 == null && tag2 == null && tag3 == null)
            {
                _localeScopeActive = string.IsNullOrEmpty(_activeLocale);
                return;
            }

            _localeScopeActive = IsLocale(tag1, tag2, tag3);
        }

        /// <summary>
        /// 结束当前 <c>locale(...)</c> 本地化作用域。
        /// </summary>
        public void LocaleEnd()
        {
            _localeScopeActive = true;
        }

        /// <summary>
        /// 判断当前生效的 <see cref="ActiveLocale"/> 是否匹配任意传入的语言标签（支持精确与主语言前缀匹配，如 <c>"zh"</c> 匹配 <c>"zh-CN"</c>）。
        /// </summary>
        public bool IsLocale(object tag1 = null, object tag2 = null, object tag3 = null)
        {
            List<string> tags = new List<string>(3);
            AppendResolvedStrings(tags, tag1);
            AppendResolvedStrings(tags, tag2);
            AppendResolvedStrings(tags, tag3);

            if (tags.Count == 0)
                return string.IsNullOrEmpty(_activeLocale);

            for (int i = 0; i < tags.Count; i++)
            {
                if (MatchesLocale(_activeLocale, tags[i]))
                    return true;
            }

            return false;
        }

        public void SetLocale(object locale)
        {
            if (!IsActiveScope)
                return;

            string text = locale?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(text))
                DeclaredLocale = text;
        }

        public void SetDescription(object description)
        {
            if (!IsActiveScope)
                return;

            DeclaredDescription = description?.ToString();
        }

        public void SetAuthor(object author)
        {
            if (!IsActiveScope)
                return;

            DeclaredAuthor = author?.ToString();
        }

        public void SetFallback(object dialogName)
        {
            if (!IsActiveScope)
                return;

            string text = dialogName?.ToString()?.Trim();
            DeclaredFallback = string.IsNullOrWhiteSpace(text) ? null : text;
        }

        public void SetOverride(object allowOverride)
        {
            if (!IsActiveScope)
                return;

            if (LuaValueConverter.TryToBoolean(allowOverride, out bool value))
                DeclaredAllowOverride = value;
        }

        public void SetThrowOnError(object enable)
        {
            if (!IsActiveScope)
                return;

            if (LuaValueConverter.TryToBoolean(enable, out bool value))
                DeclaredThrowOnError = value;
        }

        public void SetFacedownMonsterName(object name)
        {
            if (!IsActiveScope)
                return;

            string text = name?.ToString();
            if (!string.IsNullOrEmpty(text))
                DeclaredFacedownMonsterName = text;
        }

        public void AddLines(object eventName, object lineOrTable = null, object extraLine = null)
        {
            if (!IsActiveScope)
                return;

            string key = RequireEventKey(eventName);
            AddEventLinesInternal(key, lineOrTable, extraLine, null, replace: false);
        }

        public void SetLines(object eventName, object lineOrTable = null, object extraLine = null)
        {
            if (!IsActiveScope)
                return;

            string key = RequireEventKey(eventName);
            AddEventLinesInternal(key, lineOrTable, extraLine, null, replace: true);
        }

        public void ClearLines(object eventName)
        {
            if (!IsActiveScope)
                return;

            string key = RequireEventKey(eventName);
            _definedEvents.Add(key);
            _linesByEvent[key] = new List<string>();
        }

        private void BindEventLineHelpers(string eventKey, params string[] suffixes)
        {
            // 使用具名带默认参数的方法，确保 NLua/XLua 反射绑定能够读取 Optional 参数元数据（支持 1~3 参调用）。
            EventLineBinder binder = new(this, eventKey);
            LuaDslAction3 addAction = binder.Add;
            LuaDslAction3 setAction = binder.Set;

            foreach (string suffix in suffixes)
            {
                _context.SetGlobal("add_" + suffix, addAction);
                _context.SetGlobal("set_" + suffix, setAction);
            }
        }

        private void BindHook(string dslName, string hookName)
        {
            HookBinder binder = new(this, hookName);
            _context.SetGlobal(dslName, (LuaDslAction1)binder.Register);
        }

        private void RegisterHookInternal(string hookName, object callback)
        {
            if (!IsActiveScope)
                return;

            if (callback == null)
            {
                _registeredHooks.Remove(hookName);
                return;
            }

            _registeredHooks[hookName] = callback;
        }

        private sealed class EventLineBinder
        {
            private readonly LuaDialogDsl _owner;
            private readonly string _eventKey;

            public EventLineBinder(LuaDialogDsl owner, string eventKey)
            {
                _owner = owner;
                _eventKey = eventKey;
            }

            public void Add(object arg1 = null, object arg2 = null, object arg3 = null)
            {
                if (!_owner.IsActiveScope)
                    return;

                _owner.AddEventLinesInternal(_eventKey, arg1, arg2, arg3, replace: false);
            }

            public void Set(object arg1 = null, object arg2 = null, object arg3 = null)
            {
                if (!_owner.IsActiveScope)
                    return;

                _owner.AddEventLinesInternal(_eventKey, arg1, arg2, arg3, replace: true);
            }
        }

        private sealed class HookBinder
        {
            private readonly LuaDialogDsl _owner;
            private readonly string _hookName;

            public HookBinder(LuaDialogDsl owner, string hookName)
            {
                _owner = owner;
                _hookName = hookName;
            }

            public void Register(object callback = null)
            {
                _owner.RegisterHookInternal(_hookName, callback);
            }
        }

        private void AddEventLinesInternal(string eventKey, object arg1, object arg2, object arg3, bool replace)
        {
            _definedEvents.Add(eventKey);

            if (replace || !_linesByEvent.TryGetValue(eventKey, out List<string> target))
            {
                target = new List<string>();
                _linesByEvent[eventKey] = target;
            }

            AppendResolvedStrings(target, arg1);
            AppendResolvedStrings(target, arg2);
            AppendResolvedStrings(target, arg3);
        }

        internal void AppendResolvedStrings(List<string> target, object value)
        {
            if (value == null)
                return;

            if (value is string single)
            {
                target.Add(single);
                return;
            }

            if (value is IList<string> stringList)
            {
                for (int i = 0; i < stringList.Count; i++)
                {
                    if (stringList[i] != null)
                        target.Add(stringList[i]);
                }
                return;
            }

            if (_context.TryConvertList(value, out IList<string> convertedStrings) && convertedStrings != null)
            {
                for (int i = 0; i < convertedStrings.Count; i++)
                {
                    if (convertedStrings[i] != null)
                        target.Add(convertedStrings[i]);
                }
                return;
            }

            if (_context.TryConvertList(value, out IList<object> convertedObjects) && convertedObjects != null)
            {
                for (int i = 0; i < convertedObjects.Count; i++)
                {
                    if (convertedObjects[i] != null)
                        target.Add(convertedObjects[i].ToString());
                }
                return;
            }

            if (value is IEnumerable enumerable)
            {
                foreach (object item in enumerable)
                {
                    if (item != null)
                        target.Add(item.ToString());
                }
                return;
            }

            target.Add(value.ToString());
        }

        private static string RequireEventKey(object eventName)
        {
            string raw = eventName?.ToString();
            if (!LuaDialogHookNames.TryNormalizeEventKey(raw, out string normalized))
                throw new ArgumentException($"Unknown WindBot dialog event '{raw}'.", nameof(eventName));

            return normalized;
        }

        internal static string NormalizeLocale(string locale)
        {
            if (string.IsNullOrWhiteSpace(locale))
                return null;

            return locale.Trim().Replace('_', '-');
        }

        /// <summary>
        /// 按 BCP-47 规则判断当前区域是否匹配目标标签（如 <c>"zh-CN"</c> 匹配 <c>"zh-CN"</c> 与 <c>"zh"</c>；<c>"default"</c> 匹配未指定区域）。
        /// </summary>
        public static bool MatchesLocale(string currentLocale, string candidateTag)
        {
            string normalizedCandidate = NormalizeLocale(candidateTag);
            if (string.IsNullOrEmpty(normalizedCandidate))
                return false;

            string normalizedCurrent = NormalizeLocale(currentLocale);
            if (string.Equals(normalizedCandidate, "default", StringComparison.OrdinalIgnoreCase))
                return string.IsNullOrEmpty(normalizedCurrent);

            if (string.IsNullOrEmpty(normalizedCurrent))
                return false;

            if (string.Equals(normalizedCurrent, normalizedCandidate, StringComparison.OrdinalIgnoreCase))
                return true;

            // 前缀匹配：candidate="zh" 匹配 current="zh-CN"；candidate="en" 匹配 current="en-US"
            return normalizedCurrent.StartsWith(normalizedCandidate + "-", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 为给定对话名和区域生成按优先级排序的本地化候选名（不含扩展名）。
        /// 例如：(<c>"cirno"</c>, <c>"zh-CN"</c>) -> <c>["cirno.zh-CN", "cirno.zh", "cirno"]</c>；
        /// (<c>"default"</c>, <c>"zh-CN"</c>) -> <c>["default.zh-CN", "zh-CN", "default.zh", "zh", "default"]</c>。
        /// </summary>
        internal static IReadOnlyList<string> ExpandLocalizedDialogCandidates(string dialogName, string locale)
        {
            List<string> candidates = new List<string>(5);
            if (string.IsNullOrWhiteSpace(dialogName))
                return candidates;

            string trimmedName = dialogName.Trim();
            string normalizedLocale = NormalizeLocale(locale);
            bool hasExplicitLuaExt = trimmedName.EndsWith(".lua", StringComparison.OrdinalIgnoreCase);
            string baseName = hasExplicitLuaExt ? trimmedName[..^4] : trimmedName;

            void AddUnique(string value)
            {
                if (!string.IsNullOrWhiteSpace(value) &&
                    !candidates.Contains(value, StringComparer.OrdinalIgnoreCase))
                {
                    candidates.Add(value);
                }
            }

            if (!string.IsNullOrEmpty(normalizedLocale))
            {
                bool alreadyHasLocaleSuffix =
                    baseName.EndsWith("." + normalizedLocale, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(baseName, normalizedLocale, StringComparison.OrdinalIgnoreCase);

                if (!alreadyHasLocaleSuffix)
                {
                    AddUnique(baseName + "." + normalizedLocale);
                    if (string.Equals(baseName, "default", StringComparison.OrdinalIgnoreCase))
                        AddUnique(normalizedLocale);

                    int dash = normalizedLocale.IndexOf('-');
                    if (dash > 0)
                    {
                        string primaryLang = normalizedLocale[..dash];
                        if (!baseName.EndsWith("." + primaryLang, StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(baseName, primaryLang, StringComparison.OrdinalIgnoreCase))
                        {
                            AddUnique(baseName + "." + primaryLang);
                            if (string.Equals(baseName, "default", StringComparison.OrdinalIgnoreCase))
                                AddUnique(primaryLang);
                        }
                    }
                }
            }

            AddUnique(baseName);
            return candidates;
        }

        private static string NormalizeTargetName(string targetDialogName)
        {
            if (string.IsNullOrWhiteSpace(targetDialogName))
                return null;

            string trimmed = targetDialogName.Trim();
            if (trimmed.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed[..^4];

            int slash = Math.Max(trimmed.LastIndexOf('/'), trimmed.LastIndexOf('\\'));
            if (slash >= 0 && slash + 1 < trimmed.Length)
                trimmed = trimmed[(slash + 1)..];

            return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
        }
    }
}
