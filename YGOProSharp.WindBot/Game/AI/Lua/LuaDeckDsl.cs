using System;
using System.Collections;
using System.Collections.Generic;
using YGOProSharp.Abstractions.Scripting;

namespace WindBot.Game.AI.Lua
{
    /// <summary>
    /// 面向 Lua 卡组脚本的声明式 DSL 委托（兼顾固定参数与变参，确保 XLua/MoonSharp/NLua/Lua-CSharp 按 0~3 参数均可无障碍调用）。
    /// </summary>
    public delegate void LuaDslAction0();
    public delegate void LuaDslAction1(object arg1 = null);
    public delegate void LuaDslAction2(object arg1 = null, object arg2 = null);
    public delegate void LuaDslAction3(object arg1 = null, object arg2 = null, object arg3 = null);
    public delegate void LuaDslVarArgs(params object[] args);

    /// <summary>
    /// 类似 xmake.lua 的声明式 Lua 卡组 DSL 宿主。
    /// 支持 <c>deck("Name") ... deck_end()</c> 作用域、<c>set_*</c> 元数据配置、
    /// <c>add_executor</c>/<c>add_executors</c>/<c>add_default</c> 规则注册、
    /// <c>on_activate</c>/<c>on_summon</c> 等动作简写，以及 <c>on_new_turn</c>/<c>on_select_card</c> 等全套生命周期钩子。
    /// </summary>
    public sealed class LuaDeckDsl
    {
        private readonly LuaDeckExecutor _executor;
        private readonly ILuaScriptContext _context;
        private readonly string _targetDeckName;

        private string _currentDeckScope;
        private bool _hasMatchedDeckScope;
        private bool _activeScope = true;

        private readonly Dictionary<string, object> _registeredHooks =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 脚本中通过 <c>deck("...")</c> 声明的生效卡组名称。
        /// </summary>
        public string DeclaredDeckName { get; private set; }

        /// <summary>
        /// 脚本中通过 <c>set_deckfile("...")</c> 声明的默认 .ydk 文件名。
        /// </summary>
        public string DeclaredDeckFile { get; private set; }

        /// <summary>
        /// 脚本中通过 <c>set_level("...")</c> 声明的难度等级。
        /// </summary>
        public string DeclaredLevel { get; private set; }

        /// <summary>
        /// 脚本中通过 <c>set_override(bool)</c> 声明的覆盖标记。
        /// </summary>
        public bool? DeclaredAllowOverride { get; private set; }

        /// <summary>
        /// 脚本中通过 <c>set_description("...")</c> 声明的说明文本。
        /// </summary>
        public string DeclaredDescription { get; private set; }

        /// <summary>
        /// 脚本中通过 <c>set_author("...")</c> 声明的作者信息。
        /// </summary>
        public string DeclaredAuthor { get; private set; }

        /// <summary>
        /// 脚本中通过 <c>set_throw_on_error(bool)</c> 覆盖的异常处理策略。
        /// </summary>
        public bool? DeclaredThrowOnError { get; private set; }

        internal LuaDeckDsl(LuaDeckExecutor executor, ILuaScriptContext context, string targetDeckName)
        {
            _executor = executor ?? throw new ArgumentNullException(nameof(executor));
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _targetDeckName = string.IsNullOrWhiteSpace(targetDeckName) ? null : targetDeckName.Trim();
        }

        internal IReadOnlyDictionary<string, object> RegisteredHooks => _registeredHooks;

        /// <summary>
        /// 将 xmake 风格的全局 DSL 函数注入到当前 Lua 上下文。
        /// </summary>
        internal void BindGlobals()
        {
            _context.SetGlobal("Dsl", this);

            // 作用域与元数据（类似 xmake 的 target / target_end / set_*）
            _context.SetGlobal("deck", (LuaDslAction1)Deck);
            _context.SetGlobal("deck_end", (LuaDslAction0)DeckEnd);
            _context.SetGlobal("set_deckfile", (LuaDslAction1)SetDeckFile);
            _context.SetGlobal("set_level", (LuaDslAction1)SetLevel);
            _context.SetGlobal("set_override", (LuaDslAction1)SetOverride);
            _context.SetGlobal("set_description", (LuaDslAction1)SetDescription);
            _context.SetGlobal("set_author", (LuaDslAction1)SetAuthor);
            _context.SetGlobal("set_throw_on_error", (LuaDslAction1)SetThrowOnError);

            // 规则注册（类似 xmake 的 add_*；使用具名带默认参数的方法以确保反射绑定保留 Optional 参数元数据）
            _context.SetGlobal("add_executor", (LuaDslAction3)AddExecutorRule);
            _context.SetGlobal("add_executors", (LuaDslAction3)AddExecutorsRule);
            _context.SetGlobal("add_default", (LuaDslAction3)AddDefaultRule);

            // 按 ExecutorType 提供的动作简写（具名默认参数方法，兼容 NLua/XLua/MoonSharp 1~2 参数调用）
            _context.SetGlobal("on_activate", (LuaDslAction2)OnActivateRule);
            _context.SetGlobal("on_summon", (LuaDslAction2)OnSummonRule);
            _context.SetGlobal("on_spsummon", (LuaDslAction2)OnSpSummonRule);
            _context.SetGlobal("on_repos", (LuaDslAction2)OnReposRule);
            _context.SetGlobal("on_monster_set", (LuaDslAction2)OnMonsterSetRule);
            _context.SetGlobal("on_spell_set", (LuaDslAction2)OnSpellSetRule);
            _context.SetGlobal("on_summon_or_set", (LuaDslAction2)OnSummonOrSetRule);
            _context.SetGlobal("on_goto_battle", (LuaDslAction1)OnGoToBattleRule);
            _context.SetGlobal("on_goto_main2", (LuaDslAction1)OnGoToMain2Rule);
            _context.SetGlobal("on_goto_end", (LuaDslAction1)OnGoToEndRule);
            _context.SetGlobal("on_surrender", (LuaDslAction1)OnSurrenderRule);

            // 生命周期与决策钩子（类似 xmake 的 on_load / on_build / on_clean）
            BindHook("on_init", LuaDeckHookNames.OnInit);
            BindHook("on_dispose", LuaDeckHookNames.OnDispose);
            BindHook("on_rock_paper_scissors", LuaDeckHookNames.OnRockPaperScissors);
            BindHook("on_select_hand", LuaDeckHookNames.OnSelectHand);
            BindHook("on_battle", LuaDeckHookNames.OnBattle);
            BindHook("on_select_attacker", LuaDeckHookNames.OnSelectAttacker);
            BindHook("on_select_attack_target", LuaDeckHookNames.OnSelectAttackTarget);
            BindHook("on_pre_battle", LuaDeckHookNames.OnPreBattleBetween);
            BindHook("on_pre_battle_between", LuaDeckHookNames.OnPreBattleBetween);
            BindHook("on_select_battle_replay", LuaDeckHookNames.OnSelectBattleReplay);
            BindHook("on_pre_activate", LuaDeckHookNames.OnPreActivate);
            BindHook("on_chaining", LuaDeckHookNames.OnChaining);
            BindHook("on_chain_solved", LuaDeckHookNames.OnChainSolved);
            BindHook("on_chain_end", LuaDeckHookNames.OnChainEnd);
            BindHook("on_select_chain", LuaDeckHookNames.OnSelectChain);
            BindHook("on_receiving_announce", LuaDeckHookNames.OnReceivingAnnouce);
            BindHook("on_new_phase", LuaDeckHookNames.OnNewPhase);
            BindHook("on_new_turn", LuaDeckHookNames.OnNewTurn);
            BindHook("on_draw", LuaDeckHookNames.OnDraw);
            BindHook("on_player_hint", LuaDeckHookNames.OnPlayerHint);
            BindHook("on_hint_zone", LuaDeckHookNames.OnHintZone);
            BindHook("on_move", LuaDeckHookNames.OnMove);
            BindHook("on_pos_change", LuaDeckHookNames.OnPosChange);
            BindHook("on_summoning", LuaDeckHookNames.OnSummoning);
            BindHook("on_sp_summoning", LuaDeckHookNames.OnSpSummoning);
            BindHook("on_sp_summoned", LuaDeckHookNames.OnSpSummoned);
            BindHook("on_select_card", LuaDeckHookNames.OnSelectCard);
            BindHook("on_select_tribute", LuaDeckHookNames.OnSelectTribute);
            BindHook("on_select_sum", LuaDeckHookNames.OnSelectSum);
            BindHook("on_select_fusion_material", LuaDeckHookNames.OnSelectFusionMaterial);
            BindHook("on_select_synchro_material", LuaDeckHookNames.OnSelectSynchroMaterial);
            BindHook("on_select_xyz_material", LuaDeckHookNames.OnSelectXyzMaterial);
            BindHook("on_select_link_material", LuaDeckHookNames.OnSelectLinkMaterial);
            BindHook("on_select_ritual_tribute", LuaDeckHookNames.OnSelectRitualTribute);
            BindHook("on_select_pendulum_summon", LuaDeckHookNames.OnSelectPendulumSummon);
            BindHook("on_card_sorting", LuaDeckHookNames.OnCardSorting);
            BindHook("on_select_yesno", LuaDeckHookNames.OnSelectYesNo);
            BindHook("on_select_option", LuaDeckHookNames.OnSelectOption);
            BindHook("on_select_place", LuaDeckHookNames.OnSelectPlace);
            BindHook("on_select_disfield", LuaDeckHookNames.OnSelectDisfield);
            BindHook("on_select_position", LuaDeckHookNames.OnSelectPosition);
            BindHook("on_select_battle_direct_attack", LuaDeckHookNames.OnSelectBattleDirectAttack);
            BindHook("on_select_monster_summon_or_set", LuaDeckHookNames.OnSelectMonsterSummonOrSet);
            BindHook("on_announce_card", LuaDeckHookNames.OnAnnounceCard);
        }

        public void Deck(object name)
        {
            string scopeName = name?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(scopeName))
                throw new ArgumentException("deck(name) requires a non-empty deck name.", nameof(name));

            _currentDeckScope = scopeName;

            if (_targetDeckName == null)
            {
                // 未指定目标名时，默认激活脚本中的第一个 deck(...) 作用域。
                if (!_hasMatchedDeckScope)
                {
                    _hasMatchedDeckScope = true;
                    _activeScope = true;
                    DeclaredDeckName = scopeName;
                }
                else
                {
                    _activeScope = string.Equals(DeclaredDeckName, scopeName, StringComparison.OrdinalIgnoreCase);
                }
            }
            else
            {
                bool matched = string.Equals(_targetDeckName, scopeName, StringComparison.OrdinalIgnoreCase);
                _activeScope = matched;
                if (matched)
                {
                    _hasMatchedDeckScope = true;
                    DeclaredDeckName = scopeName;
                }
            }
        }

        public void DeckEnd()
        {
            _currentDeckScope = null;
            _activeScope = true;
        }

        public void SetDeckFile(object fileName)
        {
            if (!_activeScope)
                return;

            string text = fileName?.ToString()?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                return;

            DeclaredDeckFile = text;
            _executor.Deck = text;
        }

        public void SetLevel(object level)
        {
            if (!_activeScope)
                return;

            string text = level?.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(text))
                DeclaredLevel = text;
        }

        public void SetOverride(object allowOverride)
        {
            if (!_activeScope)
                return;

            if (LuaValueConverter.TryToBoolean(allowOverride, out bool value))
                DeclaredAllowOverride = value;
        }

        public void SetDescription(object description)
        {
            if (!_activeScope)
                return;

            DeclaredDescription = description?.ToString();
        }

        public void SetAuthor(object author)
        {
            if (!_activeScope)
                return;

            DeclaredAuthor = author?.ToString();
        }

        public void SetThrowOnError(object enable)
        {
            if (!_activeScope)
                return;

            if (LuaValueConverter.TryToBoolean(enable, out bool value))
                DeclaredThrowOnError = value;
        }

        public void AddExecutorRule(object type, object cardIdOrCallback = null, object callback = null)
        {
            if (!_activeScope)
                return;

            ExecutorType executorType = ParseExecutorType(type);
            AddTypedRule(executorType, cardIdOrCallback, callback);
        }

        public void AddExecutorsRule(object type, object cardIds, object callback = null)
        {
            if (!_activeScope)
                return;

            ExecutorType executorType = ParseExecutorType(type);
            IList<int> ids = ResolveCardIds(cardIds);
            if (ids == null || ids.Count == 0)
                throw new ArgumentException("add_executors(type, cardIds, callback) requires a non-empty card id list.", nameof(cardIds));

            for (int i = 0; i < ids.Count; i++)
                _executor.QueueDslExecutorRule(executorType, ids[i], callback);
        }

        public void AddDefaultRule(object type, object cardIdOrRuleName, object ruleName = null)
        {
            if (!_activeScope)
                return;

            ExecutorType executorType = ParseExecutorType(type);

            if (ruleName == null && cardIdOrRuleName is string directRuleName)
            {
                _executor.QueueDslDefaultRule(executorType, -1, directRuleName);
                return;
            }

            if (!LuaValueConverter.TryToInt32(cardIdOrRuleName, out int cardId))
            {
                throw new ArgumentException(
                    "add_default(type, cardId, ruleName) or add_default(type, ruleName) received invalid arguments.",
                    nameof(cardIdOrRuleName));
            }

            string resolvedRuleName = ruleName?.ToString();
            if (string.IsNullOrWhiteSpace(resolvedRuleName))
                throw new ArgumentException("add_default requires a non-empty DefaultExecutor rule name.", nameof(ruleName));

            _executor.QueueDslDefaultRule(executorType, cardId, resolvedRuleName);
        }

        public void OnActivateRule(object cardIdOrCallback = null, object callback = null) => AddTypedRule(ExecutorType.Activate, cardIdOrCallback, callback);
        public void OnSummonRule(object cardIdOrCallback = null, object callback = null) => AddTypedRule(ExecutorType.Summon, cardIdOrCallback, callback);
        public void OnSpSummonRule(object cardIdOrCallback = null, object callback = null) => AddTypedRule(ExecutorType.SpSummon, cardIdOrCallback, callback);
        public void OnReposRule(object cardIdOrCallback = null, object callback = null) => AddTypedRule(ExecutorType.Repos, cardIdOrCallback, callback);
        public void OnMonsterSetRule(object cardIdOrCallback = null, object callback = null) => AddTypedRule(ExecutorType.MonsterSet, cardIdOrCallback, callback);
        public void OnSpellSetRule(object cardIdOrCallback = null, object callback = null) => AddTypedRule(ExecutorType.SpellSet, cardIdOrCallback, callback);
        public void OnSummonOrSetRule(object cardIdOrCallback = null, object callback = null) => AddTypedRule(ExecutorType.SummonOrSet, cardIdOrCallback, callback);
        public void OnGoToBattleRule(object callback = null) => AddTypedRule(ExecutorType.GoToBattlePhase, callback, null);
        public void OnGoToMain2Rule(object callback = null) => AddTypedRule(ExecutorType.GoToMainPhase2, callback, null);
        public void OnGoToEndRule(object callback = null) => AddTypedRule(ExecutorType.GoToEndPhase, callback, null);
        public void OnSurrenderRule(object callback = null) => AddTypedRule(ExecutorType.Surrender, callback, null);

        private void AddTypedRule(ExecutorType executorType, object cardIdOrCallback, object callback)
        {
            if (!_activeScope)
                return;

            // 1 参（无 cardId 与回调）：等价于 Executor.AddExecutor(type)
            if (cardIdOrCallback == null && callback == null)
            {
                _executor.QueueDslNoExecutorRule(executorType);
                return;
            }

            // 2/3 参且首个实参是卡号
            if (callback != null || LuaValueConverter.TryToInt32(cardIdOrCallback, out _))
            {
                if (!LuaValueConverter.TryToInt32(cardIdOrCallback, out int cardId))
                {
                    throw new ArgumentException(
                        $"Expected numeric card id for '{executorType}' rule, got '{cardIdOrCallback}'.",
                        nameof(cardIdOrCallback));
                }

                _executor.QueueDslExecutorRule(executorType, cardId, callback);
                return;
            }

            // 2 参且实参是回调（不限卡号）
            _executor.QueueDslExecutorRule(executorType, -1, cardIdOrCallback);
        }

        private void BindHook(string dslName, string hookName)
        {
            _context.SetGlobal(dslName, (LuaDslAction1)(callback =>
            {
                if (!_activeScope)
                    return;

                if (callback == null)
                {
                    _registeredHooks.Remove(hookName);
                    return;
                }

                _registeredHooks[hookName] = callback;
            }));
        }

        private static ExecutorType ParseExecutorType(object type)
        {
            if (LuaValueConverter.TryToEnum(type, out ExecutorType parsed))
                return parsed;

            throw new ArgumentException($"Invalid ExecutorType value '{type}'.", nameof(type));
        }

        private IList<int> ResolveCardIds(object cardIds)
        {
            if (cardIds == null)
                return null;

            if (cardIds is IList<int> intList)
                return intList;

            if (_context.TryConvertList(cardIds, out IList<int> convertedInt) && convertedInt != null)
                return convertedInt;

            // 某些 Lua 引擎（如 LuaJIT/XLua）数字表元素可能先映射为 double/long/object
            if (_context.TryConvertList(cardIds, out IList<object> convertedObjects) && convertedObjects != null)
                return ConvertObjectSequenceToInts(convertedObjects);

            if (cardIds is IEnumerable enumerable && cardIds is not string)
                return ConvertObjectSequenceToInts(enumerable);

            if (LuaValueConverter.TryToInt32(cardIds, out int singleId))
                return [singleId];

            return null;
        }

        private static List<int> ConvertObjectSequenceToInts(IEnumerable source)
        {
            List<int> ids = new List<int>();
            foreach (object item in source)
            {
                if (LuaValueConverter.TryToInt32(item, out int id))
                    ids.Add(id);
            }
            return ids;
        }
    }
}
