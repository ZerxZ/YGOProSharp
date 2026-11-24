---@meta
---@diagnostic disable: lowercase-global, missing-return, unused-local, duplicate-set-field

--- YGOProSharp.WindBot Lua 卡组执行器（Decks/Lua/*.lua）开发者类型声明（LuaLS / EmmyLua）。
---
--- 面向 xmake 声明式 DSL、模块返回式（`return M`）与全局函数式三种写法，
--- 兼容 XLua / NLua / MoonSharp / Lua-CSharp。
--- 仅用于编辑器补全与静态类型检查，不参与运行时加载。

-- =============================================================================
-- 枚举常量表（由 LuaDeckExecutor.BindHostGlobals 自动注入）
-- =============================================================================

---@alias ExecutorTypeName
---| "Summon"
---| "SpSummon"
---| "Repos"
---| "MonsterSet"
---| "SpellSet"
---| "Activate"
---| "SummonOrSet"
---| "GoToBattlePhase"
---| "GoToMainPhase2"
---| "GoToEndPhase"
---| "Surrender"

---@enum ExecutorType
ExecutorType = {
    Summon = 0,
    SpSummon = 1,
    Repos = 2,
    MonsterSet = 3,
    SpellSet = 4,
    Activate = 5,
    SummonOrSet = 6,
    GoToBattlePhase = 7,
    GoToMainPhase2 = 8,
    GoToEndPhase = 9,
    Surrender = 10,
}

---@enum CardLocation
CardLocation = {
    Deck = 0x01,
    Hand = 0x02,
    MonsterZone = 0x04,
    SpellZone = 0x08,
    Grave = 0x10,
    Removed = 0x20,
    Extra = 0x40,
    Overlay = 0x80,
    Onfield = 0x0C,
    FieldZone = 0x100,
    PendulumZone = 0x200,
}

---@enum CardPosition
CardPosition = {
    FaceUpAttack = 0x1,
    FaceDownAttack = 0x2,
    FaceUpDefence = 0x4,
    FaceDownDefence = 0x8,
    FaceUp = 0x5,
    FaceDown = 0xA,
    Attack = 0x3,
    Defence = 0xC,
}

---@enum CardType
CardType = {
    Monster = 0x1,
    Spell = 0x2,
    Trap = 0x4,
    Normal = 0x10,
    Effect = 0x20,
    Fusion = 0x40,
    Ritual = 0x80,
    TrapMonster = 0x100,
    Spirit = 0x200,
    Union = 0x400,
    Dual = 0x800,
    Tuner = 0x1000,
    Synchro = 0x2000,
    Token = 0x4000,
    QuickPlay = 0x10000,
    Continuous = 0x20000,
    Equip = 0x40000,
    Field = 0x80000,
    Counter = 0x100000,
    Flip = 0x200000,
    Toon = 0x400000,
    Xyz = 0x800000,
    Pendulum = 0x1000000,
    SpSummon = 0x2000000,
    Link = 0x4000000,
}

---@enum CardAttribute
CardAttribute = {
    Earth = 0x01,
    Water = 0x02,
    Fire = 0x04,
    Wind = 0x08,
    Light = 0x10,
    Dark = 0x20,
    Divine = 0x40,
}

---@enum CardRace
CardRace = {
    Warrior = 0x1,
    SpellCaster = 0x2,
    Fairy = 0x4,
    Fiend = 0x8,
    Zombie = 0x10,
    Machine = 0x20,
    Aqua = 0x40,
    Pyro = 0x80,
    Rock = 0x100,
    WindBeast = 0x200,
    Plant = 0x400,
    Insect = 0x800,
    Thunder = 0x1000,
    Dragon = 0x2000,
    Beast = 0x4000,
    BeastWarrior = 0x8000,
    Dinosaur = 0x10000,
    Fish = 0x20000,
    SeaSerpent = 0x40000,
    Reptile = 0x80000,
    Psycho = 0x100000,
    DivineBeast = 0x200000,
    Wyrm = 0x800000,
    Cyberse = 0x1000000,
}

---@enum DuelPhase
DuelPhase = {
    Draw = 0x01,
    Standby = 0x02,
    Main1 = 0x04,
    BattleStart = 0x08,
    BattleStep = 0x10,
    Damage = 0x20,
    DamageCal = 0x40,
    Battle = 0x80,
    Main2 = 0x100,
    End = 0x200,
}

---@alias WindBotExecutorType ExecutorType|ExecutorTypeName|integer

---@alias DefaultRuleName
---| "DefaultMysticalSpaceTyphoon"
---| "DefaultCosmicCyclone"
---| "DefaultGalaxyCyclone"
---| "DefaultBookOfMoon"
---| "DefaultCompulsoryEvacuationDevice"
---| "DefaultCallOfTheHaunted"
---| "DefaultScapegoat"
---| "DefaultMaxxC"
---| "DefaultAshBlossomAndJoyousSpring"
---| "DefaultGhostOgreAndSnowRabbit"
---| "DefaultGhostBelleAndHauntedMansion"
---| "DefaultEffectVeiler"
---| "DefaultCalledByTheGrave"
---| "DefaultInfiniteImpermanence"
---| "DefaultBreakthroughSkill"
---| "DefaultDisableMonster"
---| "DefaultSolemnJudgment"
---| "DefaultSolemnWarning"
---| "DefaultSolemnStrike"
---| "DefaultOnlyHorusSpSummoning"
---| "DefaultTorrentialTribute"
---| "DefaultHeavyStorm"
---| "DefaultHarpiesFeatherDusterFirst"
---| "DefaultHammerShot"
---| "DefaultDarkHole"
---| "DefaultRaigeki"
---| "DefaultSmashingGround"
---| "DefaultPotOfDesires"
---| "DefaultSpellSet"
---| "DefaultMonsterSummon"
---| "DefaultField"
---| "DefaultMonsterRepos"
---| "DefaultSpellWillBeNegated"
---| "DefaultTrapWillBeNegated"
---| "DefaultSpellMustSetFirst"
---| "DefaultOnBecomeTarget"
---| "DefaultTrap"
---| "DefaultUniqueTrap"
---| "DefaultBottomlessTrapHole"
---| "UniqueFaceupSpell"
---| "UniqueFaceupMonster"
---| "DefaultDontChainMyself"
---| "DefaultChickenGame"
---| "DefaultAllureofDarkness"
---| "DefaultDimensionalBarrier"
---| "DefaultInterruptedKaijuSlumber"
---| "DefaultKaijuSpsummon"
---| "DefaultNumberS39UtopiaTheLightningSummon"
---| "DefaultNumberS39UtopiaTheLightningEffect"
---| "DefaultEvilswarmExcitonKnightSummon"
---| "DefaultEvilswarmExcitonKnightEffect"
---| "DefaultStardustDragonSummon"
---| "DefaultStardustDragonEffect"
---| "DefaultCastelTheSkyblasterMusketeerSummon"
---| "DefaultCastelTheSkyblasterMusketeerEffect"
---| "DefaultScarlightRedDragonArchfiendSummon"
---| "DefaultTimelordSummon"
---| "DefaultScarlightRedDragonArchfiendEffect"
---| "DefaultHonestEffect"
---| "DefaultVaylantzWorld_ShinraBansho"
---| "DefaultVaylantzWorld_KonigWissen"
---| "DefaultSetForDiabellze"
---| "DefaultCheckWhetherBotWillBeBanished"
---| "DefaultCheckWhetherNumber41IsActive"
---| "DefaultCheckAllAvailableSpellColumnNegated"
---| "DefaultCheckWhetherBotCanSearch"
---| "DefaultCheckWhetherBotCanDraw"
---| "DefaultCheckWhetherEnemyCanDraw"

-- =============================================================================
-- C# 宿主集合与核心领域对象（0-based 下标访问，兼容 CLR IList<T>）
-- =============================================================================

---@class WindBotCardList
---@field Count integer 列表元素数量（注意：CLR `IList<ClientCard>` 使用 `0` 到 `Count - 1` 下标）。
---@field [integer] WindBotClientCard

---@class WindBotIntList
---@field Count integer
---@field [integer] integer

---@class WindBotBoolList
---@field Count integer
---@field [integer] boolean

---@class WindBotPositionList
---@field Count integer
---@field [integer] CardPosition

---@class WindBotBattlePhaseAction
---@field Action integer
---@field Index integer

---@class WindBotMainPhase
---@field SummonableCards WindBotCardList
---@field SpecialSummonableCards WindBotCardList
---@field ReposableCards WindBotCardList
---@field MonsterSetableCards WindBotCardList
---@field SpellSetableCards WindBotCardList
---@field ActivableCards WindBotCardList
---@field ActivableDescs WindBotIntList
---@field CanBattlePhase boolean
---@field CanEndPhase boolean

---@class WindBotBattlePhase
---@field AttackableCards WindBotCardList
---@field ActivableCards WindBotCardList
---@field ActivableDescs WindBotIntList
---@field CanMainPhase2 boolean
---@field CanEndPhase boolean

---@class WindBotChainInfo
---@field RelatedCard WindBotClientCard|nil
---@field ActivatePlayer integer
---@field ActivateLocation integer
---@field ActivateDescription integer
---@field ActivateId integer

---@class WindBotClientCard
---@field Id integer 当前卡号。
---@field Name string|nil 卡名。
---@field Position integer 表示形式位掩码。
---@field Sequence integer 区域序号。
---@field Location CardLocation 当前区域。
---@field LastLocation CardLocation 上一次所在区域。
---@field Alias integer 别名卡号。
---@field Level integer 等级。
---@field Rank integer 阶级。
---@field Type integer 种类位掩码。
---@field Attribute integer 属性位掩码。
---@field Race integer 种族位掩码。
---@field Attack integer 当前攻击力。
---@field Defense integer 当前守备力。
---@field LScale integer 左灵摆刻度。
---@field RScale integer 右灵摆刻度。
---@field LinkCount integer 连接值。
---@field LinkMarker integer 连接标记位掩码。
---@field BaseAttack integer 原本攻击力。
---@field BaseDefense integer 原本守备力。
---@field RealPower integer 评估战力。
---@field Overlays WindBotIntList 超量素材卡号列表。
---@field Owner integer 原本持有者（`0` 己方，`1` 对方）。
---@field Controller integer 当前控制者（`0` 己方，`1` 对方）。
---@field Disabled integer 是否被无效（非 0 表示被无效）。
---@field CanDirectAttack boolean
---@field Attacked boolean
---@field IsLastAttacker boolean
---@field IsSpecialSummoned boolean
---@field EquipCards WindBotCardList
---@field EquipTarget WindBotClientCard|nil
---@field OwnTargets WindBotCardList
---@field TargetCards WindBotCardList
local WindBotClientCard = {}

---@param type CardType|integer
---@return boolean
function WindBotClientCard:HasType(type) end
---@param position CardPosition|integer
---@return boolean
function WindBotClientCard:HasPosition(position) end
---@param attribute CardAttribute|integer
---@return boolean
function WindBotClientCard:HasAttribute(attribute) end
---@param race CardRace|integer
---@return boolean
function WindBotClientCard:HasRace(race) end
---@param setcode integer
---@return boolean
function WindBotClientCard:HasSetcode(setcode) end
---@return boolean
function WindBotClientCard:IsMonster() end
---@return boolean
function WindBotClientCard:IsTuner() end
---@return boolean
function WindBotClientCard:IsSpell() end
---@return boolean
function WindBotClientCard:IsTrap() end
---@return boolean
function WindBotClientCard:IsExtraCard() end
---@return boolean
function WindBotClientCard:IsFaceup() end
---@return boolean
function WindBotClientCard:IsFacedown() end
---@return boolean
function WindBotClientCard:IsAttack() end
---@return boolean
function WindBotClientCard:IsDefense() end
---@return boolean
function WindBotClientCard:IsDisabled() end
---@return boolean
function WindBotClientCard:IsCanRevive() end
---@param id integer
---@return boolean
function WindBotClientCard:IsCode(id) end
---@param id integer
---@return boolean
function WindBotClientCard:IsOriginalCode(id) end
---@return boolean
function WindBotClientCard:IsOnField() end
---@param count? integer
---@param cardId? integer
---@return boolean
function WindBotClientCard:HasXyzMaterial(count, cardId) end
---@return integer
function WindBotClientCard:GetDefensePower() end
---@return integer
function WindBotClientCard:GetOriginCode() end
---@return integer
function WindBotClientCard:GetNonAltartCode() end

---@class WindBotClientField
---@field Hand WindBotCardList 手牌。
---@field MonsterZone WindBotCardList 怪兽区（7 格，0~4 主怪兽区，5~6 额外怪兽区，空格为 `nil`）。
---@field SpellZone WindBotCardList 魔陷区（8 格，空格为 `nil`）。
---@field Graveyard WindBotCardList 墓地。
---@field Banished WindBotCardList 除外区。
---@field Deck WindBotCardList 卡组。
---@field ExtraDeck WindBotCardList 额外卡组。
---@field LifePoints integer 当前生命值。
---@field BattlingMonster WindBotClientCard|nil
---@field UnderAttack boolean
local WindBotClientField = {}

---@return WindBotCardList
function WindBotClientField:GetMonsters() end
---@return WindBotCardList
function WindBotClientField:GetSpells() end
---@return integer
function WindBotClientField:GetMonsterCount() end
---@return integer
function WindBotClientField:GetSpellCount() end
---@param cardId integer
---@return boolean
function WindBotClientField:HasInHand(cardId) end
---@param cardId integer
---@return boolean
function WindBotClientField:HasInMonstersZone(cardId) end
---@param cardId integer
---@return boolean
function WindBotClientField:HasInSpellZone(cardId) end
---@param cardId integer
---@return boolean
function WindBotClientField:HasInGraveyard(cardId) end
---@param cardId integer
---@return boolean
function WindBotClientField:HasInBanished(cardId) end
---@param cardId integer
---@return boolean
function WindBotClientField:HasInExtra(cardId) end
---@param cardId integer
---@return integer
function WindBotClientField:GetRemainingCount(cardId) end

---@class WindBotDuel
---@field IsFirst boolean
---@field IsNewRule boolean
---@field IsNewRule2020 boolean
---@field DeckReversed boolean
---@field Fields WindBotClientField[] `Fields[0]` 为己方，`Fields[1]` 为对方。
---@field Turn integer 当前回合数。
---@field Player integer 当前回合玩家（`0` 己方，`1` 对方）。
---@field Phase DuelPhase 当前阶段。
---@field MainPhase WindBotMainPhase
---@field BattlePhase WindBotBattlePhase
---@field LastChainPlayer integer
---@field LastChainLocation CardLocation
---@field CurrentChain WindBotCardList
---@field ChainTargets WindBotCardList
---@field LastChainTargets WindBotCardList
---@field LastSummonPlayer integer
---@field SummoningCards WindBotCardList
---@field LastSummonedCards WindBotCardList
---@field SolvingChainIndex integer
local WindBotDuel = {}

---@param player integer
---@param loc CardLocation|integer
---@param seq integer
---@param subSeq? integer
---@return WindBotClientCard|nil
function WindBotDuel:GetCard(player, loc, seq, subSeq) end
---@return WindBotClientCard|nil
function WindBotDuel:GetCurrentChainCard() end
---@return WindBotClientCard|nil
function WindBotDuel:GetCurrentSolvingChainCard() end
---@return WindBotChainInfo|nil
function WindBotDuel:GetCurrentSolvingChainInfo() end
---@return boolean
function WindBotDuel:IsCurrentSolvingChainNegated() end

---@class WindBotGameClient
---@field Username string Bot 昵称。
---@field Deck string|nil 卡组名。
---@field DeckFile string|nil `.ydk` 卡组文件名。
---@field Dialog string|nil 对话名。
---@field Locale string|nil 本地化区域标识（如 `"zh-CN"`）。
---@field Debug boolean
---@field _chat boolean 是否允许发送聊天消息。
local WindBotGameClient = {}

--- 发送一条聊天消息到对局房间。
---@param message string
function WindBotGameClient:Chat(message) end

--- 主动投降。
function WindBotGameClient:Surrender() end

---@class WindBotGameAI
---@field Game WindBotGameClient
---@field Duel WindBotDuel
---@field Executor LuaDeckExecutor
local WindBotGameAI = {}

---@param index integer
---@param ... any
function WindBotGameAI:SendCustomChat(index, ...) end
---@overload fun(self: WindBotGameAI, card: WindBotClientCard)
---@overload fun(self: WindBotGameAI, cards: WindBotCardList|WindBotClientCard[])
---@overload fun(self: WindBotGameAI, loc: CardLocation)
---@param ... integer 卡号优先级列表（或单卡/区域）。
function WindBotGameAI:SelectCard(...) end
---@overload fun(self: WindBotGameAI, card: WindBotClientCard)
---@overload fun(self: WindBotGameAI, loc: CardLocation)
---@param ... integer
function WindBotGameAI:SelectNextCard(...) end
---@overload fun(self: WindBotGameAI, card: WindBotClientCard)
---@overload fun(self: WindBotGameAI, loc: CardLocation)
---@param ... integer
function WindBotGameAI:SelectThirdCard(...) end
---@param target WindBotClientCard|integer|CardLocation
---@param hint? integer
function WindBotGameAI:SelectMaterials(target, hint) end
function WindBotGameAI:CleanSelectMaterials() end
---@param pos CardPosition|integer
function WindBotGameAI:SelectPosition(pos) end
---@param zones integer
function WindBotGameAI:SelectPlace(zones) end
---@param opt integer
function WindBotGameAI:SelectOption(opt) end
---@param number integer
function WindBotGameAI:SelectNumber(number) end
---@param attribute CardAttribute|integer
function WindBotGameAI:SelectAttribute(attribute) end
---@param race CardRace|integer
function WindBotGameAI:SelectRace(race) end
---@param id integer
function WindBotGameAI:SelectAnnounceID(id) end
---@param opt boolean
function WindBotGameAI:SelectYesNo(opt) end
---@param attacker WindBotClientCard
---@param defender WindBotClientCard|nil
---@return WindBotBattlePhaseAction
function WindBotGameAI:Attack(attacker, defender) end
---@return WindBotBattlePhaseAction
function WindBotGameAI:ToEndPhase() end
---@return WindBotBattlePhaseAction
function WindBotGameAI:ToMainPhase2() end

---@class WindBotAIUtil
---@field Duel WindBotDuel
---@field Bot WindBotClientField
---@field Enemy WindBotClientField
local WindBotAIUtil = {}

---@param player integer
---@return integer
function WindBotAIUtil:GetTotalAttackingMonsterAttack(player) end
---@param field WindBotClientField
---@param onlyATK? boolean
---@return integer
function WindBotAIUtil:GetBestPower(field, onlyATK) end
---@param field WindBotClientField
---@return integer
function WindBotAIUtil:GetBestAttack(field) end
---@param onlyATK? boolean
---@return boolean
function WindBotAIUtil:IsOneEnemyBetter(onlyATK) end
---@param onlyATK? boolean
---@return boolean
function WindBotAIUtil:IsAllEnemyBetter(onlyATK) end
---@param onlyATK? boolean
---@return WindBotClientCard|nil
function WindBotAIUtil:GetBestBotMonster(onlyATK) end
---@param onlyATK? boolean
---@return WindBotClientCard|nil
function WindBotAIUtil:GetWorstBotMonster(onlyATK) end
---@param attack? integer
---@param canBeTarget? boolean
---@return WindBotClientCard|nil
function WindBotAIUtil:GetProblematicEnemyCard(attack, canBeTarget) end
---@param attack? integer
---@param canBeTarget? boolean
---@return WindBotClientCard|nil
function WindBotAIUtil:GetProblematicEnemyMonster(attack, canBeTarget) end
---@param onlyFaceup? boolean
---@param canBeTarget? boolean
---@return WindBotClientCard|nil
function WindBotAIUtil:GetBestEnemyMonster(onlyFaceup, canBeTarget) end
---@param onlyFaceup? boolean
---@return WindBotClientCard|nil
function WindBotAIUtil:GetBestEnemySpell(onlyFaceup) end
---@param card WindBotClientCard
---@return boolean
function WindBotAIUtil:IsChainTarget(card) end
---@param cardId integer
---@return boolean
function WindBotAIUtil:ChainContainsCard(cardId) end
---@return boolean
function WindBotAIUtil:IsTurn1OrMain2() end

---@class LuaDeckDsl
---@field DeclaredDeckName string|nil
---@field DeclaredDeckFile string|nil
---@field DeclaredLevel string|nil
---@field DeclaredAllowOverride boolean|nil
---@field DeclaredDescription string|nil
---@field DeclaredAuthor string|nil
---@field DeclaredThrowOnError boolean|nil

---@class LuaDeckExecutor
---@field Deck string|nil 关联的 `.ydk` 文件名。
---@field ScriptChunkName string
---@field Dsl LuaDeckDsl
---@field IsDisposed boolean
---@field CurrentType ExecutorType 当前正在评估的动作类型。
---@field CurrentCard WindBotClientCard|nil 当前正在评估的卡牌。
---@field CurrentActivateDescription integer 当前发动效果描述 ID。
---@field CurrentEffectTiming integer 当前连锁时点。
---@field Duel WindBotDuel
---@field AI WindBotGameAI
---@field Util WindBotAIUtil
---@field BotField WindBotClientField 己方场地（与全局 `Bot` 相同）。
---@field EnemyField WindBotClientField 对方场地（与全局 `Enemy` 相同）。
local LuaDeckExecutor = {}

--- 动态注册单条卡牌执行规则（模块式/运行时使用）。
---@param type WindBotExecutorType
---@param cardId integer
---@param callback? LuaDeckRuleCallback
function LuaDeckExecutor:AddLuaExecutor(type, cardId, callback) end

--- 注册一条 `DefaultExecutor` 内置规则。
---@overload fun(self: LuaDeckExecutor, type: WindBotExecutorType, defaultRuleName: DefaultRuleName|string)
---@param type WindBotExecutorType
---@param cardId integer
---@param defaultRuleName DefaultRuleName|string
function LuaDeckExecutor:AddDefaultExecutor(type, cardId, defaultRuleName) end

--- 直接调用一条 `DefaultExecutor` 无参内置规则。
---@param defaultRuleName DefaultRuleName|string
---@return boolean
function LuaDeckExecutor:CallDefaultRule(defaultRuleName) end

---@param card WindBotClientCard
---@return boolean
function LuaDeckExecutor:CheckCardIsNegated(card) end
---@param cardId integer
---@return boolean
function LuaDeckExecutor:CheckCardIdIsNegated(cardId) end
---@param cardId integer
---@return integer
function LuaDeckExecutor:GetCalledByTheGraveCount(cardId) end
---@param card WindBotClientCard
---@return boolean
function LuaDeckExecutor:CheckSpellActivateWillBeNegated(card) end

--- 通过 WindBot / AppLog 输出日志（不要使用 print）。
---@param message string
function LuaDeckExecutor:LogInfo(message) end
---@param message string
function LuaDeckExecutor:LogDebug(message) end
---@param message string
function LuaDeckExecutor:LogError(message) end

-- =============================================================================
-- 注入的宿主全局变量
-- =============================================================================

---@type LuaDeckExecutor
Self = nil
---@type LuaDeckExecutor
Executor = nil
---@type LuaDeckDsl
Dsl = nil
---@type WindBotDuel
Duel = nil
---@type WindBotGameAI
AI = nil
---@type WindBotAIUtil
Util = nil
---@type WindBotClientField
Bot = nil
---@type WindBotClientField
Enemy = nil

-- =============================================================================
-- xmake 风格卡组 DSL 全局函数
-- =============================================================================

---@alias LuaDeckRuleFunc fun(ctx: LuaDeckExecutor): boolean
---@alias LuaDeckRuleCallback LuaDeckRuleFunc|string|nil

--- 开启一个卡组作用域（类似 `xmake.lua` 的 `target("...")`）。单文件可包含多个 `deck(...)`。
---@param name string 卡组逻辑名称（对应 `Deck=` / `BotDeck=`）。
function deck(name) end

--- 结束当前 `deck(...)` 作用域，恢复顶层公共作用域。
function deck_end() end

--- 声明该卡组关联的默认 `.ydk` 卡组文件名（不含扩展名）。
---@param fileName string
function set_deckfile(fileName) end

--- 声明该卡组的难度等级（如 `"Easy"`、`"Normal"`、`"Hard"`）。
---@param level string
function set_level(level) end

--- 声明是否允许覆写同名内置 C# 卡组。
---@param allowOverride boolean
function set_override(allowOverride) end

--- 声明卡组描述信息。
---@param description string
function set_description(description) end

--- 声明卡组作者信息。
---@param author string
function set_author(author) end

--- 覆盖该脚本的异常处理策略：`true` 直接抛出，`false` 记录日志并回退默认行为。
---@param enable boolean
function set_throw_on_error(enable) end

--- 注册单条执行规则（支持按卡号或全卡通用；`callback` 可省略、传函数、或传底部具名函数名）。
---@overload fun(type: WindBotExecutorType)
---@overload fun(type: WindBotExecutorType, callback: LuaDeckRuleFunc|string)
---@param type WindBotExecutorType
---@param cardId integer
---@param callback? LuaDeckRuleCallback
function add_executor(type, cardId, callback) end

--- 批量为多个卡号注册同一动作规则（类似 `xmake` 的 `add_files`）。
---@param type WindBotExecutorType
---@param cardIds integer[]
---@param callback? LuaDeckRuleCallback
function add_executors(type, cardIds, callback) end

--- 注册 `DefaultExecutor` 内置规则。
---@overload fun(type: WindBotExecutorType, ruleName: DefaultRuleName|string)
---@param type WindBotExecutorType
---@param cardId integer
---@param ruleName DefaultRuleName|string
function add_default(type, cardId, ruleName) end

---@overload fun()
---@overload fun(callback: LuaDeckRuleFunc|string)
---@overload fun(fn: (fun(ctx: LuaDialogContext, spell: string): LuaDialogHookResult)|string|nil)
---@param cardId integer
---@param callback? LuaDeckRuleCallback
function on_activate(cardId, callback) end

---@overload fun()
---@overload fun(callback: LuaDeckRuleFunc|string)
---@overload fun(fn: (fun(ctx: LuaDialogContext, monster: string): LuaDialogHookResult)|string|nil)
---@param cardId integer
---@param callback? LuaDeckRuleCallback
function on_summon(cardId, callback) end

---@overload fun()
---@overload fun(callback: LuaDeckRuleFunc|string)
---@param cardId integer
---@param callback? LuaDeckRuleCallback
function on_spsummon(cardId, callback) end

---@overload fun()
---@overload fun(callback: LuaDeckRuleFunc|string)
---@param cardId integer
---@param callback? LuaDeckRuleCallback
function on_repos(cardId, callback) end

---@overload fun()
---@overload fun(callback: LuaDeckRuleFunc|string)
---@param cardId integer
---@param callback? LuaDeckRuleCallback
function on_monster_set(cardId, callback) end

---@overload fun()
---@overload fun(callback: LuaDeckRuleFunc|string)
---@param cardId integer
---@param callback? LuaDeckRuleCallback
function on_spell_set(cardId, callback) end

---@overload fun()
---@overload fun(callback: LuaDeckRuleFunc|string)
---@param cardId integer
---@param callback? LuaDeckRuleCallback
function on_summon_or_set(cardId, callback) end

---@param callback? LuaDeckRuleCallback
function on_goto_battle(callback) end

---@param callback? LuaDeckRuleCallback
function on_goto_main2(callback) end

---@param callback? LuaDeckRuleCallback
function on_goto_end(callback) end

---@overload fun(fn: (fun(ctx: LuaDialogContext): LuaDialogHookResult)|string|nil)
---@param callback? LuaDeckRuleCallback
function on_surrender(callback) end

-- -----------------------------------------------------------------------------
-- 生命周期与决策钩子（返回 nil 时自动回退 DefaultExecutor 默认逻辑）
-- -----------------------------------------------------------------------------

---@overload fun(fn: fun(ctx: LuaDialogContext)|string|nil)
---@param fn fun(ctx: LuaDeckExecutor)|string|nil
function on_init(fn) end
---@overload fun(fn: fun(ctx: LuaDialogContext)|string|nil)
---@param fn fun(ctx: LuaDeckExecutor)|string|nil
function on_dispose(fn) end
--- 返回 1=剪刀, 2=石头, 3=布；返回 nil 回退默认随机。
---@param fn (fun(ctx: LuaDeckExecutor): integer|nil)|string|nil
function on_rock_paper_scissors(fn) end
--- 猜拳胜出后选择先攻（`true`）还是后攻（`false`）。
---@param fn (fun(ctx: LuaDeckExecutor): boolean|nil)|string|nil
function on_select_hand(fn) end
---@param fn (fun(ctx: LuaDeckExecutor, attackers: WindBotCardList, defenders: WindBotCardList): WindBotBattlePhaseAction|nil)|string|nil
function on_battle(fn) end
---@param fn (fun(ctx: LuaDeckExecutor, attackers: WindBotCardList, defenders: WindBotCardList): WindBotClientCard|nil)|string|nil
function on_select_attacker(fn) end
---@param fn (fun(ctx: LuaDeckExecutor, attacker: WindBotClientCard, defenders: WindBotCardList): WindBotClientCard|nil)|string|nil
function on_select_attack_target(fn) end
---@param fn (fun(ctx: LuaDeckExecutor, attacker: WindBotClientCard, defender: WindBotClientCard): boolean|nil)|string|nil
function on_pre_battle(fn) end
---@param fn (fun(ctx: LuaDeckExecutor, attacker: WindBotClientCard, defender: WindBotClientCard): boolean|nil)|string|nil
function on_pre_battle_between(fn) end
---@param fn (fun(ctx: LuaDeckExecutor): boolean|nil)|string|nil
function on_select_battle_replay(fn) end
---@param fn (fun(ctx: LuaDeckExecutor, card: WindBotClientCard): boolean|nil)|string|nil
function on_pre_activate(fn) end
---@overload fun(fn: (fun(ctx: LuaDialogContext, card: string): LuaDialogHookResult)|string|nil)
---@param fn fun(ctx: LuaDeckExecutor, card: WindBotClientCard, player: integer)|string|nil
function on_chaining(fn) end
---@param fn fun(ctx: LuaDeckExecutor, chainIndex: integer)|string|nil
function on_chain_solved(fn) end
---@param fn fun(ctx: LuaDeckExecutor)|string|nil
function on_chain_end(fn) end
---@param fn (fun(ctx: LuaDeckExecutor, cards: WindBotCardList, descs: WindBotIntList, forces: WindBotBoolList, timing: integer): integer|nil)|string|nil
function on_select_chain(fn) end
---@param fn fun(ctx: LuaDeckExecutor, player: integer, data: integer)|string|nil
function on_receiving_announce(fn) end
---@param fn fun(ctx: LuaDeckExecutor)|string|nil
function on_new_phase(fn) end
---@overload fun(fn: (fun(ctx: LuaDialogContext): LuaDialogHookResult)|string|nil)
---@param fn fun(ctx: LuaDeckExecutor)|string|nil
function on_new_turn(fn) end
---@param fn fun(ctx: LuaDeckExecutor, player: integer)|string|nil
function on_draw(fn) end
---@param fn fun(ctx: LuaDeckExecutor, player: integer, hintType: integer, description: integer)|string|nil
function on_player_hint(fn) end
---@param fn fun(ctx: LuaDeckExecutor, player: integer, zone: integer)|string|nil
function on_hint_zone(fn) end
---@param fn fun(ctx: LuaDeckExecutor, card: WindBotClientCard, previousControler: integer, previousLocation: integer, currentControler: integer, currentLocation: integer)|string|nil
function on_move(fn) end
---@param fn fun(ctx: LuaDeckExecutor, card: WindBotClientCard, previousPosition: integer, currentPosition: integer)|string|nil
function on_pos_change(fn) end
---@param fn fun(ctx: LuaDeckExecutor)|string|nil
function on_summoning(fn) end
---@param fn fun(ctx: LuaDeckExecutor)|string|nil
function on_sp_summoning(fn) end
---@param fn fun(ctx: LuaDeckExecutor)|string|nil
function on_sp_summoned(fn) end
---@param fn (fun(ctx: LuaDeckExecutor, cards: WindBotCardList, min: integer, max: integer, hint: integer, cancelable: boolean): WindBotClientCard[]|nil)|string|nil
function on_select_card(fn) end
---@param fn (fun(ctx: LuaDeckExecutor, cards: WindBotCardList, min: integer, max: integer, hint: integer, cancelable: boolean): WindBotClientCard[]|nil)|string|nil
function on_select_tribute(fn) end
---@param fn (fun(ctx: LuaDeckExecutor, cards: WindBotCardList, sum: integer, min: integer, max: integer, hint: integer, mode: boolean): WindBotClientCard[]|nil)|string|nil
function on_select_sum(fn) end
---@param fn (fun(ctx: LuaDeckExecutor, cards: WindBotCardList, min: integer, max: integer): WindBotClientCard[]|nil)|string|nil
function on_select_fusion_material(fn) end
---@param fn (fun(ctx: LuaDeckExecutor, cards: WindBotCardList, sum: integer, min: integer, max: integer): WindBotClientCard[]|nil)|string|nil
function on_select_synchro_material(fn) end
---@param fn (fun(ctx: LuaDeckExecutor, cards: WindBotCardList, min: integer, max: integer): WindBotClientCard[]|nil)|string|nil
function on_select_xyz_material(fn) end
---@param fn (fun(ctx: LuaDeckExecutor, cards: WindBotCardList, min: integer, max: integer): WindBotClientCard[]|nil)|string|nil
function on_select_link_material(fn) end
---@param fn (fun(ctx: LuaDeckExecutor, cards: WindBotCardList, sum: integer, min: integer, max: integer): WindBotClientCard[]|nil)|string|nil
function on_select_ritual_tribute(fn) end
---@param fn (fun(ctx: LuaDeckExecutor, cards: WindBotCardList, min: integer, max: integer): WindBotClientCard[]|nil)|string|nil
function on_select_pendulum_summon(fn) end
---@param fn (fun(ctx: LuaDeckExecutor, cards: WindBotCardList): WindBotClientCard[]|nil)|string|nil
function on_card_sorting(fn) end
---@param fn (fun(ctx: LuaDeckExecutor, desc: integer): boolean|nil)|string|nil
function on_select_yesno(fn) end
---@param fn (fun(ctx: LuaDeckExecutor, options: WindBotIntList): integer|nil)|string|nil
function on_select_option(fn) end
---@param fn (fun(ctx: LuaDeckExecutor, cardId: integer, count: integer, available: integer): integer|nil)|string|nil
function on_select_place(fn) end
---@param fn (fun(ctx: LuaDeckExecutor, hint: integer, count: integer, available: integer): integer|nil)|string|nil
function on_select_disfield(fn) end
---@param fn (fun(ctx: LuaDeckExecutor, cardId: integer, positions: WindBotPositionList): CardPosition|integer|nil)|string|nil
function on_select_position(fn) end
---@param fn (fun(ctx: LuaDeckExecutor): boolean|nil)|string|nil
function on_select_battle_direct_attack(fn) end
---@param fn (fun(ctx: LuaDeckExecutor, card: WindBotClientCard): boolean|nil)|string|nil
function on_select_monster_summon_or_set(fn) end
---@param fn (fun(ctx: LuaDeckExecutor, avail: WindBotIntList): integer|nil)|string|nil
function on_announce_card(fn) end

--- 模块返回式卡组脚本（`local M = {}; ... return M`）的可选接口提示。
---@class WindBotDeckModule
---@field OnInit? fun(self: LuaDeckExecutor)
---@field OnDispose? fun(self: LuaDeckExecutor)
---@field OnNewTurn? fun(self: LuaDeckExecutor)
---@field OnNewPhase? fun(self: LuaDeckExecutor)
---@field OnSelectCard? fun(self: LuaDeckExecutor, cards: WindBotCardList, min: integer, max: integer, hint: integer, cancelable: boolean): WindBotClientCard[]|nil
---@field OnSelectYesNo? fun(self: LuaDeckExecutor, desc: integer): boolean|nil
---@field OnSelectOption? fun(self: LuaDeckExecutor, options: WindBotIntList): integer|nil
---@field OnSelectPlace? fun(self: LuaDeckExecutor, cardId: integer, count: integer, available: integer): integer|nil
---@field OnSelectDisfield? fun(self: LuaDeckExecutor, hint: integer, count: integer, available: integer): integer|nil
---@field OnSelectPosition? fun(self: LuaDeckExecutor, cardId: integer, positions: WindBotPositionList): CardPosition|integer|nil
