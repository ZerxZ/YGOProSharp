using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using YGOProSharp.Abstractions.Ocg.Enums;
using YGOProSharp.Abstractions.Scripting;

namespace WindBot.Game.AI.Lua
{
    /// <summary>
    /// 基于泛用 Lua 抽象接口（<see cref="ILuaRuntime"/> / <see cref="ILuaScriptContext"/>）实现的 WindBot 卡组执行器。
    /// 继承自 <see cref="DefaultExecutor"/>，Lua 脚本仅需覆写关心的回调与卡牌规则，未定义部分自动复用默认 AI 逻辑。
    /// </summary>
    public class LuaDeckExecutor : DefaultExecutor, IDisposable
    {
        private static readonly FrozenDictionary<string, Func<LuaDeckExecutor, bool>> DefaultRuleMap =
            CreateDefaultRuleMap();

        private static readonly IReadOnlyDictionary<string, int> ExecutorTypeConstants =
            CreateEnumMap<ExecutorType>();

        private static readonly IReadOnlyDictionary<string, int> CardLocationConstants =
            CreateEnumMap<CardLocation>();

        private static readonly IReadOnlyDictionary<string, int> CardPositionConstants =
            CreateEnumMap<CardPosition>();

        private static readonly IReadOnlyDictionary<string, int> CardTypeConstants =
            CreateEnumMap<CardType>();

        private static readonly IReadOnlyDictionary<string, int> CardAttributeConstants =
            CreateEnumMap<CardAttribute>();

        private static readonly IReadOnlyDictionary<string, int> CardRaceConstants =
            CreateEnumMap<CardRace>();

        private static readonly IReadOnlyDictionary<string, int> DuelPhaseConstants =
            CreateEnumMap<DuelPhase>();

        private readonly ILuaScriptContext _context;
        private readonly ILuaTable _moduleTable;
        private readonly List<ILuaFunction> _ownedFunctions = new List<ILuaFunction>();
        private readonly List<PendingDslRule> _pendingDslRules = new List<PendingDslRule>();
        private bool _isLoadingScript;
        private bool _throwOnScriptError;
        private bool _disposed;

        // 缓存所有生命周期和决策钩子，避免热路径反复向 Lua 查表。
        private readonly ILuaFunction _onInit;
        private readonly ILuaFunction _onDispose;
        private readonly ILuaFunction _onRockPaperScissors;
        private readonly ILuaFunction _onSelectHand;
        private readonly ILuaFunction _onBattle;
        private readonly ILuaFunction _onSelectAttacker;
        private readonly ILuaFunction _onSelectAttackTarget;
        private readonly ILuaFunction _onPreBattleBetween;
        private readonly ILuaFunction _onSelectBattleReplay;
        private readonly ILuaFunction _onPreActivate;
        private readonly ILuaFunction _onChaining;
        private readonly ILuaFunction _onChainSolved;
        private readonly ILuaFunction _onChainEnd;
        private readonly ILuaFunction _onSelectChain;
        private readonly ILuaFunction _onReceivingAnnouce;
        private readonly ILuaFunction _onNewPhase;
        private readonly ILuaFunction _onNewTurn;
        private readonly ILuaFunction _onDraw;
        private readonly ILuaFunction _onPlayerHint;
        private readonly ILuaFunction _onHintZone;
        private readonly ILuaFunction _onMove;
        private readonly ILuaFunction _onPosChange;
        private readonly ILuaFunction _onSummoning;
        private readonly ILuaFunction _onSpSummoning;
        private readonly ILuaFunction _onSpSummoned;
        private readonly ILuaFunction _onSelectCard;
        private readonly ILuaFunction _onSelectTribute;
        private readonly ILuaFunction _onSelectSum;
        private readonly ILuaFunction _onSelectFusionMaterial;
        private readonly ILuaFunction _onSelectSynchroMaterial;
        private readonly ILuaFunction _onSelectXyzMaterial;
        private readonly ILuaFunction _onSelectLinkMaterial;
        private readonly ILuaFunction _onSelectRitualTribute;
        private readonly ILuaFunction _onSelectPendulumSummon;
        private readonly ILuaFunction _onCardSorting;
        private readonly ILuaFunction _onSelectYesNo;
        private readonly ILuaFunction _onSelectOption;
        private readonly ILuaFunction _onSelectPlace;
        private readonly ILuaFunction _onSelectDisfield;
        private readonly ILuaFunction _onSelectPosition;
        private readonly ILuaFunction _onSelectBattleDirectAttack;
        private readonly ILuaFunction _onSelectMonsterSummonOrSet;
        private readonly ILuaFunction _onAnnounceCard;

        /// <summary>
        /// 当前脚本块名，用于日志与排错定位。
        /// </summary>
        public string ScriptChunkName { get; }

        /// <summary>
        /// xmake 风格声明式 DSL 宿主，包含脚本声明的卡组元数据。
        /// </summary>
        public LuaDeckDsl Dsl { get; }

        /// <summary>
        /// 是否已释放底层 Lua 资源。
        /// </summary>
        public bool IsDisposed => _disposed;

        // 将 Executor 中的 protected 状态以公开属性暴露，便于 XLua 等引擎直接访问。
        public ExecutorType CurrentType => Type;
        public ClientCard CurrentCard => Card;
        public int CurrentActivateDescription => ActivateDescription;
        public int CurrentEffectTiming => CurrentTiming;
        public ClientField BotField => Bot;
        public ClientField EnemyField => Enemy;

        public LuaDeckExecutor(
            GameAI ai,
            Duel duel,
            ILuaRuntime runtime,
            string scriptSource,
            string chunkName = null,
            bool throwOnScriptError = false,
            string targetDeckName = null)
            : base(ai, duel)
        {
            ArgumentNullException.ThrowIfNull(runtime);
            if (string.IsNullOrWhiteSpace(scriptSource))
                throw new ArgumentException("Lua deck script source cannot be null or whitespace.", nameof(scriptSource));

            ScriptChunkName = string.IsNullOrWhiteSpace(chunkName) ? "LuaDeck.lua" : chunkName;
            _throwOnScriptError = throwOnScriptError;

            _context = runtime.CreateContext(ScriptChunkName)
                ?? throw new InvalidOperationException($"Lua runtime returned a null context for '{ScriptChunkName}'.");

            try
            {
                Dsl = new LuaDeckDsl(this, _context, targetDeckName);
                BindHostGlobals();
                Dsl.BindGlobals();

                _isLoadingScript = true;
                object scriptResult;
                try
                {
                    scriptResult = _context.DoString(scriptSource, ScriptChunkName);
                }
                finally
                {
                    _isLoadingScript = false;
                }

                _moduleTable = scriptResult as ILuaTable;

                if (Dsl.DeclaredThrowOnError.HasValue)
                    _throwOnScriptError = Dsl.DeclaredThrowOnError.Value;

                FlushPendingDslRules();

                _onInit = ResolveHook(LuaDeckHookNames.OnInit);
                _onDispose = ResolveHook(LuaDeckHookNames.OnDispose);
                _onRockPaperScissors = ResolveHook(LuaDeckHookNames.OnRockPaperScissors);
                _onSelectHand = ResolveHook(LuaDeckHookNames.OnSelectHand);
                _onBattle = ResolveHook(LuaDeckHookNames.OnBattle);
                _onSelectAttacker = ResolveHook(LuaDeckHookNames.OnSelectAttacker);
                _onSelectAttackTarget = ResolveHook(LuaDeckHookNames.OnSelectAttackTarget);
                _onPreBattleBetween = ResolveHook(LuaDeckHookNames.OnPreBattleBetween);
                _onSelectBattleReplay = ResolveHook(LuaDeckHookNames.OnSelectBattleReplay);
                _onPreActivate = ResolveHook(LuaDeckHookNames.OnPreActivate);
                _onChaining = ResolveHook(LuaDeckHookNames.OnChaining);
                _onChainSolved = ResolveHook(LuaDeckHookNames.OnChainSolved);
                _onChainEnd = ResolveHook(LuaDeckHookNames.OnChainEnd);
                _onSelectChain = ResolveHook(LuaDeckHookNames.OnSelectChain);
                _onReceivingAnnouce = ResolveHook(LuaDeckHookNames.OnReceivingAnnouce)
                    ?? ResolveHook(LuaDeckHookNames.OnReceivingAnnounceAlias);
                _onNewPhase = ResolveHook(LuaDeckHookNames.OnNewPhase);
                _onNewTurn = ResolveHook(LuaDeckHookNames.OnNewTurn);
                _onDraw = ResolveHook(LuaDeckHookNames.OnDraw);
                _onPlayerHint = ResolveHook(LuaDeckHookNames.OnPlayerHint);
                _onHintZone = ResolveHook(LuaDeckHookNames.OnHintZone);
                _onMove = ResolveHook(LuaDeckHookNames.OnMove);
                _onPosChange = ResolveHook(LuaDeckHookNames.OnPosChange);
                _onSummoning = ResolveHook(LuaDeckHookNames.OnSummoning);
                _onSpSummoning = ResolveHook(LuaDeckHookNames.OnSpSummoning);
                _onSpSummoned = ResolveHook(LuaDeckHookNames.OnSpSummoned);
                _onSelectCard = ResolveHook(LuaDeckHookNames.OnSelectCard);
                _onSelectTribute = ResolveHook(LuaDeckHookNames.OnSelectTribute);
                _onSelectSum = ResolveHook(LuaDeckHookNames.OnSelectSum);
                _onSelectFusionMaterial = ResolveHook(LuaDeckHookNames.OnSelectFusionMaterial);
                _onSelectSynchroMaterial = ResolveHook(LuaDeckHookNames.OnSelectSynchroMaterial);
                _onSelectXyzMaterial = ResolveHook(LuaDeckHookNames.OnSelectXyzMaterial);
                _onSelectLinkMaterial = ResolveHook(LuaDeckHookNames.OnSelectLinkMaterial);
                _onSelectRitualTribute = ResolveHook(LuaDeckHookNames.OnSelectRitualTribute);
                _onSelectPendulumSummon = ResolveHook(LuaDeckHookNames.OnSelectPendulumSummon);
                _onCardSorting = ResolveHook(LuaDeckHookNames.OnCardSorting);
                _onSelectYesNo = ResolveHook(LuaDeckHookNames.OnSelectYesNo);
                _onSelectOption = ResolveHook(LuaDeckHookNames.OnSelectOption);
                _onSelectPlace = ResolveHook(LuaDeckHookNames.OnSelectPlace);
                _onSelectDisfield = ResolveHook(LuaDeckHookNames.OnSelectDisfield);
                _onSelectPosition = ResolveHook(LuaDeckHookNames.OnSelectPosition);
                _onSelectBattleDirectAttack = ResolveHook(LuaDeckHookNames.OnSelectBattleDirectAttack);
                _onSelectMonsterSummonOrSet = ResolveHook(LuaDeckHookNames.OnSelectMonsterSummonOrSet);
                _onAnnounceCard = ResolveHook(LuaDeckHookNames.OnAnnounceCard);

                if (_onInit != null)
                    InvokeVoid(_onInit, LuaDeckHookNames.OnInit, this);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>
        /// 向 Lua 侧暴露注册卡牌执行器的泛用入口。
        /// <paramref name="callback"/> 支持：
        /// <c>null</c>（无条件执行）、<see cref="Func{TResult}"/>（XLua 原生委托桥接）、
        /// <see cref="ILuaFunction"/>、Lua 函数名或内置 Default 规则名 <see cref="string"/>，
        /// 以及可由 <see cref="ILuaScriptContext.TryWrapFunction"/> 包装的第三方函数对象。
        /// </summary>
        public void AddLuaExecutor(ExecutorType type, int cardId, object callback = null)
        {
            ThrowIfDisposed();
            Func<bool> resolved = ResolveExecutorCallback(callback);
            AddExecutor(type, cardId, resolved);
        }

        public void AddLuaExecutor(int type, int cardId, object callback = null)
        {
            AddLuaExecutor((ExecutorType)type, cardId, callback);
        }

        public void AddLuaExecutor(string typeName, int cardId, object callback = null)
        {
            if (!Enum.TryParse(typeName, ignoreCase: true, out ExecutorType parsed))
                throw new ArgumentException($"Unknown ExecutorType '{typeName}'.", nameof(typeName));

            AddLuaExecutor(parsed, cardId, callback);
        }

        public void AddLuaExecutor(ExecutorType type, object callback)
        {
            AddLuaExecutor(type, -1, callback);
        }

        public void AddLuaExecutor(string typeName, object callback)
        {
            AddLuaExecutor(typeName, -1, callback);
        }

        /// <summary>
        /// 注册一条复用 <see cref="DefaultExecutor"/> 内置规则的卡牌执行器（如 "DefaultTrap"、"DefaultMonsterRepos"）。
        /// </summary>
        public void AddDefaultExecutor(ExecutorType type, int cardId, string defaultRuleName)
        {
            ThrowIfDisposed();
            if (!TryGetDefaultRule(defaultRuleName, out Func<bool> rule))
                throw new ArgumentException($"Unknown DefaultExecutor rule '{defaultRuleName}'.", nameof(defaultRuleName));

            AddExecutor(type, cardId, rule);
        }

        public void AddDefaultExecutor(string typeName, int cardId, string defaultRuleName)
        {
            if (!Enum.TryParse(typeName, ignoreCase: true, out ExecutorType parsed))
                throw new ArgumentException($"Unknown ExecutorType '{typeName}'.", nameof(typeName));

            AddDefaultExecutor(parsed, cardId, defaultRuleName);
        }

        public void AddDefaultExecutor(ExecutorType type, string defaultRuleName)
        {
            AddDefaultExecutor(type, -1, defaultRuleName);
        }

        public void AddDefaultExecutor(string typeName, string defaultRuleName)
        {
            AddDefaultExecutor(typeName, -1, defaultRuleName);
        }

        /// <summary>
        /// 供 Lua 脚本按名称直接调用 <see cref="DefaultExecutor"/> 的无参内置判定规则。
        /// </summary>
        public bool CallDefaultRule(string defaultRuleName)
        {
            if (string.IsNullOrWhiteSpace(defaultRuleName))
                return false;

            if (DefaultRuleMap.TryGetValue(defaultRuleName, out Func<LuaDeckExecutor, bool> rule))
                return rule(this);

            Logger.WriteErrorLine($"[LuaDeck:{ScriptChunkName}] Unknown DefaultExecutor rule '{defaultRuleName}'.");
            return false;
        }

        /// <summary>
        /// 尝试按名称获取绑定到当前实例的 <see cref="DefaultExecutor"/> 内置判定规则委托。
        /// </summary>
        public bool TryGetDefaultRule(string defaultRuleName, out Func<bool> rule)
        {
            if (!string.IsNullOrWhiteSpace(defaultRuleName) &&
                DefaultRuleMap.TryGetValue(defaultRuleName, out Func<LuaDeckExecutor, bool> entry))
            {
                rule = () => entry(this);
                return true;
            }

            rule = null;
            return false;
        }

        // 暴露带参数的 DefaultExecutor 常用查询方法，方便 Lua 直接调用。
        public bool CheckCardIsNegated(ClientCard card) => DefaultCheckWhetherCardIsNegated(card);
        public bool CheckCardIdIsNegated(int cardId) => DefaultCheckWhetherCardIdIsNegated(cardId);
        public int GetCalledByTheGraveCount(int cardId) => GetCalledbytheGraveIdCount(cardId);
        public bool CheckSpellActivateWillBeNegated(ClientCard card) => DefaultCheckWhetherSpellActivateWillBeNegated(card);

        /// <summary>
        /// 供 Lua 脚本按规范通过 WindBot 日志（底层统一转发到 AppLog）输出日志，不直接写控制台。
        /// </summary>
        public void LogInfo(string message) => Logger.WriteLine($"[LuaDeck:{ScriptChunkName}] {message}");
        public void LogDebug(string message) => Logger.DebugWriteLine($"[LuaDeck:{ScriptChunkName}] {message}");
        public void LogError(string message) => Logger.WriteErrorLine($"[LuaDeck:{ScriptChunkName}] {message}");

        public override int OnRockPaperScissors()
        {
            if (_onRockPaperScissors != null &&
                TryInvokeInt(_onRockPaperScissors, LuaDeckHookNames.OnRockPaperScissors, out int result, this))
            {
                return result;
            }

            return base.OnRockPaperScissors();
        }

        public override bool OnSelectHand()
        {
            if (_onSelectHand != null &&
                TryInvokeBool(_onSelectHand, LuaDeckHookNames.OnSelectHand, out bool result, this))
            {
                return result;
            }

            return base.OnSelectHand();
        }

        public override BattlePhaseAction OnBattle(IList<ClientCard> attackers, IList<ClientCard> defenders)
        {
            if (_onBattle != null &&
                TryInvokeObject(_onBattle, LuaDeckHookNames.OnBattle, out object result, this, attackers, defenders) &&
                result is BattlePhaseAction action)
            {
                return action;
            }

            return base.OnBattle(attackers, defenders);
        }

        public override ClientCard OnSelectAttacker(IList<ClientCard> attackers, IList<ClientCard> defenders)
        {
            if (_onSelectAttacker != null &&
                TryInvokeObject(_onSelectAttacker, LuaDeckHookNames.OnSelectAttacker, out object result, this, attackers, defenders) &&
                result is ClientCard card)
            {
                return card;
            }

            return base.OnSelectAttacker(attackers, defenders);
        }

        public override BattlePhaseAction OnSelectAttackTarget(ClientCard attacker, IList<ClientCard> defenders)
        {
            if (_onSelectAttackTarget != null &&
                TryInvokeObject(_onSelectAttackTarget, LuaDeckHookNames.OnSelectAttackTarget, out object result, this, attacker, defenders) &&
                result is BattlePhaseAction action)
            {
                return action;
            }

            return base.OnSelectAttackTarget(attacker, defenders);
        }

        public override bool OnPreBattleBetween(ClientCard attacker, ClientCard defender)
        {
            if (_onPreBattleBetween != null &&
                TryInvokeBool(_onPreBattleBetween, LuaDeckHookNames.OnPreBattleBetween, out bool result, this, attacker, defender))
            {
                return result;
            }

            return base.OnPreBattleBetween(attacker, defender);
        }

        public override bool OnPreActivate(ClientCard card)
        {
            if (_onPreActivate != null &&
                TryInvokeBool(_onPreActivate, LuaDeckHookNames.OnPreActivate, out bool result, this, card))
            {
                return result;
            }

            return base.OnPreActivate(card);
        }

        public override void OnChaining(int player, ClientCard card)
        {
            base.OnChaining(player, card);
            if (_onChaining != null)
                InvokeVoid(_onChaining, LuaDeckHookNames.OnChaining, this, player, card);
        }

        public override void OnChainSolved(int chainIndex)
        {
            base.OnChainSolved(chainIndex);
            if (_onChainSolved != null)
                InvokeVoid(_onChainSolved, LuaDeckHookNames.OnChainSolved, this, chainIndex);
        }

        public override void OnChainEnd()
        {
            base.OnChainEnd();
            if (_onChainEnd != null)
                InvokeVoid(_onChainEnd, LuaDeckHookNames.OnChainEnd, this);
        }

        public override void OnReceivingAnnouce(int player, int data)
        {
            base.OnReceivingAnnouce(player, data);
            if (_onReceivingAnnouce != null)
                InvokeVoid(_onReceivingAnnouce, LuaDeckHookNames.OnReceivingAnnouce, this, player, data);
        }

        public override void OnNewPhase()
        {
            base.OnNewPhase();
            if (_onNewPhase != null)
                InvokeVoid(_onNewPhase, LuaDeckHookNames.OnNewPhase, this);
        }

        public override void OnNewTurn()
        {
            base.OnNewTurn();
            if (_onNewTurn != null)
                InvokeVoid(_onNewTurn, LuaDeckHookNames.OnNewTurn, this);
        }

        public override void OnDraw(int player)
        {
            base.OnDraw(player);
            if (_onDraw != null)
                InvokeVoid(_onDraw, LuaDeckHookNames.OnDraw, this, player);
        }

        public override void OnPlayerHint(int player, int hintType, int description)
        {
            base.OnPlayerHint(player, hintType, description);
            if (_onPlayerHint != null)
                InvokeVoid(_onPlayerHint, LuaDeckHookNames.OnPlayerHint, this, player, hintType, description);
        }

        public override void OnHintZone(int player, int zone)
        {
            base.OnHintZone(player, zone);
            if (_onHintZone != null)
                InvokeVoid(_onHintZone, LuaDeckHookNames.OnHintZone, this, player, zone);
        }

        public override void OnMove(ClientCard card, int previousControler, int previousLocation, int currentControler, int currentLocation)
        {
            base.OnMove(card, previousControler, previousLocation, currentControler, currentLocation);
            if (_onMove != null)
            {
                InvokeVoid(
                    _onMove,
                    LuaDeckHookNames.OnMove,
                    this,
                    card,
                    previousControler,
                    previousLocation,
                    currentControler,
                    currentLocation);
            }
        }

        public override void OnPosChange(ClientCard card, int previousPosition, int currentPosition)
        {
            base.OnPosChange(card, previousPosition, currentPosition);
            if (_onPosChange != null)
                InvokeVoid(_onPosChange, LuaDeckHookNames.OnPosChange, this, card, previousPosition, currentPosition);
        }

        public override IList<ClientCard> OnSelectCard(IList<ClientCard> cards, int min, int max, int hint, bool cancelable)
        {
            if (_onSelectCard != null &&
                TryInvokeCardList(
                    _onSelectCard,
                    LuaDeckHookNames.OnSelectCard,
                    out IList<ClientCard> result,
                    this,
                    cards,
                    min,
                    max,
                    hint,
                    cancelable))
            {
                return result;
            }

            return base.OnSelectCard(cards, min, max, hint, cancelable);
        }

        public override IList<ClientCard> OnSelectTribute(IList<ClientCard> cards, int min, int max, int hint, bool cancelable)
        {
            if (_onSelectTribute != null &&
                TryInvokeCardList(
                    _onSelectTribute,
                    LuaDeckHookNames.OnSelectTribute,
                    out IList<ClientCard> result,
                    this,
                    cards,
                    min,
                    max,
                    hint,
                    cancelable))
            {
                return result;
            }

            return base.OnSelectTribute(cards, min, max, hint, cancelable);
        }

        public override IList<ClientCard> OnSelectSum(
            IList<ClientCard> cards,
            IList<ClientCard> mandatoryCards,
            int sum,
            int min,
            int max,
            int hint,
            bool exactEqual)
        {
            if (_onSelectSum != null &&
                TryInvokeCardList(
                    _onSelectSum,
                    LuaDeckHookNames.OnSelectSum,
                    out IList<ClientCard> result,
                    this,
                    cards,
                    mandatoryCards,
                    sum,
                    min,
                    max,
                    hint,
                    exactEqual))
            {
                return result;
            }

            return base.OnSelectSum(cards, mandatoryCards, sum, min, max, hint, exactEqual);
        }

        public override IList<ClientCard> OnSelectFusionMaterial(IList<ClientCard> cards, int min, int max)
        {
            if (_onSelectFusionMaterial != null &&
                TryInvokeCardList(
                    _onSelectFusionMaterial,
                    LuaDeckHookNames.OnSelectFusionMaterial,
                    out IList<ClientCard> result,
                    this,
                    cards,
                    min,
                    max))
            {
                return result;
            }

            return base.OnSelectFusionMaterial(cards, min, max);
        }

        public override IList<ClientCard> OnSelectSynchroMaterial(
            IList<ClientCard> cards,
            IList<ClientCard> mandatoryCards,
            int sum,
            int min,
            int max)
        {
            if (_onSelectSynchroMaterial != null &&
                TryInvokeCardList(
                    _onSelectSynchroMaterial,
                    LuaDeckHookNames.OnSelectSynchroMaterial,
                    out IList<ClientCard> result,
                    this,
                    cards,
                    mandatoryCards,
                    sum,
                    min,
                    max))
            {
                return result;
            }

            return base.OnSelectSynchroMaterial(cards, mandatoryCards, sum, min, max);
        }

        public override IList<ClientCard> OnSelectXyzMaterial(IList<ClientCard> cards, int min, int max)
        {
            if (_onSelectXyzMaterial != null &&
                TryInvokeCardList(
                    _onSelectXyzMaterial,
                    LuaDeckHookNames.OnSelectXyzMaterial,
                    out IList<ClientCard> result,
                    this,
                    cards,
                    min,
                    max))
            {
                return result;
            }

            return base.OnSelectXyzMaterial(cards, min, max);
        }

        public override IList<ClientCard> OnSelectLinkMaterial(IList<ClientCard> cards, int min, int max)
        {
            if (_onSelectLinkMaterial != null &&
                TryInvokeCardList(
                    _onSelectLinkMaterial,
                    LuaDeckHookNames.OnSelectLinkMaterial,
                    out IList<ClientCard> result,
                    this,
                    cards,
                    min,
                    max))
            {
                return result;
            }

            return base.OnSelectLinkMaterial(cards, min, max);
        }

        public override IList<ClientCard> OnSelectRitualTribute(
            IList<ClientCard> cards,
            IList<ClientCard> mandatoryCards,
            int sum,
            int min,
            int max,
            bool exactEqual)
        {
            if (_onSelectRitualTribute != null &&
                TryInvokeCardList(
                    _onSelectRitualTribute,
                    LuaDeckHookNames.OnSelectRitualTribute,
                    out IList<ClientCard> result,
                    this,
                    cards,
                    mandatoryCards,
                    sum,
                    min,
                    max,
                    exactEqual))
            {
                return result;
            }

            return base.OnSelectRitualTribute(cards, mandatoryCards, sum, min, max, exactEqual);
        }

        public override IList<ClientCard> OnSelectPendulumSummon(IList<ClientCard> cards, int min, int max)
        {
            if (_onSelectPendulumSummon != null &&
                TryInvokeCardList(
                    _onSelectPendulumSummon,
                    LuaDeckHookNames.OnSelectPendulumSummon,
                    out IList<ClientCard> result,
                    this,
                    cards,
                    min,
                    max))
            {
                return result;
            }

            return base.OnSelectPendulumSummon(cards, min, max);
        }

        public override IList<ClientCard> OnCardSorting(IList<ClientCard> cards)
        {
            if (_onCardSorting != null &&
                TryInvokeCardList(_onCardSorting, LuaDeckHookNames.OnCardSorting, out IList<ClientCard> result, this, cards))
            {
                return result;
            }

            return base.OnCardSorting(cards);
        }

        public override void OnSelectChain(IList<ClientCard> cards)
        {
            base.OnSelectChain(cards);
            if (_onSelectChain != null)
                InvokeVoid(_onSelectChain, LuaDeckHookNames.OnSelectChain, this, cards);
        }

        public override bool OnSelectYesNo(int desc)
        {
            if (_onSelectYesNo != null &&
                TryInvokeBool(_onSelectYesNo, LuaDeckHookNames.OnSelectYesNo, out bool result, this, desc))
            {
                return result;
            }

            return base.OnSelectYesNo(desc);
        }

        public override int OnSelectOption(IList<int> options)
        {
            if (_onSelectOption != null &&
                TryInvokeInt(_onSelectOption, LuaDeckHookNames.OnSelectOption, out int result, this, options))
            {
                return result;
            }

            return base.OnSelectOption(options);
        }

        public override int OnSelectPlace(int cardId, int player, CardLocation location, int available)
        {
            if (_onSelectPlace != null &&
                TryInvokeInt(
                    _onSelectPlace,
                    LuaDeckHookNames.OnSelectPlace,
                    out int result,
                    this,
                    cardId,
                    player,
                    (int)location,
                    available))
            {
                return result;
            }

            return base.OnSelectPlace(cardId, player, location, available);
        }

        public override uint OnSelectDisfield(int hint, int count, uint available)
        {
            if (_onSelectDisfield != null &&
                TryInvokeObject(_onSelectDisfield, LuaDeckHookNames.OnSelectDisfield, out object raw, this, hint, count, available) &&
                LuaValueConverter.TryToUInt32(raw, out uint result))
            {
                return result;
            }

            return base.OnSelectDisfield(hint, count, available);
        }

        public override CardPosition OnSelectPosition(int cardId, IList<CardPosition> positions)
        {
            if (_onSelectPosition != null &&
                TryInvokeObject(_onSelectPosition, LuaDeckHookNames.OnSelectPosition, out object raw, this, cardId, positions) &&
                LuaValueConverter.TryToEnum(raw, out CardPosition position))
            {
                return position;
            }

            return base.OnSelectPosition(cardId, positions);
        }

        public override bool OnSelectBattleReplay()
        {
            if (_onSelectBattleReplay != null &&
                TryInvokeBool(_onSelectBattleReplay, LuaDeckHookNames.OnSelectBattleReplay, out bool result, this))
            {
                return result;
            }

            return base.OnSelectBattleReplay();
        }

        public override bool OnSelectBattleDirectAttack(ClientCard attacker, bool preselectedAnswer)
        {
            if (_onSelectBattleDirectAttack != null &&
                TryInvokeBool(
                    _onSelectBattleDirectAttack,
                    LuaDeckHookNames.OnSelectBattleDirectAttack,
                    out bool result,
                    this,
                    attacker,
                    preselectedAnswer))
            {
                return result;
            }

            return base.OnSelectBattleDirectAttack(attacker, preselectedAnswer);
        }

        public override bool OnSelectMonsterSummonOrSet(ClientCard card)
        {
            if (_onSelectMonsterSummonOrSet != null &&
                TryInvokeBool(_onSelectMonsterSummonOrSet, LuaDeckHookNames.OnSelectMonsterSummonOrSet, out bool result, this, card))
            {
                return result;
            }

            return base.OnSelectMonsterSummonOrSet(card);
        }

        public override int OnAnnounceCard(IList<int> avail)
        {
            if (_onAnnounceCard != null &&
                TryInvokeInt(_onAnnounceCard, LuaDeckHookNames.OnAnnounceCard, out int result, this, avail))
            {
                return result;
            }

            return base.OnAnnounceCard(avail);
        }

        public override void OnSpSummoned()
        {
            base.OnSpSummoned();
            if (_onSpSummoned != null)
                InvokeVoid(_onSpSummoned, LuaDeckHookNames.OnSpSummoned, this);
        }

        public override void OnSpSummoning()
        {
            base.OnSpSummoning();
            if (_onSpSummoning != null)
                InvokeVoid(_onSpSummoning, LuaDeckHookNames.OnSpSummoning, this);
        }

        public override void OnSummoning()
        {
            base.OnSummoning();
            if (_onSummoning != null)
                InvokeVoid(_onSummoning, LuaDeckHookNames.OnSummoning, this);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            try
            {
                if (_onDispose != null)
                    InvokeVoid(_onDispose, LuaDeckHookNames.OnDispose, this);
            }
            finally
            {
                _disposed = true;
                Executors.Clear();

                for (int i = _ownedFunctions.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        _ownedFunctions[i]?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Logger.WriteErrorLine($"[LuaDeck:{ScriptChunkName}] Dispose function error: {ex}");
                    }
                }
                _ownedFunctions.Clear();

                try
                {
                    _moduleTable?.Dispose();
                }
                catch (Exception ex)
                {
                    Logger.WriteErrorLine($"[LuaDeck:{ScriptChunkName}] Dispose module table error: {ex}");
                }

                try
                {
                    _context?.Dispose();
                }
                catch (Exception ex)
                {
                    Logger.WriteErrorLine($"[LuaDeck:{ScriptChunkName}] Dispose context error: {ex}");
                }
            }
        }

        private void BindHostGlobals()
        {
            _context.SetGlobal("Self", this);
            _context.SetGlobal("Executor", this);
            _context.SetGlobal("Duel", Duel);
            _context.SetGlobal("AI", AI);
            _context.SetGlobal("Util", Util);
            _context.SetGlobal("Bot", Bot);
            _context.SetGlobal("Enemy", Enemy);

            _context.SetGlobal("ExecutorType", ExecutorTypeConstants);
            _context.SetGlobal("CardLocation", CardLocationConstants);
            _context.SetGlobal("CardPosition", CardPositionConstants);
            _context.SetGlobal("CardType", CardTypeConstants);
            _context.SetGlobal("CardAttribute", CardAttributeConstants);
            _context.SetGlobal("CardRace", CardRaceConstants);
            _context.SetGlobal("DuelPhase", DuelPhaseConstants);
        }

        internal void QueueDslExecutorRule(ExecutorType type, int cardId, object callback)
        {
            ThrowIfDisposed();
            if (_isLoadingScript)
            {
                _pendingDslRules.Add(new PendingDslRule(PendingDslRuleKind.LuaExecutor, type, cardId, callback, null));
                return;
            }

            AddLuaExecutor(type, cardId, callback);
        }

        internal void QueueDslDefaultRule(ExecutorType type, int cardId, string defaultRuleName)
        {
            ThrowIfDisposed();
            if (_isLoadingScript)
            {
                _pendingDslRules.Add(new PendingDslRule(PendingDslRuleKind.DefaultRule, type, cardId, null, defaultRuleName));
                return;
            }

            AddDefaultExecutor(type, cardId, defaultRuleName);
        }

        internal void QueueDslNoExecutorRule(ExecutorType type)
        {
            ThrowIfDisposed();
            if (_isLoadingScript)
            {
                _pendingDslRules.Add(new PendingDslRule(PendingDslRuleKind.DefaultNoExecutor, type, -1, null, null));
                return;
            }

            AddExecutor(type);
        }

        private void FlushPendingDslRules()
        {
            for (int i = 0; i < _pendingDslRules.Count; i++)
            {
                PendingDslRule rule = _pendingDslRules[i];
                switch (rule.Kind)
                {
                    case PendingDslRuleKind.LuaExecutor:
                        AddLuaExecutor(rule.Type, rule.CardId, rule.Callback);
                        break;
                    case PendingDslRuleKind.DefaultRule:
                        AddDefaultExecutor(rule.Type, rule.CardId, rule.DefaultRuleName);
                        break;
                    case PendingDslRuleKind.DefaultNoExecutor:
                        AddExecutor(rule.Type);
                        break;
                }
            }

            _pendingDslRules.Clear();
        }

        private ILuaFunction ResolveHook(string name)
        {
            if (Dsl != null && Dsl.RegisteredHooks.TryGetValue(name, out object dslCallback) && dslCallback != null)
            {
                ILuaFunction dslFunction = ResolveHookObject(dslCallback, name);
                if (dslFunction != null)
                    return dslFunction;
            }

            return ResolveNamedFunction(name);
        }

        private ILuaFunction ResolveNamedFunction(string name)
        {
            ILuaFunction function = null;

            if (_moduleTable != null && _moduleTable.TryGetFunction(name, out ILuaFunction moduleFunction) && moduleFunction != null)
                function = moduleFunction;
            else if (_context.TryGetFunction(name, out ILuaFunction globalFunction) && globalFunction != null)
                function = globalFunction;

            if (function != null)
                TrackOwnedFunction(function);

            return function;
        }

        private ILuaFunction ResolveHookObject(object callback, string hookName)
        {
            if (callback is ILuaFunction luaFunction)
            {
                TrackOwnedFunction(luaFunction);
                return luaFunction;
            }

            if (callback is string functionName)
            {
                ILuaFunction resolved = ResolveNamedFunction(functionName);
                if (resolved == null)
                {
                    throw new InvalidOperationException(
                        $"Lua hook callback function '{functionName}' registered for '{hookName}' was not found in '{ScriptChunkName}'.");
                }

                return resolved;
            }

            if (callback is Delegate managedDelegate)
            {
                ILuaFunction adapted = new DelegateLuaFunction(managedDelegate);
                TrackOwnedFunction(adapted);
                return adapted;
            }

            if (_context.TryWrapFunction(callback, out ILuaFunction wrapped) && wrapped != null)
            {
                TrackOwnedFunction(wrapped);
                return wrapped;
            }

            throw new ArgumentException(
                $"Unsupported Lua hook callback of type '{callback.GetType().FullName}' for '{hookName}'. " +
                "Pass an ILuaFunction, Delegate, function name string, or implement ILuaScriptContext.TryWrapFunction.",
                nameof(callback));
        }

        private Func<bool> ResolveExecutorCallback(object callback)
        {
            if (callback == null)
                return null;

            if (callback is Func<bool> directDelegate)
                return directDelegate;

            if (callback is Func<LuaDeckExecutor, bool> selfDelegate)
                return () => selfDelegate(this);

            if (callback is ILuaFunction luaFunction)
            {
                TrackOwnedFunction(luaFunction);
                return CreateLuaRuleDelegate(luaFunction, "<inline>");
            }

            if (callback is string functionOrRuleName)
            {
                if (string.IsNullOrWhiteSpace(functionOrRuleName))
                    return null;

                ILuaFunction namedFunction = ResolveNamedFunction(functionOrRuleName);
                if (namedFunction != null)
                    return CreateLuaRuleDelegate(namedFunction, functionOrRuleName);

                if (TryGetDefaultRule(functionOrRuleName, out Func<bool> defaultRule))
                    return defaultRule;

                throw new InvalidOperationException(
                    $"Lua callback or DefaultExecutor rule '{functionOrRuleName}' was not found in '{ScriptChunkName}'.");
            }

            if (_context.TryWrapFunction(callback, out ILuaFunction wrapped) && wrapped != null)
            {
                TrackOwnedFunction(wrapped);
                return CreateLuaRuleDelegate(wrapped, "<wrapped>");
            }

            throw new ArgumentException(
                $"Unsupported Lua executor callback of type '{callback.GetType().FullName}'. " +
                "Pass a Func<bool>, ILuaFunction, function/default-rule name string, or implement ILuaScriptContext.TryWrapFunction.",
                nameof(callback));
        }

        private Func<bool> CreateLuaRuleDelegate(ILuaFunction function, string label)
        {
            return () =>
            {
                if (_disposed)
                    return false;

                return TryInvokeBool(function, label, out bool result, this) && result;
            };
        }

        private void TrackOwnedFunction(ILuaFunction function)
        {
            if (!_ownedFunctions.Contains(function))
                _ownedFunctions.Add(function);
        }

        private void InvokeVoid(ILuaFunction function, string hookName, object arg1)
        {
            if (_disposed)
                return;

            try
            {
                function.Call(arg1);
            }
            catch (Exception ex)
            {
                HandleScriptError(hookName, ex);
            }
        }

        private void InvokeVoid(ILuaFunction function, string hookName, object arg1, object arg2)
        {
            if (_disposed)
                return;

            try
            {
                function.Call(arg1, arg2);
            }
            catch (Exception ex)
            {
                HandleScriptError(hookName, ex);
            }
        }

        private void InvokeVoid(ILuaFunction function, string hookName, params object[] args)
        {
            if (_disposed)
                return;

            try
            {
                function.Call(args);
            }
            catch (Exception ex)
            {
                HandleScriptError(hookName, ex);
            }
        }

        private bool TryInvokeObject(ILuaFunction function, string hookName, out object result, object arg1)
        {
            result = null;
            if (_disposed)
                return false;

            try
            {
                result = function.Call(arg1);
                return result != null;
            }
            catch (Exception ex)
            {
                HandleScriptError(hookName, ex);
                return false;
            }
        }

        private bool TryInvokeObject(ILuaFunction function, string hookName, out object result, object arg1, object arg2)
        {
            result = null;
            if (_disposed)
                return false;

            try
            {
                result = function.Call(arg1, arg2);
                return result != null;
            }
            catch (Exception ex)
            {
                HandleScriptError(hookName, ex);
                return false;
            }
        }

        private bool TryInvokeObject(ILuaFunction function, string hookName, out object result, params object[] args)
        {
            result = null;
            if (_disposed)
                return false;

            try
            {
                result = function.Call(args);
                return result != null;
            }
            catch (Exception ex)
            {
                HandleScriptError(hookName, ex);
                return false;
            }
        }

        private bool TryInvokeBool(ILuaFunction function, string hookName, out bool result, object arg1)
        {
            if (TryInvokeObject(function, hookName, out object raw, arg1) &&
                LuaValueConverter.TryToBoolean(raw, out result))
            {
                return true;
            }

            result = false;
            return false;
        }

        private bool TryInvokeBool(ILuaFunction function, string hookName, out bool result, object arg1, object arg2)
        {
            if (TryInvokeObject(function, hookName, out object raw, arg1, arg2) &&
                LuaValueConverter.TryToBoolean(raw, out result))
            {
                return true;
            }

            result = false;
            return false;
        }

        private bool TryInvokeBool(ILuaFunction function, string hookName, out bool result, params object[] args)
        {
            if (TryInvokeObject(function, hookName, out object raw, args) &&
                LuaValueConverter.TryToBoolean(raw, out result))
            {
                return true;
            }

            result = false;
            return false;
        }

        private bool TryInvokeInt(ILuaFunction function, string hookName, out int result, object arg1)
        {
            if (TryInvokeObject(function, hookName, out object raw, arg1) &&
                LuaValueConverter.TryToInt32(raw, out result))
            {
                return true;
            }

            result = 0;
            return false;
        }

        private bool TryInvokeInt(ILuaFunction function, string hookName, out int result, object arg1, object arg2)
        {
            if (TryInvokeObject(function, hookName, out object raw, arg1, arg2) &&
                LuaValueConverter.TryToInt32(raw, out result))
            {
                return true;
            }

            result = 0;
            return false;
        }

        private bool TryInvokeInt(ILuaFunction function, string hookName, out int result, params object[] args)
        {
            if (TryInvokeObject(function, hookName, out object raw, args) &&
                LuaValueConverter.TryToInt32(raw, out result))
            {
                return true;
            }

            result = 0;
            return false;
        }

        private bool TryInvokeCardList(ILuaFunction function, string hookName, out IList<ClientCard> result, params object[] args)
        {
            if (TryInvokeObject(function, hookName, out object raw, args))
            {
                result = LuaValueConverter.TryToCardList(_context, raw);
                return result != null;
            }

            result = null;
            return false;
        }

        private void HandleScriptError(string hookName, Exception ex)
        {
            Logger.WriteErrorLine($"[LuaDeck:{ScriptChunkName}] Error in '{hookName}': {ex}");
            if (_throwOnScriptError)
                throw ex;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(LuaDeckExecutor));
        }

        private static IReadOnlyDictionary<string, int> CreateEnumMap<TEnum>() where TEnum : struct, Enum
        {
            string[] names = Enum.GetNames<TEnum>();
            Dictionary<string, int> map = new Dictionary<string, int>(names.Length, StringComparer.OrdinalIgnoreCase);
            foreach (string name in names)
                map[name] = Convert.ToInt32(Enum.Parse<TEnum>(name));
            return map.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        }

        private static FrozenDictionary<string, Func<LuaDeckExecutor, bool>> CreateDefaultRuleMap()
        {
            Dictionary<string, Func<LuaDeckExecutor, bool>> rules = new Dictionary<string, Func<LuaDeckExecutor, bool>>(StringComparer.OrdinalIgnoreCase)
            {
                [nameof(DefaultMysticalSpaceTyphoon)] = static e => e.DefaultMysticalSpaceTyphoon(),
                [nameof(DefaultCosmicCyclone)] = static e => e.DefaultCosmicCyclone(),
                [nameof(DefaultGalaxyCyclone)] = static e => e.DefaultGalaxyCyclone(),
                [nameof(DefaultBookOfMoon)] = static e => e.DefaultBookOfMoon(),
                [nameof(DefaultCompulsoryEvacuationDevice)] = static e => e.DefaultCompulsoryEvacuationDevice(),
                [nameof(DefaultCallOfTheHaunted)] = static e => e.DefaultCallOfTheHaunted(),
                [nameof(DefaultScapegoat)] = static e => e.DefaultScapegoat(),
                [nameof(DefaultMaxxC)] = static e => e.DefaultMaxxC(),
                [nameof(DefaultAshBlossomAndJoyousSpring)] = static e => e.DefaultAshBlossomAndJoyousSpring(),
                [nameof(DefaultGhostOgreAndSnowRabbit)] = static e => e.DefaultGhostOgreAndSnowRabbit(),
                [nameof(DefaultGhostBelleAndHauntedMansion)] = static e => e.DefaultGhostBelleAndHauntedMansion(),
                [nameof(DefaultEffectVeiler)] = static e => e.DefaultEffectVeiler(),
                [nameof(DefaultCalledByTheGrave)] = static e => e.DefaultCalledByTheGrave(),
                [nameof(DefaultInfiniteImpermanence)] = static e => e.DefaultInfiniteImpermanence(),
                [nameof(DefaultBreakthroughSkill)] = static e => e.DefaultBreakthroughSkill(),
                [nameof(DefaultDisableMonster)] = static e => e.DefaultDisableMonster(),
                [nameof(DefaultSolemnJudgment)] = static e => e.DefaultSolemnJudgment(),
                [nameof(DefaultSolemnWarning)] = static e => e.DefaultSolemnWarning(),
                [nameof(DefaultSolemnStrike)] = static e => e.DefaultSolemnStrike(),
                [nameof(DefaultOnlyHorusSpSummoning)] = static e => e.DefaultOnlyHorusSpSummoning(),
                [nameof(DefaultTorrentialTribute)] = static e => e.DefaultTorrentialTribute(),
                [nameof(DefaultHeavyStorm)] = static e => e.DefaultHeavyStorm(),
                [nameof(DefaultHarpiesFeatherDusterFirst)] = static e => e.DefaultHarpiesFeatherDusterFirst(),
                [nameof(DefaultHammerShot)] = static e => e.DefaultHammerShot(),
                [nameof(DefaultDarkHole)] = static e => e.DefaultDarkHole(),
                [nameof(DefaultRaigeki)] = static e => e.DefaultRaigeki(),
                [nameof(DefaultSmashingGround)] = static e => e.DefaultSmashingGround(),
                [nameof(DefaultPotOfDesires)] = static e => e.DefaultPotOfDesires(),
                [nameof(DefaultSpellSet)] = static e => e.DefaultSpellSet(),
                [nameof(DefaultMonsterSummon)] = static e => e.DefaultMonsterSummon(),
                [nameof(DefaultField)] = static e => e.DefaultField(),
                [nameof(DefaultMonsterRepos)] = static e => e.DefaultMonsterRepos(),
                [nameof(DefaultSpellWillBeNegated)] = static e => e.DefaultSpellWillBeNegated(),
                [nameof(DefaultTrapWillBeNegated)] = static e => e.DefaultTrapWillBeNegated(),
                [nameof(DefaultSpellMustSetFirst)] = static e => e.DefaultSpellMustSetFirst(),
                [nameof(DefaultOnBecomeTarget)] = static e => e.DefaultOnBecomeTarget(),
                [nameof(DefaultTrap)] = static e => e.DefaultTrap(),
                [nameof(DefaultUniqueTrap)] = static e => e.DefaultUniqueTrap(),
                [nameof(DefaultBottomlessTrapHole)] = static e => e.DefaultBottomlessTrapHole(),
                [nameof(UniqueFaceupSpell)] = static e => e.UniqueFaceupSpell(),
                [nameof(UniqueFaceupMonster)] = static e => e.UniqueFaceupMonster(),
                [nameof(DefaultDontChainMyself)] = static e => e.DefaultDontChainMyself(),
                [nameof(DefaultChickenGame)] = static e => e.DefaultChickenGame(),
                [nameof(DefaultAllureofDarkness)] = static e => e.DefaultAllureofDarkness(),
                [nameof(DefaultDimensionalBarrier)] = static e => e.DefaultDimensionalBarrier(),
                [nameof(DefaultInterruptedKaijuSlumber)] = static e => e.DefaultInterruptedKaijuSlumber(),
                [nameof(DefaultKaijuSpsummon)] = static e => e.DefaultKaijuSpsummon(),
                [nameof(DefaultNumberS39UtopiaTheLightningSummon)] = static e => e.DefaultNumberS39UtopiaTheLightningSummon(),
                [nameof(DefaultNumberS39UtopiaTheLightningEffect)] = static e => e.DefaultNumberS39UtopiaTheLightningEffect(),
                [nameof(DefaultEvilswarmExcitonKnightSummon)] = static e => e.DefaultEvilswarmExcitonKnightSummon(),
                [nameof(DefaultEvilswarmExcitonKnightEffect)] = static e => e.DefaultEvilswarmExcitonKnightEffect(),
                [nameof(DefaultStardustDragonSummon)] = static e => e.DefaultStardustDragonSummon(),
                [nameof(DefaultStardustDragonEffect)] = static e => e.DefaultStardustDragonEffect(),
                [nameof(DefaultCastelTheSkyblasterMusketeerSummon)] = static e => e.DefaultCastelTheSkyblasterMusketeerSummon(),
                [nameof(DefaultCastelTheSkyblasterMusketeerEffect)] = static e => e.DefaultCastelTheSkyblasterMusketeerEffect(),
                [nameof(DefaultScarlightRedDragonArchfiendSummon)] = static e => e.DefaultScarlightRedDragonArchfiendSummon(),
                [nameof(DefaultTimelordSummon)] = static e => e.DefaultTimelordSummon(),
                [nameof(DefaultScarlightRedDragonArchfiendEffect)] = static e => e.DefaultScarlightRedDragonArchfiendEffect(),
                [nameof(DefaultHonestEffect)] = static e => e.DefaultHonestEffect(),
                [nameof(DefaultVaylantzWorld_ShinraBansho)] = static e => e.DefaultVaylantzWorld_ShinraBansho(),
                [nameof(DefaultVaylantzWorld_KonigWissen)] = static e => e.DefaultVaylantzWorld_KonigWissen(),
                [nameof(DefaultSetForDiabellze)] = static e => e.DefaultSetForDiabellze(),
                [nameof(DefaultCheckWhetherBotWillBeBanished)] = static e => e.DefaultCheckWhetherBotWillBeBanished(),
                [nameof(DefaultCheckWhetherNumber41IsActive)] = static e => e.DefaultCheckWhetherNumber41IsActive(),
                [nameof(DefaultCheckAllAvailableSpellColumnNegated)] = static e => e.DefaultCheckAllAvailableSpellColumnNegated(),
                [nameof(DefaultCheckWhetherBotCanSearch)] = static e => e.DefaultCheckWhetherBotCanSearch(),
                [nameof(DefaultCheckWhetherBotCanDraw)] = static e => e.DefaultCheckWhetherBotCanDraw(),
                [nameof(DefaultCheckWhetherEnemyCanDraw)] = static e => e.DefaultCheckWhetherEnemyCanDraw()
            };

            return rules.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        }

        private enum PendingDslRuleKind
        {
            LuaExecutor,
            DefaultRule,
            DefaultNoExecutor
        }

        private readonly record struct PendingDslRule(
            PendingDslRuleKind Kind,
            ExecutorType Type,
            int CardId,
            object Callback,
            string DefaultRuleName);

        private sealed class DelegateLuaFunction : ILuaFunction
        {
            private readonly Delegate _delegate;
            private readonly int _paramCount;

            public DelegateLuaFunction(Delegate managedDelegate)
            {
                _delegate = managedDelegate;
                _paramCount = managedDelegate.Method.GetParameters().Length;
            }

            public object Call(params object[] args)
            {
                if (_paramCount == 0)
                    return _delegate.DynamicInvoke(Array.Empty<object>());

                if (args != null && args.Length == _paramCount)
                    return _delegate.DynamicInvoke(args);

                object[] invokeArgs = new object[_paramCount];
                if (args != null)
                {
                    int copyCount = Math.Min(args.Length, _paramCount);
                    Array.Copy(args, invokeArgs, copyCount);
                }

                return _delegate.DynamicInvoke(invokeArgs);
            }

            public void Dispose()
            {
            }
        }
    }
}
