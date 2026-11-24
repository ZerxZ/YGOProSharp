---@meta
---@diagnostic disable: lowercase-global, missing-return, unused-local, duplicate-set-field

--- ygopro-core Lua API 精确类型声明（LuaLS / EmmyLua）。
---
--- 该文件由 `xmake gen-lua-api -P YGOProSharp.Native` 依据
--- `YGOProSharp.Native/ygopro-core/lib*.cpp` 的 `luaL_Reg` 注册表与
--- `YGOProSharp.Native/lua-api/signatures/*.lua` 生成。
--- 仅用于编辑器补全与静态类型检查，不应放入 `script_reader` 运行时目录。

---@alias Player integer 玩家编号：`0` / `1`（部分查询支持 `PLAYER_NONE=2`、`PLAYER_ALL=3`）。
---@alias CardCode integer 卡片密码（卡号）。
---@alias SetCode integer 字段编码（16 位字段值）。
---@alias CardType integer 卡片种类位掩码（`TYPE_*`）。
---@alias Location integer 区域位掩码（`LOCATION_*`）。
---@alias Position integer 表示形式位掩码（`POS_*`）。
---@alias Reason integer 移动/操作原因位掩码（`REASON_*`）。
---@alias Reset integer 效果重置条件位掩码（`RESET_*` / `RESETS_*`）。
---@alias EffectCode integer 效果代码或事件代码（`EFFECT_*` / `EVENT_*`）。
---@alias EffectType integer 效果类型位掩码（`EFFECT_TYPE_*`）。
---@alias EffectProperty integer 效果主属性位掩码（`EFFECT_FLAG_*`）。
---@alias EffectProperty2 integer 效果扩展属性位掩码（`EFFECT_FLAG2_*`）。
---@alias EffectCategory integer 效果分类位掩码（`CATEGORY_*`）。
---@alias Race integer 怪兽种族位掩码（`RACE_*`）。
---@alias Attribute integer 怪兽属性位掩码（`ATTRIBUTE_*`）。
---@alias Phase integer 阶段常量（`PHASE_*`）。
---@alias SummonType integer 召唤类型位掩码（`SUMMON_TYPE_*`）。
---@alias SummonInfo integer 特殊召唤快照查询标志（`SUMMON_INFO_*`）。
---@alias ChainInfo integer 连锁信息查询标志（`CHAININFO_*`）。
---@alias QueryFlag integer 场面查询位掩码（`QUERY_*`）。
---@alias Hint integer 提示类型常量（`HINT_*`）。
---@alias CardHint integer 单卡提示类型常量（`CHINT_*`）。
---@alias Zone integer 场上格子位掩码。
---@alias CountLimitCode integer 次数限制编码（卡号或自定义 ID，可按位或 `EFFECT_COUNT_CODE_*`）。
---@alias CounterType integer 指示物类型编码。
---@alias GlobalFlag integer 全局标志位掩码（`GLOBALFLAG_*`）。
---@alias ActivityType integer 行动计数类型常量（`ACTIVITY_*`）。
---@alias Status integer 卡片运行时状态位掩码（`STATUS_*`）。
---@alias LinkMarker integer 连接标记方向位掩码（`LINK_MARKER_*`）。
---@alias AssumeType integer 假设属性类型常量（`ASSUME_*`）。
---@alias Opcode integer 宣言卡名过滤后缀表达式操作码（`OPCODE_*`）。
---@alias Timing integer 提示时点位掩码（`TIMING_*`）。
---@alias DuelOption integer 对局规则选项位掩码（`DUEL_*`）。

---@alias FilterFunction fun(c: Card, ...: any): boolean|integer|nil
---@alias EffectFilterFunction fun(e: Effect): boolean|integer|nil
---@alias MaterialFilter fun(c: Card, ...: any): boolean|integer|nil
---@alias ClassValueFunction fun(c: Card, ...: any): integer
---@alias SumValueFunction fun(c: Card, ...: any): integer
---@alias EffectTargetFilter fun(e: Effect, c: Card, ...: any): boolean|integer|nil
---@alias ValueFunction fun(e: Effect, c?: Card|Effect|nil, ...: any): integer|boolean|nil
---@alias ChainLimitFunction fun(e: Effect, rp: Player, tp: Player): boolean|integer|nil
---@alias ConditionFunction fun(e: Effect, tp: Player, eg: Group, ep: Player, ev: integer, re: Effect, r: Reason, rp: Player): boolean|integer|nil
---@alias CostFunction fun(e: Effect, tp: Player, eg: Group, ep: Player, ev: integer, re: Effect, r: Reason, rp: Player, chk: integer): boolean|integer|nil
---@alias TargetFunction fun(e: Effect, tp: Player, eg: Group, ep: Player, ev: integer, re: Effect, r: Reason, rp: Player, chk: integer, chkc?: Card): boolean|integer|nil
---@alias OperationFunction fun(e: Effect, tp: Player, eg: Group, ep: Player, ev: integer, re: Effect, r: Reason, rp: Player): nil
---@alias InitialEffectFunction fun(c: Card): nil

--- 卡片脚本全局初始化上下文（加载 `c<id>.lua` 时由 `interpreter::load_card_script` 临时注入）。
---@type table|nil
self_table = nil
---@type CardCode|nil
self_code = nil

---@class Card
---@operator add(Group): Group
---@operator bor(Group): Group
---@operator sub(Group): Group
---@operator band(Group): Group
---@operator bxor(Group): Group
Card = {}

---@class Effect
Effect = {}

---@class Group
---@operator len: integer
---@operator add(Card|Group): Group
---@operator bor(Card|Group): Group
---@operator sub(Card|Group): Group
---@operator band(Card|Group): Group
---@operator bxor(Card|Group): Group
Group = {}

---@class Duel
Duel = {}

---@class Debug
Debug = {}

-------------------------------------------------------------------------------
-- Card API (281 functions)
-------------------------------------------------------------------------------

--- 取得当前卡号；若存在第二卡号（如规则同名卡）则额外返回第 2 个卡号。
--- C++: `scriptlib::card_get_code`
---@return CardCode code 当前主卡号。
---@return CardCode? anotherCode 第二卡号（若存在）。
function Card:GetCode() end

--- 取得卡片的原本单卡号（`data.get_original_code()`）。
--- C++: `scriptlib::card_get_origin_code`
---@return CardCode code
function Card:GetOriginalCode() end

--- 取得规则视角上的原本卡号；存在第二原本卡号时返回 2 个卡号。
--- C++: `scriptlib::card_get_origin_code_rule`
---@return CardCode code 规则原本卡号。
---@return CardCode? secondCode 第二规则卡号（若存在）。
function Card:GetOriginalCodeRule() end

--- 多值返回作为融合素材时的全部卡号（当前卡号、可选第二卡号及 `EFFECT_ADD_FUSION_CODE` 追加卡号）。
--- C++: `scriptlib::card_get_fusion_code`
---@return CardCode ... 融合卡号列表。
function Card:GetFusionCode() end

--- 多值返回作为连接素材时的全部卡号（当前卡号、可选第二卡号及 `EFFECT_ADD_LINK_CODE` 追加卡号）。
--- C++: `scriptlib::card_get_link_code`
---@return CardCode ... 连接卡号列表。
function Card:GetLinkCode() end

--- 判断作为融合素材时的卡号集合是否匹配任意给定卡号。
--- C++: `scriptlib::card_is_fusion_code`
---@param code CardCode|nil 目标值（`nil` 会被跳过）。
---@param ... CardCode|nil 额外候选值（匹配任意一个即返回 `true`）。
---@return boolean matched
function Card:IsFusionCode(code, ...) end

--- 判断作为连接素材时的卡号集合是否匹配任意给定卡号。
--- C++: `scriptlib::card_is_link_code`
---@param code CardCode|nil 目标值（`nil` 会被跳过）。
---@param ... CardCode|nil 额外候选值（匹配任意一个即返回 `true`）。
---@return boolean matched
function Card:IsLinkCode(code, ...) end

--- 判断当前字段是否匹配任意给定字段编码。
--- C++: `scriptlib::card_is_set_card`
---@param setcode SetCode|nil 目标值（`nil` 会被跳过）。
---@param ... SetCode|nil 额外候选值（匹配任意一个即返回 `true`）。
---@return boolean matched
function Card:IsSetCard(setcode, ...) end

--- 判断原本字段是否匹配任意给定字段编码。
--- C++: `scriptlib::card_is_origin_set_card`
---@param setcode SetCode|nil 目标值（`nil` 会被跳过）。
---@param ... SetCode|nil 额外候选值（匹配任意一个即返回 `true`）。
---@return boolean matched
function Card:IsOriginalSetCard(setcode, ...) end

--- 判断离场/区域变更前的字段是否匹配任意给定字段编码。
--- C++: `scriptlib::card_is_pre_set_card`
---@param setcode SetCode|nil 目标值（`nil` 会被跳过）。
---@param ... SetCode|nil 额外候选值（匹配任意一个即返回 `true`）。
---@return boolean matched
function Card:IsPreviousSetCard(setcode, ...) end

--- 判断作为融合素材时的字段是否匹配任意给定字段编码。
--- C++: `scriptlib::card_is_fusion_set_card`
---@param setcode SetCode|nil 目标值（`nil` 会被跳过）。
---@param ... SetCode|nil 额外候选值（匹配任意一个即返回 `true`）。
---@return boolean matched
function Card:IsFusionSetCard(setcode, ...) end

--- 判断作为连接素材时的字段是否匹配任意给定字段编码。
--- C++: `scriptlib::card_is_link_set_card`
---@param setcode SetCode|nil 目标值（`nil` 会被跳过）。
---@param ... SetCode|nil 额外候选值（匹配任意一个即返回 `true`）。
---@return boolean matched
function Card:IsLinkSetCard(setcode, ...) end

--- 判断特殊召唤快照记录的字段是否匹配任意给定字段编码。
--- C++: `scriptlib::card_is_special_summon_set_card`
---@param setcode SetCode|nil 目标值（`nil` 会被跳过）。
---@param ... SetCode|nil 额外候选值（匹配任意一个即返回 `true`）。
---@return boolean matched
function Card:IsSpecialSummonSetCard(setcode, ...) end

--- 取得基础卡片种类位掩码（受 `EFFECT_ADD_CARD_TYPE` 等基础种类效果影响）。
--- C++: `scriptlib::card_get_card_type`
---@return CardType value
function Card:GetCardType() end

--- 取得当前完整卡片种类位掩码（`TYPE_*`）。
--- C++: `scriptlib::card_get_type`
---@return CardType value
function Card:GetType() end

--- 取得卡库记录的原本卡片种类（`data.type`）。
--- C++: `scriptlib::card_get_origin_type`
---@return CardType value
function Card:GetOriginalType() end

--- 取得作为融合素材时视为的卡片种类。
--- C++: `scriptlib::card_get_fusion_type`
---@return CardType value
function Card:GetFusionType() end

--- 取得作为同调素材时视为的卡片种类。
--- C++: `scriptlib::card_get_synchro_type`
---@return CardType value
function Card:GetSynchroType() end

--- 取得作为超量素材时视为的卡片种类。
--- C++: `scriptlib::card_get_xyz_type`
---@return CardType value
function Card:GetXyzType() end

--- 取得作为连接素材时视为的卡片种类。
--- C++: `scriptlib::card_get_link_type`
---@return CardType value
function Card:GetLinkType() end

--- 取得当前等级（无等级时返回 `0`）。
--- C++: `scriptlib::card_get_level`
---@return integer value
function Card:GetLevel() end

--- 取得当前阶级（非超量时返回 `0`）。
--- C++: `scriptlib::card_get_rank`
---@return integer value
function Card:GetRank() end

--- 取得当前连接值（非连接时返回 `0`）。
--- C++: `scriptlib::card_get_link`
---@return integer value
function Card:GetLink() end

--- 取得作为同调怪兽 `sc` 素材时的同调等级（可能以高低 16 位编码两个可选等级）。
--- C++: `scriptlib::card_get_synchro_level`
---@param sc Card 目标同调怪兽。
---@return integer synchroLevel
function Card:GetSynchroLevel(sc) end

--- 取得作为仪式怪兽 `rc` 祭品时的仪式等级（可能以高低 16 位编码两个可选等级）。
--- C++: `scriptlib::card_get_ritual_level`
---@param rc Card 目标仪式怪兽。
---@return integer ritualLevel
function Card:GetRitualLevel(rc) end

--- 取得原本等级（原本超量/连接或带 `STATUS_NO_LEVEL` 时返回 `0`）。
--- C++: `scriptlib::card_get_origin_level`
---@return integer value
function Card:GetOriginalLevel() end

--- 取得原本阶级（原本非超量时返回 `0`）。
--- C++: `scriptlib::card_get_origin_rank`
---@return integer value
function Card:GetOriginalRank() end

--- 判定此卡作为超量怪兽 `xyzCard` 素材时是否可视为等级 `lv`。
--- C++: `scriptlib::card_is_xyz_level`
---@param xyzCard Card 目标超量怪兽。
---@param lv integer 目标等级。
---@return boolean matched
function Card:IsXyzLevel(xyzCard, lv) end

--- 取得当前左灵摆刻度。
--- C++: `scriptlib::card_get_lscale`
---@return integer value
function Card:GetLeftScale() end

--- 取得原本左灵摆刻度（`data.lscale`）。
--- C++: `scriptlib::card_get_origin_lscale`
---@return integer value
function Card:GetOriginalLeftScale() end

--- 取得当前右灵摆刻度。
--- C++: `scriptlib::card_get_rscale`
---@return integer value
function Card:GetRightScale() end

--- 取得原本右灵摆刻度（`data.rscale`）。
--- C++: `scriptlib::card_get_origin_rscale`
---@return integer value
function Card:GetOriginalRightScale() end

--- 取得当前生效的灵摆刻度（在左/右 P 区取左/右刻度，否则取 `data.lscale`）。
--- C++: `scriptlib::card_get_current_scale`
---@return integer value
function Card:GetCurrentScale() end

--- 判定此卡是否具有指定的连接标记方向。
--- C++: `scriptlib::card_is_link_marker`
---@param marker LinkMarker 连接标记位掩码（`LINK_MARKER_*`）。
---@return boolean matched
function Card:IsLinkMarker(marker) end

--- 取得此卡连接端指向的卡片集合。
--- C++: `scriptlib::card_get_linked_group`
---@return Group group
function Card:GetLinkedGroup() end

--- 取得此卡连接端指向的卡片数量。
--- C++: `scriptlib::card_get_linked_group_count`
---@return integer value
function Card:GetLinkedGroupCount() end

--- 取得此卡连接端所指的区域位掩码；`player` 为对方时自动互换高低 16 位。
--- C++: `scriptlib::card_get_linked_zone`
---@param player? Player 视角玩家（默认卡片当前控制者）。
---@return Zone zone
function Card:GetLinkedZone(player) end

--- 取得与此卡互相连接的卡片集合。
--- C++: `scriptlib::card_get_mutual_linked_group`
---@return Group group
function Card:GetMutualLinkedGroup() end

--- 取得与此卡互相连接的卡片数量。
--- C++: `scriptlib::card_get_mutual_linked_group_count`
---@return integer value
function Card:GetMutualLinkedGroupCount() end

--- 取得与此卡互相连接的区域位掩码；`player` 为对方时自动互换高低 16 位。
--- C++: `scriptlib::card_get_mutual_linked_zone`
---@param player? Player 视角玩家（默认卡片当前控制者）。
---@return Zone zone
function Card:GetMutualLinkedZone(player) end

--- 判定此卡是否处于连接状态。
--- C++: `scriptlib::card_is_link_state`
---@return boolean result
function Card:IsLinkState() end

--- 判定此卡是否处于额外连接状态。
--- C++: `scriptlib::card_is_extra_link_state`
---@return boolean result
function Card:IsExtraLinkState() end

--- 取得与此卡同纵列的卡片集合（注意：`ygopro-core` 中无参数，与 EDOPro 不同）。
--- C++: `scriptlib::card_get_column_group`
---@return Group group
function Card:GetColumnGroup() end

--- 取得与此卡同纵列的卡片数量。
--- C++: `scriptlib::card_get_column_group_count`
---@return integer value
function Card:GetColumnGroupCount() end

--- 取得与此卡同纵列且属于区域 `loc` 的格子位掩码；`player` 为对方时自动互换高低 16 位。
--- C++: `scriptlib::card_get_column_zone`
---@param loc Location 区域筛选掩码（如 `LOCATION_MZONE`、`LOCATION_SZONE`）。
---@param player? Player 视角玩家（默认卡片当前控制者）。
---@return Zone zone
function Card:GetColumnZone(loc, player) end

--- 判定此卡所在纵列是否全格都有卡。
--- C++: `scriptlib::card_is_all_column`
---@return boolean result
function Card:IsAllColumn() end

--- 取得当前属性位掩码。
--- C++: `scriptlib::card_get_attribute`
---@return Attribute value
function Card:GetAttribute() end

--- 取得原本属性（带 `STATUS_NO_LEVEL` 时返回 `0`）。
--- C++: `scriptlib::card_get_origin_attribute`
---@return Attribute value
function Card:GetOriginalAttribute() end

--- 取得作为融合素材时对指定玩家视为的属性。
--- C++: `scriptlib::card_get_fusion_attribute`
---@param player? Player 视角玩家（默认当前 `reason_player`）。
---@return Attribute value
function Card:GetFusionAttribute(player) end

--- 取得作为连接素材时对指定玩家视为的属性。
--- C++: `scriptlib::card_get_link_attribute`
---@param player? Player 视角玩家（默认当前 `reason_player`）。
---@return Attribute value
function Card:GetLinkAttribute(player) end

--- 取得在墓地对指定玩家视为的属性。
--- C++: `scriptlib::card_get_attribute_in_grave`
---@param player? Player 视角玩家（默认当前 `reason_player`）。
---@return Attribute value
function Card:GetAttributeInGrave(player) end

--- 取得当前种族位掩码。
--- C++: `scriptlib::card_get_race`
---@return Race value
function Card:GetRace() end

--- 取得原本种族（带 `STATUS_NO_LEVEL` 时返回 `0`）。
--- C++: `scriptlib::card_get_origin_race`
---@return Race value
function Card:GetOriginalRace() end

--- 取得作为连接素材时对指定玩家视为的种族。
--- C++: `scriptlib::card_get_link_race`
---@param player? Player 视角玩家（默认当前 `reason_player`）。
---@return Race value
function Card:GetLinkRace(player) end

--- 取得在墓地对指定玩家视为的种族。
--- C++: `scriptlib::card_get_race_in_grave`
---@param player? Player 视角玩家（默认当前 `reason_player`）。
---@return Race value
function Card:GetRaceInGrave(player) end

--- 取得当前攻击力（负值钳位为 `0`）。
--- C++: `scriptlib::card_get_attack`
---@return integer value
function Card:GetAttack() end

--- 取得原本攻击力（`get_base_attack()`，负值钳位为 `0`）。
--- C++: `scriptlib::card_get_origin_attack`
---@return integer value
function Card:GetBaseAttack() end

--- 取得卡面印刷攻击力（`?` 返回 `-2`，带 `STATUS_NO_LEVEL` 返回 `0`）。
--- C++: `scriptlib::card_get_text_attack`
---@return integer value
function Card:GetTextAttack() end

--- 取得当前守备力（负值钳位为 `0`）。
--- C++: `scriptlib::card_get_defense`
---@return integer value
function Card:GetDefense() end

--- 取得原本守备力（`get_base_defense()`，负值钳位为 `0`）。
--- C++: `scriptlib::card_get_origin_defense`
---@return integer value
function Card:GetBaseDefense() end

--- 取得卡面印刷守备力（`?` 返回 `-2`，带 `STATUS_NO_LEVEL` 返回 `0`）。
--- C++: `scriptlib::card_get_text_defense`
---@return integer value
function Card:GetTextDefense() end

--- 取得离场前在场上的卡号；存在第二卡号时返回 2 个卡号。
--- C++: `scriptlib::card_get_previous_code_onfield`
---@return CardCode code
---@return CardCode? anotherCode
function Card:GetPreviousCodeOnField() end

--- 取得离场前在场上的基础种类位掩码。
--- C++: `scriptlib::card_get_previous_card_type_onfield`
---@return CardType value
function Card:GetPreviousCardTypeOnField() end

--- 取得离场前在场上的完整种类位掩码。
--- C++: `scriptlib::card_get_previous_type_onfield`
---@return CardType value
function Card:GetPreviousTypeOnField() end

--- 取得离场前在场上的等级。
--- C++: `scriptlib::card_get_previous_level_onfield`
---@return integer value
function Card:GetPreviousLevelOnField() end

--- 取得离场前在场上的阶级。
--- C++: `scriptlib::card_get_previous_rank_onfield`
---@return integer value
function Card:GetPreviousRankOnField() end

--- 取得离场前在场上的属性。
--- C++: `scriptlib::card_get_previous_attribute_onfield`
---@return Attribute value
function Card:GetPreviousAttributeOnField() end

--- 取得离场前在场上的种族。
--- C++: `scriptlib::card_get_previous_race_onfield`
---@return Race value
function Card:GetPreviousRaceOnField() end

--- 取得离场前在场上的攻击力。
--- C++: `scriptlib::card_get_previous_attack_onfield`
---@return integer value
function Card:GetPreviousAttackOnField() end

--- 取得离场前在场上的守备力。
--- C++: `scriptlib::card_get_previous_defense_onfield`
---@return integer value
function Card:GetPreviousDefenseOnField() end

--- 取得离场前在场上的超量素材数量。
--- C++: `scriptlib::card_get_previous_overlay_count_onfield`
---@return integer value
function Card:GetPreviousOverlayCountOnField() end

--- 取得卡片持有者（原本卡组所属玩家）。
--- C++: `scriptlib::card_get_owner`
---@return Player value
function Card:GetOwner() end

--- 取得卡片当前控制者。
--- C++: `scriptlib::card_get_controler`
---@return Player value
function Card:GetControler() end

--- 取得卡片上一个控制者。
--- C++: `scriptlib::card_get_previous_controler`
---@return Player value
function Card:GetPreviousControler() end

--- 覆写卡片当前原因字段 `current.reason`。
--- C++: `scriptlib::card_set_reason`
---@param reason Reason 原因位掩码。
---@return nil
function Card:SetReason(reason) end

--- 取得卡片当前移动/状态变更的原因位掩码。
--- C++: `scriptlib::card_get_reason`
---@return Reason value
function Card:GetReason() end

--- 取得导致当前原因的关联卡片。
--- C++: `scriptlib::card_get_reason_card`
---@return Card|nil card
function Card:GetReasonCard() end

--- 取得触发当前原因的玩家编号。
--- C++: `scriptlib::card_get_reason_player`
---@return Player value
function Card:GetReasonPlayer() end

--- 取得导致当前原因的关联效果。
--- C++: `scriptlib::card_get_reason_effect`
---@return Effect|nil effect
function Card:GetReasonEffect() end

--- 取得卡片当前表示形式。
--- C++: `scriptlib::card_get_position`
---@return Position value
function Card:GetPosition() end

--- 取得卡片上一个表示形式。
--- C++: `scriptlib::card_get_previous_position`
---@return Position value
function Card:GetPreviousPosition() end

--- 取得战斗期间记录的临时表示形式（`temp.position`）。
--- C++: `scriptlib::card_get_battle_position`
---@return Position value
function Card:GetBattlePosition() end

--- 取得卡片当前所在区域（若被视为不在场则返回 `0`）。
--- C++: `scriptlib::card_get_location`
---@return Location value
function Card:GetLocation() end

--- 取得卡片上一个所在区域。
--- C++: `scriptlib::card_get_previous_location`
---@return Location value
function Card:GetPreviousLocation() end

--- 取得卡片当前所在区域内的序号。
--- C++: `scriptlib::card_get_sequence`
---@return integer value
function Card:GetSequence() end

--- 取得卡片上一个区域内的序号。
--- C++: `scriptlib::card_get_previous_sequence`
---@return integer value
function Card:GetPreviousSequence() end

--- 取得卡片的召唤类型（`summon_info & DEFAULT_SUMMON_TYPE`）。
--- C++: `scriptlib::card_get_summon_type`
---@return SummonType value
function Card:GetSummonType() end

--- 取得卡片召唤前所在的区域。
--- C++: `scriptlib::card_get_summon_location`
---@return Location value
function Card:GetSummonLocation() end

--- 取得执行召唤的玩家编号。
--- C++: `scriptlib::card_get_summon_player`
---@return Player value
function Card:GetSummonPlayer() end

--- 按传入的 0..N 个 `SUMMON_INFO_*` 标志依次返回对应特召快照值（`SUMMON_INFO_REASON_EFFECT` 返回 `Effect|nil`，其余为 `integer`，未知标志返回 `nil`）。
--- C++: `scriptlib::card_get_special_summon_info`
---@param ... SummonInfo 要查询的特召快照标志（`SUMMON_INFO_*`）。
---@return (integer|Effect|nil) ... 与输入标志一一对应的返回值列表。
function Card:GetSpecialSummonInfo(...) end

--- 取得即将移动到的目标区域（`sendto_param.location`）。
--- C++: `scriptlib::card_get_destination`
---@return Location value
function Card:GetDestination() end

--- 取得因效果离场时的重定向目标区域。
--- C++: `scriptlib::card_get_leave_field_dest`
---@return Location value
function Card:GetLeaveFieldDest() end

--- 取得进入当前区域的回合编号。
--- C++: `scriptlib::card_get_turnid`
---@return integer value
function Card:GetTurnID() end

--- 取得场上标识 ID（`fieldid`）。
--- C++: `scriptlib::card_get_fieldid`
---@return integer value
function Card:GetFieldID() end

--- 取得场上真实标识 ID（`fieldid_r`，用于判定是否离开过场）。
--- C++: `scriptlib::card_get_fieldidr`
---@return integer value
function Card:GetRealFieldID() end

--- 判断规则原本卡号是否匹配任意给定卡号。
--- C++: `scriptlib::card_is_origin_code_rule`
---@param code CardCode|nil 目标值（`nil` 会被跳过）。
---@param ... CardCode|nil 额外候选值（匹配任意一个即返回 `true`）。
---@return boolean matched
function Card:IsOriginalCodeRule(code, ...) end

--- 判断当前卡号（含第二卡号）是否匹配任意给定卡号。
--- C++: `scriptlib::card_is_code`
---@param code CardCode|nil 目标值（`nil` 会被跳过）。
---@param ... CardCode|nil 额外候选值（匹配任意一个即返回 `true`）。
---@return boolean matched
function Card:IsCode(code, ...) end

--- 判定基础卡片种类是否包含指定位掩码的**任一位**（区别于 `IsType` 与 `IsAllCardTypes`）。
--- C++: `scriptlib::card_is_card_type`
---@param cardType CardType 基础种类位掩码（`TYPE_*`）。
---@return boolean matched
function Card:IsCardType(cardType) end

--- 判定基础卡片种类是否**同时包含**指定位掩码的全部位。
--- C++: `scriptlib::card_is_all_card_types`
---@param cardType CardType 基础种类位掩码（`TYPE_*`）。
---@return boolean matched
function Card:IsAllCardTypes(cardType) end

--- 判定离场前在场上的基础种类是否包含指定位掩码的任一位。
--- C++: `scriptlib::card_is_previous_card_type_onfield`
---@param cardType CardType 基础种类位掩码（`TYPE_*`）。
---@return boolean matched
function Card:IsPreviousCardTypeOnField(cardType) end

--- 判定当前种类是否包含指定位掩码的**任一位**（多标志全匹配请用 `IsAllTypes`）。
--- C++: `scriptlib::card_is_type`
---@param cardType CardType 种类位掩码（`TYPE_*`）。
---@return boolean matched
function Card:IsType(cardType) end

--- 判定当前种类是否**同时包含**指定位掩码的全部位。
--- C++: `scriptlib::card_is_all_types`
---@param cardType CardType 种类位掩码（`TYPE_*`）。
---@return boolean matched
function Card:IsAllTypes(cardType) end

--- 判定融合素材种类是否包含指定位掩码的任一位。
--- C++: `scriptlib::card_is_fusion_type`
---@param cardType CardType 种类位掩码（`TYPE_*`）。
---@return boolean matched
function Card:IsFusionType(cardType) end

--- 判定同调素材种类是否包含指定位掩码的任一位。
--- C++: `scriptlib::card_is_synchro_type`
---@param cardType CardType 种类位掩码（`TYPE_*`）。
---@return boolean matched
function Card:IsSynchroType(cardType) end

--- 判定超量素材种类是否包含指定位掩码的任一位。
--- C++: `scriptlib::card_is_xyz_type`
---@param cardType CardType 种类位掩码（`TYPE_*`）。
---@return boolean matched
function Card:IsXyzType(cardType) end

--- 判定连接素材种类是否包含指定位掩码的任一位。
--- C++: `scriptlib::card_is_link_type`
---@param cardType CardType 种类位掩码（`TYPE_*`）。
---@return boolean matched
function Card:IsLinkType(cardType) end

--- 判定当前等级是否等于任意给定值。
--- C++: `scriptlib::card_is_level`
---@param value integer|nil 目标值（`nil` 会被跳过）。
---@param ... integer|nil 额外候选值（匹配任意一个即返回 `true`）。
---@return boolean matched
function Card:IsLevel(value, ...) end

--- 判定当前阶级是否等于任意给定值。
--- C++: `scriptlib::card_is_rank`
---@param value integer|nil 目标值（`nil` 会被跳过）。
---@param ... integer|nil 额外候选值（匹配任意一个即返回 `true`）。
---@return boolean matched
function Card:IsRank(value, ...) end

--- 判定当前连接值是否等于任意给定值。
--- C++: `scriptlib::card_is_link`
---@param value integer|nil 目标值（`nil` 会被跳过）。
---@param ... integer|nil 额外候选值（匹配任意一个即返回 `true`）。
---@return boolean matched
function Card:IsLink(value, ...) end

--- 判定当前攻击力是否等于任意给定值（非怪兽且无 `EFFECT_PRE_MONSTER` 时恒返回 `false`）。
--- C++: `scriptlib::card_is_attack`
---@param value integer|nil 目标值（`nil` 会被跳过）。
---@param ... integer|nil 额外候选值（匹配任意一个即返回 `true`）。
---@return boolean matched
function Card:IsAttack(value, ...) end

--- 判定当前守备力是否等于任意给定值（原本连接怪兽或非怪兽且无 `EFFECT_PRE_MONSTER` 时恒返回 `false`）。
--- C++: `scriptlib::card_is_defense`
---@param value integer|nil 目标值（`nil` 会被跳过）。
---@param ... integer|nil 额外候选值（匹配任意一个即返回 `true`）。
---@return boolean matched
function Card:IsDefense(value, ...) end

--- 判定当前种族是否包含指定位掩码的任一位。
--- C++: `scriptlib::card_is_race`
---@param race Race 种族位掩码（`RACE_*`）。
---@return boolean matched
function Card:IsRace(race) end

--- 判定作为连接素材时对指定玩家视为的种族是否包含任一位。
--- C++: `scriptlib::card_is_link_race`
---@param race Race 种族位掩码。
---@param player? Player 视角玩家（默认 `reason_player`）。
---@return boolean matched
function Card:IsLinkRace(race, player) end

--- 判定当前属性是否包含指定位掩码的任一位。
--- C++: `scriptlib::card_is_attribute`
---@param attribute Attribute 属性位掩码（`ATTRIBUTE_*`）。
---@return boolean matched
function Card:IsAttribute(attribute) end

--- 判定作为融合素材时对指定玩家视为的属性是否包含任一位。
--- C++: `scriptlib::card_is_fusion_attribute`
---@param attribute Attribute 属性位掩码。
---@param player? Player 视角玩家（默认 `reason_player`）。
---@return boolean matched
function Card:IsFusionAttribute(attribute, player) end

--- 判定作为连接素材时对指定玩家视为的属性是否包含任一位。
--- C++: `scriptlib::card_is_link_attribute`
---@param attribute Attribute 属性位掩码。
---@param player? Player 视角玩家（默认 `reason_player`）。
---@return boolean matched
function Card:IsLinkAttribute(attribute, player) end

--- 判定当前属性是否拥有除 `attribute` 外的任意有效属性（`ATTRIBUTE_ALL & ~attribute`）。
--- C++: `scriptlib::card_is_non_attribute`
---@param attribute Attribute 要排除的属性位掩码。
---@return boolean matched
function Card:IsNonAttribute(attribute) end

--- 判定此卡是否为额外卡组怪兽。
--- C++: `scriptlib::card_is_extra_deck_monster`
---@return boolean result
function Card:IsExtraDeckMonster() end

--- 判定当前原因是否包含指定位掩码的**任一位**（全匹配请用 `IsAllReasons`）。
--- C++: `scriptlib::card_is_reason`
---@param reason Reason 原因位掩码（`REASON_*`）。
---@return boolean matched
function Card:IsReason(reason) end

--- 判定当前原因是否**同时包含**指定位掩码的全部位。
--- C++: `scriptlib::card_is_all_reasons`
---@param reason Reason 原因位掩码（`REASON_*`）。
---@return boolean matched
function Card:IsAllReasons(reason) end

--- 判定召唤类型是否**全部包含**指定位掩码（注意：为全位精确匹配，非任一位匹配）。
--- C++: `scriptlib::card_is_summon_type`
---@param summonType SummonType 召唤类型位掩码（`SUMMON_TYPE_*`）。
---@return boolean matched
function Card:IsSummonType(summonType) end

--- 判定召唤前所在区域是否包含指定位掩码的任一位。
--- C++: `scriptlib::card_is_summon_location`
---@param location Location 区域位掩码（`LOCATION_*`）。
---@return boolean matched
function Card:IsSummonLocation(location) end

--- 判定召唤玩家是否等于 `player`。
--- C++: `scriptlib::card_is_summon_player`
---@param player Player 玩家编号。
---@return boolean matched
function Card:IsSummonPlayer(player) end

--- 判定卡片状态是否包含指定 `STATUS_*` 位掩码的任一位。
--- C++: `scriptlib::card_is_status`
---@param status Status 状态位掩码（`STATUS_*`）。
---@return boolean matched
function Card:IsStatus(status) end

--- 判定作为同调怪兽 `sc` 素材时能否被视为非调整怪兽。
--- C++: `scriptlib::card_is_not_tuner`
---@param sc Card 目标同调怪兽。
---@return boolean result
function Card:IsNotTuner(sc) end

--- 判定作为同调怪兽 `sc` 素材时能否被视为调整怪兽。
--- C++: `scriptlib::card_is_tuner`
---@param sc Card 目标同调怪兽。
---@return boolean result
function Card:IsTuner(sc) end

--- 判定卡片原本效果属性是否满足过滤函数 `filter`。
--- C++: `scriptlib::card_is_original_effect_property`
---@param filter EffectFilterFunction 效果过滤函数。
---@return boolean matched
function Card:IsOriginalEffectProperty(filter) end

--- 判定卡片当前效果属性是否满足过滤函数 `filter`。
--- C++: `scriptlib::card_is_effect_property`
---@param filter EffectFilterFunction 效果过滤函数。
---@return boolean matched
function Card:IsEffectProperty(filter) end

--- 设置或清除卡片状态位（处于 `STATUS_COPYING_EFFECT` 时忽略）。
--- C++: `scriptlib::card_set_status`
---@param status Status 状态位掩码（`STATUS_*`）。
---@param enable boolean `true` 置位，`false` 清除。
---@return nil
function Card:SetStatus(status, enable) end

--- 判定二重怪兽是否已处于再度召唤状态（`EFFECT_DUAL_STATUS`）。
--- C++: `scriptlib::card_is_dual_state`
---@return boolean result
function Card:IsDualState() end

--- 赋予卡片二重再度召唤状态（`EFFECT_DUAL_STATUS`）。
--- C++: `scriptlib::card_enable_dual_state`
---@return nil
function Card:EnableDualState() end

--- 设置卡片上显示的回合计数。
--- C++: `scriptlib::card_set_turn_counter`
---@param count integer 回合计数值。
---@return nil
function Card:SetTurnCounter(count) end

--- 取得卡片当前显示的回合计数。
--- C++: `scriptlib::card_get_turn_counter`
---@return integer count
function Card:GetTurnCounter() end

--- 设置该卡的召唤素材集合；传 `nil` 清空素材。
--- C++: `scriptlib::card_set_material`
---@param materials Group|nil 素材集合，传 `nil` 清空。
---@return nil
function Card:SetMaterial(materials) end

--- 取得该卡的召唤素材卡片集合。
--- C++: `scriptlib::card_get_material`
---@return Group group
function Card:GetMaterial() end

--- 取得该卡的召唤素材数量。
--- C++: `scriptlib::card_get_material_count`
---@return integer value
function Card:GetMaterialCount() end

--- 取得当前装备到此卡上的装备卡集合。
--- C++: `scriptlib::card_get_equip_group`
---@return Group group
function Card:GetEquipGroup() end

--- 取得装备到此卡上的装备卡数量。
--- C++: `scriptlib::card_get_equip_count`
---@return integer value
function Card:GetEquipCount() end

--- 取得该装备卡当前装备的目标怪兽。
--- C++: `scriptlib::card_get_equip_target`
---@return Card|nil card
function Card:GetEquipTarget() end

--- 取得该装备卡之前装备的目标怪兽。
--- C++: `scriptlib::card_get_pre_equip_target`
---@return Card|nil card
function Card:GetPreviousEquipTarget() end

--- 检查 `target` 是否符合该装备卡的装备限制及同盟互斥限制。
--- C++: `scriptlib::card_check_equip_target`
---@param target Card 候选装备目标怪兽。
---@return boolean valid
function Card:CheckEquipTarget(target) end

--- 检查 `target` 是否符合该同盟卡的装备限制及新旧同盟互斥限制。
--- C++: `scriptlib::card_check_union_target`
---@param target Card 候选同盟装备目标怪兽。
---@return boolean valid
function Card:CheckUnionTarget(target) end

--- 返回装备在该卡上的新同盟与旧同盟数量（双返回值）。
--- C++: `scriptlib::card_get_union_count`
---@return integer newUnionCount 新规则同盟怪兽数量。
---@return integer oldUnionCount 旧规则同盟怪兽数量。
function Card:GetUnionCount() end

--- 取得该卡持有的超量素材集合。
--- C++: `scriptlib::card_get_overlay_group`
---@return Group group
function Card:GetOverlayGroup() end

--- 取得该卡持有的超量素材数量。
--- C++: `scriptlib::card_get_overlay_count`
---@return integer value
function Card:GetOverlayCount() end

--- 取得持有该超量素材的超量怪兽。
--- C++: `scriptlib::card_get_overlay_target`
---@return Card|nil card
function Card:GetOverlayTarget() end

--- 检查玩家能否因 `reason` 从该卡取除至少 `count` 个超量素材。
--- C++: `scriptlib::card_check_remove_overlay_card`
---@param player Player 玩家编号（0 或 1）。
---@param count integer 需要取除的数量。
---@param reason Reason 原因位掩码。
---@return boolean|nil canRemove 能否取除（`player` 非 0/1 无返回值）。
function Card:CheckRemoveOverlayCard(player, count, reason) end

--- [协程挂起] 让玩家从该卡取除 `min..max` 个超量素材并返回实际取除数量。
--- C++: `scriptlib::card_remove_overlay_card`
---@param player Player 玩家编号（0 或 1）。
---@param min integer 最少取除数。
---@param max integer 最多取除数。
---@param reason Reason 原因位掩码。
---@return integer|nil removed 实际取除数量（`player` 非 0/1 无返回值）。
function Card:RemoveOverlayCard(player, min, max, reason) end

--- 取得本回合被该卡攻击过的怪兽集合（不含直接攻击）。
--- C++: `scriptlib::card_get_attacked_group`
---@return Group group
function Card:GetAttackedGroup() end

--- 取得本回合攻击记录数（含直接攻击）。
--- C++: `scriptlib::card_get_attacked_group_count`
---@return integer value
function Card:GetAttackedGroupCount() end

--- 取得本回合已完成的攻击次数。
--- C++: `scriptlib::card_get_attacked_count`
---@return integer value
function Card:GetAttackedCount() end

--- 取得本回合与该卡战斗过的怪兽集合。
--- C++: `scriptlib::card_get_battled_group`
---@return Group group
function Card:GetBattledGroup() end

--- 取得本回合战斗过的记录数。
--- C++: `scriptlib::card_get_battled_group_count`
---@return integer value
function Card:GetBattledGroupCount() end

--- 取得本回合攻击宣言次数。
--- C++: `scriptlib::card_get_attack_announced_count`
---@return integer value
function Card:GetAttackAnnouncedCount() end

--- 判定此卡本回合是否进行过直接攻击。
--- C++: `scriptlib::card_is_direct_attacked`
---@return boolean directAttacked
function Card:IsDirectAttacked() end

--- 建立此卡对 `target` 的持续取对象关联。
--- C++: `scriptlib::card_set_card_target`
---@param target Card 目标卡片。
---@return nil
function Card:SetCardTarget(target) end

--- 取得该卡持续取对象的所有卡片集合。
--- C++: `scriptlib::card_get_card_target`
---@return Group group
function Card:GetCardTarget() end

--- 取得该卡持续取对象的第一张卡片。
--- C++: `scriptlib::card_get_first_card_target`
---@return Card|nil card
function Card:GetFirstCardTarget() end

--- 取得该卡持续取对象的卡片数量。
--- C++: `scriptlib::card_get_card_target_count`
---@return integer value
function Card:GetCardTargetCount() end

--- 判定此卡是否正在持续取 `target` 为对象。
--- C++: `scriptlib::card_is_has_card_target`
---@param target Card 目标卡片。
---@return boolean targeted
function Card:IsHasCardTarget(target) end

--- 解除此卡对 `target` 的持续取对象关联。
--- C++: `scriptlib::card_cancel_card_target`
---@param target Card 目标卡片。
---@return nil
function Card:CancelCardTarget(target) end

--- 取得正在持续取该卡为对象的所有卡片集合。
--- C++: `scriptlib::card_get_owner_target`
---@return Group group
function Card:GetOwnerTarget() end

--- 取得正在持续取该卡为对象的卡片数量。
--- C++: `scriptlib::card_get_owner_target_count`
---@return integer value
function Card:GetOwnerTargetCount() end

--- 多值返回该卡上的全部 `EFFECT_TYPE_ACTIVATE` 效果（无则返回 0 个值）。
--- C++: `scriptlib::card_get_activate_effect`
---@return Effect ... 卡片发动效果列表。
function Card:GetActivateEffect() end

--- 查找首个可发动的 `EFFECT_TYPE_ACTIVATE` 效果；`copyInfo=false` 或自由时点返回单个 `Effect`，否则返回 7 个值含触发事件信息。
--- C++: `scriptlib::card_check_activate_effect`
---@param neglectCon boolean 是否忽略发动条件。
---@param neglectCost boolean 是否忽略发动代价。
---@param copyInfo boolean 为 `true` 且非 `EVENT_FREE_CHAIN` 时额外返回触发事件 6 元组。
---@return Effect|nil effect 首个可发动的发动效果；无则返回 `nil`。
---@return Group|nil eg 事件卡组（仅 `copyInfo=true` 且非自由时点）。
---@return Player? ep 事件玩家。
---@return integer? ev 事件值。
---@return Effect|nil re 触发效果。
---@return Reason? r 触发原因。
---@return Player? rp 触发玩家。
function Card:CheckActivateEffect(neglectCon, neglectCost, copyInfo) end

--- 查询此卡上的 `EFFECT_TUNER_MATERIAL_LIMIT` 限制信息（存在时返回 5 个值，否则无返回值）。
--- C++: `scriptlib::card_get_tuner_limit`
---@return Effect|nil effect 限制效果。
---@return function|nil targetFilter 额外过滤函数。
---@return Location|nil location 区域限制值。
---@return integer? min 最少张数（默认 `0`）。
---@return integer? max 最多张数（默认 `99`）。
function Card:GetTunerLimit() end

--- 查询怪兽区此卡的手牌同调效果信息（存在时返回 4 个值，否则无返回值）。
--- C++: `scriptlib::card_get_hand_synchro`
---@return Effect|nil effect 效果对象。
---@return function|nil targetFilter 额外过滤函数。
---@return integer? min 最少张数（默认 `0`）。
---@return integer? max 最多张数（默认 `99`）。
function Card:GetHandSynchro() end

--- 将效果 `effect` 注册到当前卡片（注意：仅 2 个参数，不存在第 3 个 `player` 参数）。
--- C++: `scriptlib::card_register_effect`
---@param effect Effect 要注册的效果对象。
---@param forced? boolean 是否强制注册（忽略卡片对该效果的免疫，默认 `false`）。
---@return integer|nil effectId 注册成功返回效果 ID；效果已有 handler 返回 `-1`；卡片免疫且非强制或 owner 为 temp_card 时无返回值（`nil`）。
function Card:RegisterEffect(effect, forced) end

--- 多值返回该卡上所有生效且匹配 `code` 的效果（可按玩家过滤次数限制）；无匹配时返回 `nil`。
--- C++: `scriptlib::card_is_has_effect`
---@param code EffectCode 要查询的效果代码。
---@param checkPlayer? Player 传 `0` 或 `1` 时仅返回对该玩家仍满足次数限制的效果（默认不检查）。
---@return Effect ... 匹配的全部效果对象；无匹配时返回 `nil`。
function Card:IsHasEffect(code, checkPlayer) end

--- 按重置类型 `resetType`（如 `RESET_CODE`、`RESET_EVENT`）重置该卡的效果。
--- C++: `scriptlib::card_reset_effect`
---@param resetCode integer 重置编码或事件掩码。
---@param resetType Reset 重置类型（`RESET_*`）。
---@return nil
function Card:ResetEffect(resetCode, resetType) end

--- 返回该卡上匹配 `code` 的生效效果数量。
--- C++: `scriptlib::card_get_effect_count`
---@param code EffectCode 效果代码。
---@return integer count
function Card:GetEffectCount(code) end

--- 为该卡注册不可无效的标记效果（`FlagEffect`）并返回新效果对象。
--- C++: `scriptlib::card_register_flag_effect`
---@param code integer 标记代码（低位掩码 `MAX_CARD_ID`）。
---@param reset Reset 重置条件（含 `RESET_PHASE` 且未指定回合归属时自动补全双方回合）。
---@param property EffectProperty 属性标志（自动附加 `EFFECT_FLAG_CANNOT_DISABLE`）。
---@param count integer 重置计数（`0` 自动修正为 `1`）。
---@param label? integer 初始整型标签（默认 `0`）。
---@param description? integer 客户端显示提示描述 ID（默认 `0`）。
---@return Effect flagEffect 注册好的标记效果。
function Card:RegisterFlagEffect(code, reset, property, count, label, description) end

--- 返回该卡上指定标记效果的数量。
--- C++: `scriptlib::card_get_flag_effect`
---@param code integer 标记代码。
---@return integer count
function Card:GetFlagEffect(code) end

--- 移除该卡上所有指定 `code` 的标记效果。
--- C++: `scriptlib::card_reset_flag_effect`
---@param code integer 标记代码。
---@return nil
function Card:ResetFlagEffect(code) end

--- 修改该卡上找到的第一个指定 `code` 标记效果的整型标签。
--- C++: `scriptlib::card_set_flag_effect_label`
---@param code integer 标记代码。
---@param label integer 新的标签值。
---@return boolean updated 找到并修改返回 `true`，未找到返回 `false`。
function Card:SetFlagEffectLabel(code, label) end

--- 多值返回该卡上所有指定 `code` 标记效果的首个标签（无标签项返回 `0`）；无该标记效果时返回 `nil`。
--- C++: `scriptlib::card_get_flag_effect_label`
---@param code integer 标记代码。
---@return integer ... 标签值列表；不存在该标记效果时返回 `nil`。
function Card:GetFlagEffectLabel(code) end

--- 建立此卡指向 `rcard` 的卡片关联。
--- C++: `scriptlib::card_create_relation`
---@param rcard Card 目标卡片。
---@param reset Reset 解除关联的重置标志。
---@return nil
function Card:CreateRelation(rcard, reset) end

--- 解除此卡指向 `rcard` 的卡片关联。
--- C++: `scriptlib::card_release_relation`
---@param rcard Card 目标卡片。
---@return nil
function Card:ReleaseRelation(rcard) end

--- 建立此卡与效果 `effect` 的关联。
--- C++: `scriptlib::card_create_effect_relation`
---@param effect Effect 效果对象。
---@return nil
function Card:CreateEffectRelation(effect) end

--- 解除此卡与效果 `effect` 的关联。
--- C++: `scriptlib::card_release_effect_relation`
---@param effect Effect 效果对象。
---@return nil
function Card:ReleaseEffectRelation(effect) end

--- 清空此卡的所有效果关联。
--- C++: `scriptlib::card_clear_effect_relation`
---@return nil
function Card:ClearEffectRelation() end

--- 判定此卡是否仍与效果 `effect` 保持关联。
--- C++: `scriptlib::card_is_relate_to_effect`
---@param effect Effect 效果对象。
---@return boolean related
function Card:IsRelateToEffect(effect) end

--- 判定此卡是否仍与指定连锁项保持关联（省略或 `0` 取当前连锁栈顶）。
--- C++: `scriptlib::card_is_relate_to_chain`
---@param chainIndex? integer 连锁序号（1-based；省略、`0` 或越界取当前连锁栈顶）。
---@return boolean related
function Card:IsRelateToChain(chainIndex) end

--- 判定此卡是否与 `rcard` 存在卡片关联。
--- C++: `scriptlib::card_is_relate_to_card`
---@param rcard Card 目标卡片。
---@return boolean related
function Card:IsRelateToCard(rcard) end

--- 判定此卡是否仍与当前战斗关联（比对 `fieldid_r`）。
--- C++: `scriptlib::card_is_relate_to_battle`
---@return boolean related
function Card:IsRelateToBattle() end

--- 复制卡号 `code` 的脚本效果到此卡，返回复制记录 ID。
--- C++: `scriptlib::card_copy_effect`
---@param code CardCode 要复制效果的源卡号。
---@param reset Reset 重置条件。
---@param count? integer 重置计数（默认 `1`）。
---@return integer copyId 复制 ID（可用于 `ResetEffect(copyId, RESET_COPY)`）。
function Card:CopyEffect(code, reset, count) end

--- 用卡号 `code` 的脚本效果替换此卡原有效果，返回替换记录 ID。
--- C++: `scriptlib::card_replace_effect`
---@param code CardCode 替换源卡号。
---@param reset Reset 重置条件。
---@param count? integer 重置计数（默认 `1`）。
---@return integer replaceId
function Card:ReplaceEffect(code, reset, count) end

--- 为卡片附加不可无效、不可复制的苏生限制（`EFFECT_REVIVE_LIMIT`）。
--- C++: `scriptlib::card_enable_revive_limit`
---@return nil
function Card:EnableReviveLimit() end

--- 标记卡片已完成正规召唤手续（置位 `STATUS_PROC_COMPLETE`）。
--- C++: `scriptlib::card_complete_procedure`
---@return nil
function Card:CompleteProcedure() end

--- 判定卡片是否带有 `STATUS_DISABLED` 无效状态。
--- C++: `scriptlib::card_is_disabled`
---@return boolean result
function Card:IsDisabled() end

--- 判定卡片是否可被破坏；传入 `effect` 时按该效果检查（若传参则不得显式传 `nil`）。
--- C++: `scriptlib::card_is_destructable`
---@param effect? Effect 可选的效果对象（省略时检查一般可破坏性）。
---@return boolean destructable
function Card:IsDestructable(effect) end

--- 判定卡片自身属性是否为**可通常召唤的怪兽卡**（注意：检查当前能否执行通召请用 `Card:IsSummonable`）。
--- C++: `scriptlib::card_is_summonable`
---@return boolean result
function Card:IsSummonableCard() end

--- 判定卡片自身属性是否为**可特殊召唤的卡片**（区别于 `Card:IsSpecialSummonable`）。
--- C++: `scriptlib::card_is_special_summonable_card`
---@return boolean result
function Card:IsSpecialSummonableCard() end

--- 判定卡片本身是否为可按指定方式融合召唤的融合怪兽卡。
--- C++: `scriptlib::card_is_fusion_summonable_card`
---@param summonType? SummonType 召唤类型（默认 `0`）。
---@return boolean result
function Card:IsFusionSummonableCard(summonType) end

--- 判定当前 `reason_player` 能否按规则手续特殊召唤此卡（如同调/超量/连接/规则特召）。
--- C++: `scriptlib::card_is_special_summonable`
---@param summonType? SummonType 召唤类型限制（默认 `0`）。
---@return boolean summonable
function Card:IsSpecialSummonable(summonType) end

--- 判定能否在限定调整/素材组/素材数范围内同调召唤此卡（非同调怪兽或伤害步骤返回 `false`）。
--- C++: `scriptlib::card_is_synchro_summonable`
---@param tuner Card|nil 必选调整怪兽（必填，不限定则传 `nil`）。
---@param materials? Group|nil 限定候选素材集合（默认 `nil`）。
---@param minc? integer 最少素材数（默认 `0`）。
---@param maxc? integer 最多素材数（默认 `0`）。
---@return boolean summonable
function Card:IsSynchroSummonable(tuner, materials, minc, maxc) end

--- 判定能否在限定素材组/素材数范围内超量召唤此卡（非超量怪兽或伤害步骤返回 `false`）。
--- C++: `scriptlib::card_is_xyz_summonable`
---@param materials Group|nil 限定候选素材集合（必填，不限定则传 `nil`）。
---@param minc? integer 最少素材数（默认 `0`）。
---@param maxc? integer 最多素材数（默认 `0`）。
---@return boolean summonable
function Card:IsXyzSummonable(materials, minc, maxc) end

--- 判定能否在限定素材组/必选卡/素材数范围内连接召唤此卡（非连接怪兽或伤害步骤返回 `false`）。
--- C++: `scriptlib::card_is_link_summonable`
---@param materials Group|nil 限定候选素材集合（必填，不限定则传 `nil`）。
---@param mustCard? Card|nil 必选素材卡（默认 `nil`）。
---@param minc? integer 最少素材数（默认 `0`）。
---@param maxc? integer 最多素材数（默认 `0`）。
---@return boolean summonable
function Card:IsLinkSummonable(materials, mustCard, minc, maxc) end

--- 判定当前 `reason_player` 能否立即通常召唤此卡（伤害步骤返回 `false`；注意与 `Card:IsSummonableCard` 区分）。
--- C++: `scriptlib::card_is_can_be_summoned`
---@param ignoreCount boolean 是否忽略通常召唤次数限制。
---@param effect Effect|nil 赋予额外通常召唤的效果（必填，无则显式传 `nil`）。
---@param minTribute? integer 最少解放数（默认 `0`）。
---@param zone? Zone 允许召唤到的怪兽区位掩码（默认 `0x1f`）。
---@return boolean summonable
function Card:IsSummonable(ignoreCount, effect, minTribute, zone) end

--- 判定当前 `reason_player` 能否将此卡通常盖放到怪兽区（伤害步骤直接返回 `false`）。
--- C++: `scriptlib::card_is_msetable`
---@param ignoreCount boolean 是否忽略通常召唤次数限制。
---@param effect Effect|nil 赋予额外通常盖放的效果（必填，无则显式传 `nil`）。
---@param minTribute? integer 最少解放数（默认 `0`）。
---@param zone? Zone 允许放置的怪兽区位掩码（默认 `0x1f`）。
---@return boolean setable
function Card:IsMSetable(ignoreCount, effect, minTribute, zone) end

--- 判定当前 `reason_player` 能否将此卡盖放到魔陷区。
--- C++: `scriptlib::card_is_ssetable`
---@param ignoreField? boolean 是否忽略魔陷区空位限制（默认 `false`）。
---@return boolean setable
function Card:IsSSetable(ignoreField) end

--- 判定此卡能否由效果 `effect` 按指定参数特殊召唤。
--- C++: `scriptlib::card_is_can_be_special_summoned`
---@param effect Effect 发动特殊召唤的效果。
---@param summonType SummonType 召唤类型（如 `0`、`SUMMON_TYPE_FUSION`）。
---@param sumPlayer Player 执行特殊召唤的玩家。
---@param nocheck boolean 是否无视召唤条件。
---@param nolimit boolean 是否无视苏生限制。
---@param position? Position 表示形式（默认 `POS_FACEUP`）。
---@param toPlayer? Player 召唤到哪方场上（默认 `sumPlayer`）。
---@param zone? Zone 允许召唤到的格子掩码（默认 `0xff`）。
---@return boolean summonable
function Card:IsCanBeSpecialSummoned(effect, summonType, sumPlayer, nocheck, nolimit, position, toPlayer, zone) end

--- 判定此卡能否直接放置到场上（检查非 `STATUS_FORBIDDEN` 且不违反唯一限制）。
--- C++: `scriptlib::card_is_can_be_placed_on_field`
---@param toPlayer? Player 目标玩家（默认 `reason_player`）。
---@param toLocation? Location 目标区域（默认 `LOCATION_SZONE`）。
---@return boolean placeable
function Card:IsCanBePlacedOnField(toPlayer, toLocation) end

--- 判定能否将此卡加入手牌。
--- C++: `scriptlib::card_is_able_to_hand`
---@param player? Player 操作玩家（默认 `reason_player`）。
---@return boolean able
function Card:IsAbleToHand(player) end

--- 判定当前 `reason_player` 能否将此卡送回卡组。
--- C++: `scriptlib::card_is_able_to_deck`
---@return boolean result
function Card:IsAbleToDeck() end

--- 判定当前 `reason_player` 能否将此卡送回额外卡组。
--- C++: `scriptlib::card_is_able_to_extra`
---@return boolean result
function Card:IsAbleToExtra() end

--- 判定当前 `reason_player` 能否将此卡送去墓地。
--- C++: `scriptlib::card_is_able_to_grave`
---@return boolean result
function Card:IsAbleToGrave() end

--- 判定能否以指定表示形式与原因除外此卡。
--- C++: `scriptlib::card_is_able_to_remove`
---@param player? Player 操作玩家（默认 `reason_player`）。
---@param position? Position 除外表示形式（默认 `POS_FACEUP`）。
---@param reason? Reason 除外原因（默认 `REASON_EFFECT`）。
---@return boolean able
function Card:IsAbleToRemove(player, position, reason) end

--- 判定能否作为 Cost 返回手牌。
--- C++: `scriptlib::card_is_able_to_hand_as_cost`
---@return boolean result
function Card:IsAbleToHandAsCost() end

--- 判定能否作为 Cost 返回卡组。
--- C++: `scriptlib::card_is_able_to_deck_as_cost`
---@return boolean result
function Card:IsAbleToDeckAsCost() end

--- 判定能否作为 Cost 返回额外卡组。
--- C++: `scriptlib::card_is_able_to_extra_as_cost`
---@return boolean result
function Card:IsAbleToExtraAsCost() end

--- 判定能否作为 Cost 返回卡组或额外卡组（按卡片种类自动分派）。
--- C++: `scriptlib::card_is_able_to_deck_or_extra_as_cost`
---@return boolean result
function Card:IsAbleToDeckOrExtraAsCost() end

--- 判定能否作为 Cost 送去墓地。
--- C++: `scriptlib::card_is_able_to_grave_as_cost`
---@return boolean result
function Card:IsAbleToGraveAsCost() end

--- 判定能否作为 Cost 以指定表示形式除外此卡。
--- C++: `scriptlib::card_is_able_to_remove_as_cost`
---@param position? Position 除外表示形式（默认 `POS_FACEUP`）。
---@return boolean able
function Card:IsAbleToRemoveAsCost(position) end

--- 判定能否因非召唤原因解放此卡。
--- C++: `scriptlib::card_is_releasable`
---@param reason? Reason 解放原因（默认 `REASON_COST`）。
---@return boolean releasable
function Card:IsReleasable(reason) end

--- 判定能否被当前 `reason_effect` 因效果解放。
--- C++: `scriptlib::card_is_releasable_by_effect`
---@return boolean result
function Card:IsReleasableByEffect() end

--- 判定手牌中的此卡能否被丢弃（`REASON_COST` 时额外检查 `EFFECT_CANNOT_USE_AS_COST`）。
--- C++: `scriptlib::card_is_discardable`
---@param reason? Reason 丢弃原因（默认 `REASON_COST`）。
---@return boolean discardable
function Card:IsDiscardable(reason) end

--- 判定此卡当前是否具备攻击能力。
--- C++: `scriptlib::card_is_attackable`
---@return boolean result
function Card:IsAttackable() end

--- 判定当前战斗攻击者（`core.attacker`）能否继续连续攻击。
--- C++: `scriptlib::card_is_chain_attackable`
---@param ac? integer 允许攻击次数阈值（默认 `2`；`0` 表示不限次数）。
---@param monsterOnly? boolean 是否限定仅对怪兽连击（默认 `false`）。
---@return boolean chainAttackable
function Card:IsChainAttackable(ac, monsterOnly) end

--- 判定当前是否为表侧表示（`POS_FACEUP`）。
--- C++: `scriptlib::card_is_faceup`
---@return boolean result
function Card:IsFaceup() end

--- 判定是否为表侧表示，或位于手牌/墓地/卡组任一区域。
--- C++: `scriptlib::card_is_faceup_ex`
---@return boolean result
function Card:IsFaceupEx() end

--- 判定当前是否为攻击表示（`POS_ATTACK`）。
--- C++: `scriptlib::card_is_attack_pos`
---@return boolean result
function Card:IsAttackPos() end

--- 判定当前是否为里侧表示（`POS_FACEDOWN`）。
--- C++: `scriptlib::card_is_facedown`
---@return boolean result
function Card:IsFacedown() end

--- 判定当前是否为守备表示（`POS_DEFENSE`）。
--- C++: `scriptlib::card_is_defense_pos`
---@return boolean result
function Card:IsDefensePos() end

--- 判定当前表示形式是否匹配指定位掩码。
--- C++: `scriptlib::card_is_position`
---@param position Position 表示形式位掩码（`POS_*`）。
---@return boolean matched
function Card:IsPosition(position) end

--- 判定上一个表示形式是否匹配指定位掩码。
--- C++: `scriptlib::card_is_pre_position`
---@param position Position 表示形式位掩码（`POS_*`）。
---@return boolean matched
function Card:IsPreviousPosition(position) end

--- 判定当前控制者是否等于 `player`。
--- C++: `scriptlib::card_is_controler`
---@param player Player 玩家编号。
---@return boolean matched
function Card:IsControler(player) end

--- 判定上一个控制者是否等于 `player`。
--- C++: `scriptlib::card_is_pre_controler`
---@param player Player 玩家编号。
---@return boolean matched
function Card:IsPreviousControler(player) end

--- 判定是否位于场上（且未被视为不在场）。
--- C++: `scriptlib::card_is_onfield`
---@return boolean result
function Card:IsOnField() end

--- 判定当前位置是否匹配 `location`（被视为不在场时恒返回 `false`；支持区分场地/灵摆区）。
--- C++: `scriptlib::card_is_location`
---@param location Location 区域位掩码（`LOCATION_*`）。
---@return boolean matched
function Card:IsLocation(location) end

--- 判定上一个位置是否匹配 `location`。
--- C++: `scriptlib::card_is_pre_location`
---@param location Location 区域位掩码（`LOCATION_*`）。
---@return boolean matched
function Card:IsPreviousLocation(location) end

--- 判定当前等级 `> 0` 且 `<= level`。
--- C++: `scriptlib::card_is_level_below`
---@param value integer 等级上限。
---@return boolean matched
function Card:IsLevelBelow(value) end

--- 判定当前等级 `> 0` 且 `>= level`。
--- C++: `scriptlib::card_is_level_above`
---@param value integer 等级下限。
---@return boolean matched
function Card:IsLevelAbove(value) end

--- 判定当前阶级 `> 0` 且 `<= rank`。
--- C++: `scriptlib::card_is_rank_below`
---@param value integer 阶级上限。
---@return boolean matched
function Card:IsRankBelow(value) end

--- 判定当前阶级 `> 0` 且 `>= rank`。
--- C++: `scriptlib::card_is_rank_above`
---@param value integer 阶级下限。
---@return boolean matched
function Card:IsRankAbove(value) end

--- 判定当前连接值 `> 0` 且 `<= link`。
--- C++: `scriptlib::card_is_link_below`
---@param value integer 连接值上限。
---@return boolean matched
function Card:IsLinkBelow(value) end

--- 判定当前连接值 `> 0` 且 `>= link`。
--- C++: `scriptlib::card_is_link_above`
---@param value integer 连接值下限。
---@return boolean matched
function Card:IsLinkAbove(value) end

--- 判定为怪兽且当前攻击力 `>= 0` 且 `<= atk`（`?` 攻 `-2` 返回 `false`）。
--- C++: `scriptlib::card_is_attack_below`
---@param value integer 攻击力上限。
---@return boolean matched
function Card:IsAttackBelow(value) end

--- 判定为怪兽且当前攻击力 `>= atk`。
--- C++: `scriptlib::card_is_attack_above`
---@param value integer 攻击力下限。
---@return boolean matched
function Card:IsAttackAbove(value) end

--- 判定为非连接怪兽且当前守备力 `>= 0` 且 `<= def`。
--- C++: `scriptlib::card_is_defense_below`
---@param value integer 守备力上限。
---@return boolean matched
function Card:IsDefenseBelow(value) end

--- 判定为非连接怪兽且当前守备力 `>= def`。
--- C++: `scriptlib::card_is_defense_above`
---@param value integer 守备力下限。
---@return boolean matched
function Card:IsDefenseAbove(value) end

--- 判定是否为拥有等级的怪兽（非超量、非连接且无 `STATUS_NO_LEVEL`）。
--- C++: `scriptlib::card_is_has_level`
---@return boolean result
function Card:IsHasLevel() end

--- 判定是否为拥有守备力的怪兽（非连接怪兽）。
--- C++: `scriptlib::card_is_has_defense`
---@return boolean result
function Card:IsHasDefense() end

--- 判定是否公开（底层检查 `POS_FACEUP`，常用于手牌公开判断）。
--- C++: `scriptlib::card_is_public`
---@return boolean result
function Card:IsPublic() end

--- 判定是否带有 `STATUS_FORBIDDEN` 状态。
--- C++: `scriptlib::card_is_forbidden`
---@return boolean result
function Card:IsForbidden() end

--- 仅检查卡片自身是否允许转移控制权（**不检查**对方怪兽区空位；含空位检查请用 `Card:IsControlerCanBeChanged`）。
--- C++: `scriptlib::card_is_able_to_change_controler`
---@return boolean result
function Card:IsAbleToChangeControler() end

--- 检查此卡的控制权能否实际转移给对方（含对方怪兽区空位与区域掩码检查）。
--- C++: `scriptlib::card_is_controler_can_be_changed`
---@param ignoreMZone? boolean 是否忽略怪兽区空位检查（默认 `false`）。
---@param zone? Zone 目标怪兽区位掩码（默认 `0xff`）。
---@return boolean changeable
function Card:IsControlerCanBeChanged(ignoreMZone, zone) end

--- 向此卡放置指定数量指示物（若卡片不受当前 `reason_effect` 影响则直接返回 `false`）。
--- C++: `scriptlib::card_add_counter`
---@param counterType CounterType 指示物类型编码。
---@param count integer 放置数量。
---@param singly? boolean 是否允许按上限逐个放置至满额（默认 `false`）。
---@return boolean added
function Card:AddCounter(counterType, count, singly) end

--- [可能协程挂起] 从此卡取除指示物；`counterType == 0` 时同步清空该卡所有指示物，否则挂起处理后返回是否成功。
--- C++: `scriptlib::card_remove_counter`
---@param player Player 执行取除的玩家编号。
---@param counterType CounterType 指示物类型（传 `0` 清空所有指示物）。
---@param count integer 取除数量。
---@param reason Reason 原因位掩码。
---@return boolean removed
function Card:RemoveCounter(player, counterType, count, reason) end

--- 取得该卡上的指示物数量；注意传 `0` 时返回的是指示物的**种类数**而非总数。
--- C++: `scriptlib::card_get_counter`
---@param counterType CounterType 指示物类型（传 `0` 返回不同指示物的种类数）。
---@return integer count
function Card:GetCounter(counterType) end

--- 允许此卡放置指定类型指示物（注册 `EFFECT_COUNTER_PERMIT | counterType`）。
--- C++: `scriptlib::card_enable_counter_permit`
---@param counterType CounterType 指示物类型编码。
---@param range? Location 生效区域（默认：怪兽为 `LOCATION_MZONE`，非怪兽为 `LOCATION_SZONE | LOCATION_FZONE`）。
---@param condition? ConditionFunction 可选条件回调。
---@return nil
function Card:EnableCounterPermit(counterType, range, condition) end

--- 为该卡设置某类指示物的持有数量上限。
--- C++: `scriptlib::card_set_counter_limit`
---@param counterType CounterType 指示物类型编码。
---@param limit integer 上限数量。
---@return nil
function Card:SetCounterLimit(counterType, limit) end

--- 判定能否因效果改变表示形式。
--- C++: `scriptlib::card_is_can_change_position`
---@return boolean result
function Card:IsCanChangePosition() end

--- 判定能否变为里侧表示。
--- C++: `scriptlib::card_is_can_turn_set`
---@return boolean result
function Card:IsCanTurnSet() end

--- 判定此卡能否放置指定种类和数量的指示物。
--- C++: `scriptlib::card_is_can_add_counter`
---@param counterType CounterType 指示物类型编码。
---@param count integer 数量。
---@param singly? boolean 是否允许按上限逐个放置（默认 `false`）。
---@param location? Location 预检的目标区域（默认 `0` 使用当前区域）。
---@return boolean canAdd
function Card:IsCanAddCounter(counterType, count, singly, location) end

--- 判定玩家能否从此卡取除指定种类与数量的指示物。
--- C++: `scriptlib::card_is_can_remove_counter`
---@param player Player 玩家编号（0 或 1）。
---@param counterType CounterType 指示物类型编码。
---@param count integer 取除数量。
---@param reason Reason 原因位掩码。
---@return boolean|nil canRemove `player` 非 0/1 时无返回值。
function Card:IsCanRemoveCounter(player, counterType, count, reason) end

--- 判定此卡是否允许持有指定种类的指示物。
--- C++: `scriptlib::card_is_can_have_counter`
---@param counterType CounterType 指示物类型编码。
---@return boolean allowed
function Card:IsCanHaveCounter(counterType) end

--- 判定此卡能否作为超量素材重叠到超量怪兽下方。
--- C++: `scriptlib::card_is_can_overlay`
---@param player? Player 操作玩家（默认 `reason_player`）。
---@return boolean allowed
function Card:IsCanOverlay(player) end

--- 判定此卡能否作为融合怪兽 `fusionCard` 的融合素材。
--- C++: `scriptlib::card_is_can_be_fusion_material`
---@param fusionCard? Card|nil 目标融合怪兽（默认 `nil`）。
---@param summonType? SummonType 召唤类型（默认 `SUMMON_TYPE_FUSION`）。
---@return boolean allowed
function Card:IsCanBeFusionMaterial(fusionCard, summonType) end

--- 判定此卡能否作为同调怪兽 `synchroCard`（及指定调整 `tuner`）的同调素材；注意传入参数时不可显式传 `nil`。
--- C++: `scriptlib::card_is_can_be_synchro_material`
---@param synchroCard? Card 目标同调怪兽（可选；传参时必须为 `Card`）。
---@param tuner? Card 搭配的调整怪兽（可选；传参时必须为 `Card`）。
---@return boolean allowed
function Card:IsCanBeSynchroMaterial(synchroCard, tuner) end

--- 判定此卡能否作为仪式怪兽 `ritualCard` 的仪式素材（参数必传，不限定时传 `nil`）。
--- C++: `scriptlib::card_is_can_be_ritual_material`
---@param ritualCard Card|nil 目标仪式怪兽（必填，不限定则显式传 `nil`）。
---@return boolean allowed
function Card:IsCanBeRitualMaterial(ritualCard) end

--- 判定此卡能否作为超量怪兽 `xyzCard` 的超量素材（参数必传，不限定时传 `nil`）。
--- C++: `scriptlib::card_is_can_be_xyz_material`
---@param xyzCard Card|nil 目标超量怪兽（必填，不限定则显式传 `nil`）。
---@return boolean allowed
function Card:IsCanBeXyzMaterial(xyzCard) end

--- 判定此卡能否作为连接怪兽 `linkCard` 的连接素材（参数必传，不限定时传 `nil`）。
--- C++: `scriptlib::card_is_can_be_link_material`
---@param linkCard Card|nil 目标连接怪兽（必填，不限定则显式传 `nil`）。
---@return boolean allowed
function Card:IsCanBeLinkMaterial(linkCard) end

--- 检查给定素材集合中是否满足该融合怪兽的融合素材要求。
--- C++: `scriptlib::card_check_fusion_material`
---@param materials? Group|nil 候选素材集合（默认 `nil`）。
---@param mustCard? Card|nil 必选素材卡（默认 `nil`）。
---@param chkf? Player 检查玩家标志（默认 `PLAYER_NONE`）。
---@param notMaterial? boolean 是否按非融合素材规则检查（默认 `false`）。
---@return boolean valid
function Card:CheckFusionMaterial(materials, mustCard, chkf, notMaterial) end

--- 判定此卡能否作为 `fusionCard` 的融合素材代替怪兽。
--- C++: `scriptlib::card_check_fusion_substitute`
---@param fusionCard Card 目标融合怪兽。
---@return boolean substitute
function Card:CheckFusionSubstitute(fusionCard) end

--- 判定此卡是否不受效果 `effect` 影响。
--- C++: `scriptlib::card_is_immune_to_effect`
---@param effect Effect 效果对象。
---@return boolean immune
function Card:IsImmuneToEffect(effect) end

--- 判定此卡能否被效果 `effect` 无效化。
--- C++: `scriptlib::card_is_can_be_disabled_by_effect`
---@param effect Effect 无效来源效果。
---@param isMonsterEffect? boolean 是否按怪兽效果无效判定（默认 `true`）。
---@return boolean disablable
function Card:IsCanBeDisabledByEffect(effect, isMonsterEffect) end

--- 判定此卡能否成为效果 `effect` 的取对象目标；传参时必须为 `Effect`（不可显式传 `nil`）。
--- C++: `scriptlib::card_is_can_be_effect_target`
---@param effect? Effect 效果对象（省略时取当前 `reason_effect`）。
---@return boolean targetable
function Card:IsCanBeEffectTarget(effect) end

--- 判定此卡能否成为攻击怪兽 `attacker` 的攻击对象。
--- C++: `scriptlib::card_is_can_be_battle_target`
---@param attacker Card 攻击宣言怪兽。
---@return boolean targetable
function Card:IsCanBeBattleTarget(attacker) end

--- 陷阱怪兽辅助方法：在当前连锁期间赋予 `EFFECT_PRE_MONSTER` 及非零的种类/属性/种族/等级/攻守。
--- C++: `scriptlib::card_add_monster_attribute`
---@param cardType CardType 怪兽种类位掩码。
---@param attribute? Attribute 属性（默认 `0` 不设置）。
---@param race? Race 种族（默认 `0` 不设置）。
---@param level? integer 等级（默认 `0` 不设置）。
---@param atk? integer 攻击力（默认 `0` 不设置）。
---@param def? integer 守备力（默认 `0` 不设置）。
---@return nil
function Card:AddMonsterAttribute(cardType, attribute, race, level, atk, def) end

--- 控制魔陷卡发动结算后是否取消自动送墓（`true` 留在场上，`false` 强制标记送墓）。
--- C++: `scriptlib::card_cancel_to_grave`
---@param cancel? boolean 是否取消送墓（默认 `true`）。
---@return nil
function Card:CancelToGrave(cancel) end

--- 返回通常召唤此卡所需的最小与最大解放数（双返回值）。
--- C++: `scriptlib::card_get_tribute_requirement`
---@return integer minTribute 最少祭品数。
---@return integer maxTribute 最多祭品数。
function Card:GetTributeRequirement() end

--- 取得当前战斗的对手怪兽（攻击者返回防御者，反之亦然；非战斗中返回 `nil`）。
--- C++: `scriptlib::card_get_battle_target`
---@return Card|nil card
function Card:GetBattleTarget() end

--- 返回此卡当前可攻击的对象怪兽集合，以及是否可以直接攻击（双返回值）。
--- C++: `scriptlib::card_get_attackable_target`
---@return Group targets 可攻击的对象怪兽集合。
---@return boolean canDirectAttack 是否允许直接攻击。
function Card:GetAttackableTarget() end

--- 向客户端发送单卡提示（`MSG_CARD_HINT`；`hintType >= CHINT_DESC_ADD` 时会被忽略）。
--- C++: `scriptlib::card_set_hint`
---@param hintType CardHint 卡片提示类型（`CHINT_*`）。
---@param value integer 提示值。
---@return nil
function Card:SetHint(hintType, value) end

--- 将卡组中的此卡翻转为表侧守备表示（在卡组顶时同步通知客户端 `MSG_DECK_TOP`）。
--- C++: `scriptlib::card_reverse_in_deck`
---@return nil
function Card:ReverseInDeck() end

--- 设置此卡在场上只能表侧存在 1 张的唯一性限制。
--- C++: `scriptlib::card_set_unique_onfield`
---@param selfFlag integer 己方场上是否受限（`0` 或 `1`）。
---@param opponentFlag integer 对方场上是否受限（`0` 或 `1`）。
---@param codeOrFunc integer|FilterFunction 唯一标识码或冲突判定函数。
---@param location? Location 限制生效的场上区域（默认 `LOCATION_ONFIELD`）。
---@return nil
function Card:SetUniqueOnField(selfFlag, opponentFlag, codeOrFunc, location) end

--- 检查此卡放置在指定玩家指定区域时是否**不发生**唯一性冲突（无冲突返回 `true`）。
--- C++: `scriptlib::card_check_unique_onfield`
---@param checkPlayer Player 检查玩家编号。
---@param checkLocation? Location 目标区域（默认 `LOCATION_ONFIELD`）。
---@param ignoreCard? Card|nil 忽略检查的一张卡（默认 `nil`）。
---@return boolean noConflict 无冲突返回 `true`，发生冲突返回 `false`。
function Card:CheckUniqueOnField(checkPlayer, checkLocation, ignoreCard) end

--- 按传入的 0..N 个卡号逐个重置该卡上关联的无效效果（`RESET_CARD`）。
--- C++: `scriptlib::card_reset_negate_effect`
---@param ... CardCode 要重置的卡号列表。
---@return nil
function Card:ResetNegateEffect(...) end

--- 临时设定卡片的假设属性（`ASSUME_*`），会在顶层脚本调用结束后自动恢复。
--- C++: `scriptlib::card_assume_prop`
---@param assumeType AssumeType 假设属性类型（`ASSUME_*`）。
---@param assumeValue integer 假设属性值。
---@return nil
function Card:AssumeProperty(assumeType, assumeValue) end

--- 为卡片注册“1 回合只能特殊召唤 1 次”限制并启用 `GLOBALFLAG_SPSUMMON_ONCE`（复制效果期间忽略）。
--- C++: `scriptlib::card_set_spsummon_once`
---@param code CardCode 共享特召次数限制的卡号。
---@return nil
function Card:SetSPSummonOnce(code) end

-------------------------------------------------------------------------------
-- Effect API (55 functions)
-------------------------------------------------------------------------------

--- 创建绑定到持有者卡片的效果对象（`effect_owner` 默认初始化为当前 `reason_player`）。
--- C++: `scriptlib::effect_new`
---@param owner Card 效果所属（创建者）卡片。
---@return Effect effect 新建的效果对象。
function Effect.CreateEffect(owner) end

--- 创建不绑定具体卡片的全局效果对象（`owner` 为内部 `temp_card`，`effect_owner` 为 `0`）。
--- C++: `scriptlib::effect_newex`
---@return Effect effect 新建的全局效果对象。
function Effect.GlobalEffect() end

--- 深拷贝当前效果对象（包含已注册的回调句柄与参数配置）。
--- C++: `scriptlib::effect_clone`
---@return Effect effect 克隆出的新效果对象。
function Effect:Clone() end

--- 将当前效果从其所属卡片或场地中移除并重置（未注册到有效 handler 时为空操作）。
--- C++: `scriptlib::effect_reset`
---@return nil
function Effect:Reset() end

--- 取得效果在当前 duel 内部分配的唯一递增 ID。
--- C++: `scriptlib::effect_get_field_id`
---@return integer fieldId 效果内部 ID。
function Effect:GetFieldID() end

--- 设置效果的提示描述 ID（通常由 `aux.Stringid(code, idx)` 生成的整数，不接受字符串）。
--- C++: `scriptlib::effect_set_description`
---@param description integer 描述文本 ID。
---@return nil
function Effect:SetDescription(description) end

--- 设置效果代码（如 `EFFECT_UPDATE_ATTACK` 或触发事件 `EVENT_SUMMON_SUCCESS`）。
--- C++: `scriptlib::effect_set_code`
---@param code EffectCode 效果代码或事件代码。
---@return nil
function Effect:SetCode(code) end

--- 设置效果生效或可发动时卡片自身必须处于的区域范围。
--- C++: `scriptlib::effect_set_range`
---@param range Location 区域位掩码（`LOCATION_*`）。
---@return nil
function Effect:SetRange(range) end

--- 设置效果影响双方玩家的区域/对象范围（并清除 `EFFECT_FLAG_ABSOLUTE_TARGET` 标志）。
--- C++: `scriptlib::effect_set_target_range`
---@param selfRange Location|integer 控制者侧的目标范围（或玩家效果数值）。
---@param opponentRange Location|integer 对手侧的目标范围（或玩家效果数值）。
---@return nil
function Effect:SetTargetRange(selfRange, opponentRange) end

--- 以固定玩家视角设置绝对目标范围（自动置位 `EFFECT_FLAG_ABSOLUTE_TARGET`）。
--- C++: `scriptlib::effect_set_absolute_range`
---@param player Player 基准玩家编号（`0` 时不互换，非 `0` 时交换双方范围）。
---@param selfRange Location|integer `player` 侧的目标范围。
---@param opponentRange Location|integer `1 - player` 侧的目标范围。
---@return nil
function Effect:SetAbsoluteRange(player, selfRange, opponentRange) end

--- 设置效果的次数限制；注意原生层仅接收 2 个参数，誓约/决斗一次等标志直接按位或到 `code` 中。
--- 注意：C++ 仅读取 2 个参数（不含 `self`），不存在第 3 个 `flags` 参数；`code == EFFECT_COUNT_CODE_CHAIN` 会自动附带 `EFFECT_COUNT_CODE_SINGLE`。
--- C++: `scriptlib::effect_set_count_limit`
---@param count integer 允许次数（传入 `0` 时自动修正为 `1`）。
---@param code? CountLimitCode 共享计数编码，可与 `EFFECT_COUNT_CODE_OATH`、`EFFECT_COUNT_CODE_DUEL`、`EFFECT_COUNT_CODE_SINGLE`、`EFFECT_COUNT_CODE_CHAIN` 按位或（默认 `0`）。
---@return nil
function Effect:SetCountLimit(count, code) end

--- 设置效果的重置时机与计数。
--- C++: `scriptlib::effect_set_reset`
---@param reset Reset 重置条件位掩码（含 `RESET_PHASE` 且未指定己方/对方回合时自动补全双方回合）。
---@param count? integer 触发重置所需次数（省略或 `0` 时自动修正为 `1`）。
---@return nil
function Effect:SetReset(reset, count) end

--- 设置效果类型（会自动补齐关联标志，例如 `EFFECT_TYPE_ACTIVATE` 自动设置魔陷/场地/手牌范围，起动/诱发即时自动附加 `EFFECT_TYPE_FIELD`）。
--- C++: `scriptlib::effect_set_type`
---@param type EffectType 效果类型位掩码（`EFFECT_TYPE_*`）。
---@return nil
function Effect:SetType(type) end

--- 设置效果属性位掩码（支持主标志 `flag1` 与扩展标志 `flag2`，保留内部标志位不被覆盖）。
--- C++: `scriptlib::effect_set_property`
---@param flag1 EffectProperty 主属性标志（`EFFECT_FLAG_*`）。
---@param flag2? EffectProperty2 扩展属性标志（`EFFECT_FLAG2_*`，默认 `0`）。
---@return nil
function Effect:SetProperty(flag1, flag2) end

--- 清空并写入一个或多个整型自定义标签（label），后续由 `Effect:GetLabel()` 按序多值返回。
--- C++: `scriptlib::effect_set_label`
---@param label integer 首个标签值。
---@param ... integer 额外标签值。
---@return nil
function Effect:SetLabel(label, ...) end

--- 保存与效果关联的对象引用（支持 `Card`、`Effect`、`Group` 或 `nil` 清空，传入其他类型会抛 Lua 错误）。
--- C++: `scriptlib::effect_set_label_object`
---@param object Card|Effect|Group|nil 关联对象，传 `nil` 清空。
---@return nil
function Effect:SetLabelObject(object) end

--- 设置效果分类位掩码（用于无效判定及处理类型标注）。
--- C++: `scriptlib::effect_set_category`
---@param category EffectCategory 效果分类位掩码（`CATEGORY_*`）。
---@return nil
function Effect:SetCategory(category) end

--- 设置客户端询问发动的提示时点（`opponentTiming` 省略时默认等于 `selfTiming`）。
--- C++: `scriptlib::effect_set_hint_timing`
---@param selfTiming Timing 己方回合提示时点（`TIMING_*`）。
---@param opponentTiming? Timing 对方回合提示时点（默认同 `selfTiming`）。
---@return nil
function Effect:SetHintTiming(selfTiming, opponentTiming) end

--- 设置效果发动或适用的条件回调函数。
--- C++: `scriptlib::effect_set_condition`
---@param condition ConditionFunction 条件回调。
---@return nil
function Effect:SetCondition(condition) end

--- 设置效果目标选择回调（启动/发动型为 `TargetFunction`，永续/适用型为 `EffectTargetFilter`）。
--- C++: `scriptlib::effect_set_target`
---@param target TargetFunction|EffectTargetFilter 目标选择或适用对象过滤回调。
---@return nil
function Effect:SetTarget(target) end

--- 设置效果发动代价（Cost）回调。
--- C++: `scriptlib::effect_set_cost`
---@param cost CostFunction 代价支付回调。
---@return nil
function Effect:SetCost(cost) end

--- 设置效果固定值（整型/布尔）或动态计算回调函数。
--- C++: `scriptlib::effect_set_value`
---@param value integer|boolean|ValueFunction 固定数值、布尔值或动态计算函数。
---@return nil
function Effect:SetValue(value) end

--- 设置效果处理回调；允许传入 `nil` 以清除已设置的处理函数。
--- C++: `scriptlib::effect_set_operation`
---@param operation OperationFunction|nil 效果处理函数，传 `nil` 清空。
---@return nil
function Effect:SetOperation(operation) end

--- 设置效果的拥有玩家（仅接受 `0` 或 `1`，省略时按 `0` 处理）。
--- C++: `scriptlib::effect_set_owner_player`
---@param player? Player 拥有玩家编号（`0` 或 `1`，默认 `0`）。
---@return nil
function Effect:SetOwnerPlayer(player) end

--- 取得效果的描述文本 ID。
--- C++: `scriptlib::effect_get_description`
---@return integer description
function Effect:GetDescription() end

--- 取得效果代码（`EFFECT_*` / `EVENT_*`）。
--- C++: `scriptlib::effect_get_code`
---@return EffectCode code
function Effect:GetCode() end

--- 取得效果类型位掩码（`EFFECT_TYPE_*`）。
--- C++: `scriptlib::effect_get_type`
---@return EffectType type
function Effect:GetType() end

--- 取得效果的主属性标志与扩展属性标志（双返回值）。
--- C++: `scriptlib::effect_get_property`
---@return EffectProperty flag1 主属性标志（`EFFECT_FLAG_*`）。
---@return EffectProperty2 flag2 扩展属性标志（`EFFECT_FLAG2_*`）。
function Effect:GetProperty() end

--- 返回效果保存的全部整型标签（多返回值）；若尚未设置任何标签，返回单个 `0`。
--- C++: `scriptlib::effect_get_label`
---@return integer ... 全部标签整数值（未设置时为 `0`）。
function Effect:GetLabel() end

--- 取得 `Effect:SetLabelObject` 保存的关联对象引用。
--- C++: `scriptlib::effect_get_label_object`
---@return Card|Effect|Group|nil object 关联对象；未设置或失效时返回 `nil`。
function Effect:GetLabelObject() end

--- 取得效果分类位掩码（`CATEGORY_*`）。
--- C++: `scriptlib::effect_get_category`
---@return EffectCategory category
function Effect:GetCategory() end

--- 取得效果生效/发动范围（`LOCATION_*`）。
--- C++: `scriptlib::effect_get_range`
---@return Location range
function Effect:GetRange() end

--- 取得效果的创建者卡片（`owner`）。
--- C++: `scriptlib::effect_get_owner`
---@return Card|nil owner
function Effect:GetOwner() end

--- 取得效果当前注册所附着的卡片（`handler`）。
--- C++: `scriptlib::effect_get_handler`
---@return Card|nil handler
function Effect:GetHandler() end

--- 取得已设置的条件回调函数。
--- C++: `scriptlib::effect_get_condition`
---@return ConditionFunction|nil condition
function Effect:GetCondition() end

--- 取得已设置的目标选择/适用过滤回调函数。
--- C++: `scriptlib::effect_get_target`
---@return TargetFunction|EffectTargetFilter|nil target
function Effect:GetTarget() end

--- 取得已设置的代价回调函数。
--- C++: `scriptlib::effect_get_cost`
---@return CostFunction|nil cost
function Effect:GetCost() end

--- 取得已设置的固定整型值或动态值回调函数。
--- C++: `scriptlib::effect_get_value`
---@return ValueFunction|integer value 若设置的是函数则返回函数，否则返回整型值。
function Effect:GetValue() end

--- 取得已设置的处理回调函数。
--- C++: `scriptlib::effect_get_operation`
---@return OperationFunction|nil operation
function Effect:GetOperation() end

--- 取得效果发动时所视为的卡片种类位掩码。
--- C++: `scriptlib::effect_get_active_type`
---@return CardType activeType
function Effect:GetActiveType() end

--- 判定效果发动时所视为的卡片种类是否包含指定位掩码的任一位。
--- C++: `scriptlib::effect_is_active_type`
---@param cardType CardType 卡片种类位掩码（`TYPE_*`）。
---@return boolean matched
function Effect:IsActiveType(cardType) end

--- 取得效果的拥有玩家编号。
--- C++: `scriptlib::effect_get_owner_player`
---@return Player player
function Effect:GetOwnerPlayer() end

--- 取得效果当前附着卡片的控制者编号。
--- C++: `scriptlib::effect_get_handler_player`
---@return Player player
function Effect:GetHandlerPlayer() end

--- 检查效果是否具备指定的属性标志（主标志 `flag1` 与扩展标志 `flag2` 均按非零项检查）。
--- C++: `scriptlib::effect_is_has_property`
---@param flag1 EffectProperty 要检查的主属性位掩码（传 `0` 跳过主标志检查）。
---@param flag2? EffectProperty2 要检查的扩展属性位掩码（默认 `0` 跳过）。
---@return boolean matched
function Effect:IsHasProperty(flag1, flag2) end

--- 检查效果分类是否包含指定位掩码的任一位。
--- C++: `scriptlib::effect_is_has_category`
---@param category EffectCategory 效果分类位掩码（`CATEGORY_*`）。
---@return boolean matched
function Effect:IsHasCategory(category) end

--- 检查效果类型是否包含指定位掩码的任一位。
--- C++: `scriptlib::effect_is_has_type`
---@param type EffectType 效果类型位掩码（`EFFECT_TYPE_*`）。
---@return boolean matched
function Effect:IsHasType(type) end

--- 检查效果范围是否包含指定区域位掩码的任一位。
--- C++: `scriptlib::effect_is_has_range`
---@param range Location 区域位掩码（`LOCATION_*`）。
---@return boolean matched
function Effect:IsHasRange(range) end

--- 判定指定玩家当前能否发动该效果（使用空事件检查）。
--- C++: `scriptlib::effect_is_activatable`
---@param player Player 发动玩家编号。
---@param neglectLoc? boolean 是否忽略位置限制（默认 `false`）。
---@param neglectTarget? boolean 是否忽略目标限制（默认 `false`）。
---@return boolean activatable
function Effect:IsActivatable(player, neglectLoc, neglectTarget) end

--- 判定该效果是否为入连锁的发动型效果（`EFFECT_TYPES_CHAIN_LINK`）。
--- C++: `scriptlib::effect_is_activated`
---@return boolean activated
function Effect:IsActivated() end

--- 返回该效果的 `cost_checked` 标记（常用于复制效果时跳过或检查 Cost）。
--- C++: `scriptlib::effect_is_cost_checked`
---@return boolean checked
function Effect:IsCostChecked() end

--- 设置该效果的 `cost_checked` 标记。
--- C++: `scriptlib::effect_set_cost_check`
---@param costCheck boolean 是否标记为需检查 Cost。
---@return nil
function Effect:SetCostCheck(costCheck) end

--- 取得该效果上次发动时所在的区域。
--- C++: `scriptlib::effect_get_activate_location`
---@return Location location
function Effect:GetActivateLocation() end

--- 取得该效果上次发动时所在的区域序号。
--- C++: `scriptlib::effect_get_activate_sequence`
---@return integer sequence
function Effect:GetActivateSequence() end

--- 检查指定玩家是否仍满足该效果的次数限制。
--- C++: `scriptlib::effect_check_count_limit`
---@param player Player 玩家编号。
---@return boolean allowed
function Effect:CheckCountLimit(player) end

--- 手动消耗指定玩家对该效果的剩余次数限制。
--- C++: `scriptlib::effect_use_count_limit`
---@param player Player 玩家编号。
---@param count? integer 消耗次数（默认 `1`）。
---@param oathOnly? boolean 为 `true` 时仅在该效果带有 `EFFECT_COUNT_CODE_OATH` 时才扣减（默认 `false`）。
---@return nil
function Effect:UseCountLimit(player, count, oathOnly) end

-------------------------------------------------------------------------------
-- Group API (40 functions)
-------------------------------------------------------------------------------

--- 创建一个空的卡片集合（默认在顶层脚本调用结束后自动回收，跨回调保存需调用 `KeepAlive`）。
--- C++: `scriptlib::group_new`
---@return Group group 新卡片集合。
function Group.CreateGroup() end

--- 标记集合跨脚本调用持久存活，避免被引擎自动回收（使用完毕须手动调用 `DeleteGroup` 释放；对只读集合无效）。
--- C++: `scriptlib::group_keep_alive`
---@return nil
function Group:KeepAlive() end

--- 解除 `KeepAlive` 标记并交回引擎回收（仅对已调用 `KeepAlive` 的集合有效）。
--- C++: `scriptlib::group_delete`
---@return nil
function Group:DeleteGroup() end

--- 复制当前集合的全部卡片引用到一个新集合。
--- C++: `scriptlib::group_clone`
---@return Group group 新卡片集合。
function Group:Clone() end

--- 从 0 个或多个卡片对象构造新集合（自动跳过其中的 `nil` 参数）。
--- C++: `scriptlib::group_from_cards`
---@param ... Card|nil 要加入集合的卡片（`nil` 会被忽略）。
---@return Group group 新卡片集合。
function Group.FromCards(...) end

--- 清空集合内所有卡片（只读集合忽略；会重置迭代游标）。
--- C++: `scriptlib::group_clear`
---@return nil
function Group:Clear() end

--- 向集合插入一张卡片（只读集合忽略；会重置迭代游标）。
--- C++: `scriptlib::group_add_card`
---@param card Card 要加入的卡片。
---@return nil
function Group:AddCard(card) end

--- 从集合移除一张卡片（只读集合忽略；会重置迭代游标）。
--- C++: `scriptlib::group_remove_card`
---@param card Card 要移除的卡片。
---@return nil
function Group:RemoveCard(card) end

--- 推进迭代游标并返回下一张卡片；必须先调用 `Group:GetFirst()`，否则在集合被修改后调用会抛 Lua 错误。
--- C++: `scriptlib::group_get_next`
---@return Card|nil card 下一张卡片；到达末尾时返回 `nil`。
function Group:GetNext() end

--- 重置迭代游标并返回集合中的第一张卡片。
--- C++: `scriptlib::group_get_first`
---@return Card|nil card 第一张卡片；空集合返回 `nil`。
function Group:GetFirst() end

--- 取得集合内卡片数量（等价于 `#group`）。
--- C++: `scriptlib::group_get_count`
---@return integer count 卡片数量。
function Group:GetCount() end

--- 长度元方法 `#group`，返回集合内卡片数量。
--- C++: `scriptlib::group_get_count`
---@return integer count 卡片数量。
function Group:__len() end

--- 按过滤函数筛选集合并返回新集合；注意第 2 参 `excluded` 必须显式传入（无排除时传 `nil`）。
--- C++: `scriptlib::group_filter`
---@param filter FilterFunction 过滤函数 `fun(c: Card, ...): boolean|integer`。
---@param excluded Card|Group|nil 排除的单卡或集合（必填，无排除时显式传 `nil`）。
---@param ... any 透传给 `filter` 的附加参数。
---@return Group filtered 满足条件的新集合。
function Group:Filter(filter, excluded, ...) end

--- 统计集合中满足过滤条件的卡片数量；`excluded` 必须显式传入（无排除时传 `nil`）。
--- C++: `scriptlib::group_filter_count`
---@param filter FilterFunction 过滤函数。
---@param excluded Card|Group|nil 排除的单卡或集合（必填，无排除时显式传 `nil`）。
---@param ... any 透传给 `filter` 的附加参数。
---@return integer count 满足条件的卡片数。
function Group:FilterCount(filter, excluded, ...) end

--- [协程挂起] 按过滤条件让玩家从集合中选择 `min..max` 张卡片。
--- C++: `scriptlib::group_filter_select`
---@param player Player 执行选择的玩家编号（0 或 1）。
---@param filter FilterFunction 过滤函数。
---@param min integer 最少选择张数。
---@param max integer 最多选择张数。
---@param excluded Card|Group|nil 排除的单卡或集合（必填，无排除时显式传 `nil`）。
---@param ... any 透传给 `filter` 的附加参数。
---@return Group|nil selected 选中的卡片集合（`player` 非 0/1 时无返回值）。
function Group:FilterSelect(player, filter, min, max, excluded, ...) end

--- [协程挂起] 让玩家从集合中不可取消地选择 `min..max` 张卡片。
--- 注意：第 4 参是排除项 `excluded`，不是 `cancelable`！需要可取消选择请改用 `Group:CancelableSelect`。
--- C++: `scriptlib::group_select`
---@param player Player 执行选择的玩家编号（0 或 1）。
---@param min integer 最少选择张数。
---@param max integer 最多选择张数。
---@param excluded Card|Group|nil 排除的单卡或集合（必填，无排除时显式传 `nil`）。
---@return Group|nil selected 选中的卡片集合（`player` 非 0/1 时无返回值）。
function Group:Select(player, min, max, excluded) end

--- [协程挂起] 在待选集合（`self`）与已选可反选集合（`unselectGroup`）之间交互点选一张卡（常用于多步自定义组合选择）。
--- C++: `scriptlib::group_select_unselect`
---@param unselectGroup Group|nil 已选中且允许反选的集合（须与 `self` 无交集，可为 `nil`）。
---@param player Player 执行选择的玩家编号（0 或 1）。
---@param finishable? boolean 是否允许点“完成”结束选择（默认 `false`）。
---@param cancelable? boolean 是否允许点“取消”中止选择（默认 `false`）。
---@param min? integer 显示给客户端的最少数量提示（默认 `1`）。
---@param max? integer 显示给客户端的最多数量提示（默认 `1`）。
---@return Card|nil card 玩家点击的卡片；点完成/取消或两集合为空/有交集时返回 `nil`。
function Group:SelectUnselect(unselectGroup, player, finishable, cancelable, min, max) end

--- 从集合中随机抽取最多 `count` 张卡片并向客户端发送 `MSG_RANDOM_SELECTED`。
--- C++: `scriptlib::group_random_select`
---@param player Player 触发随机选择的玩家编号。
---@param count integer 随机选取张数（大于集合大小时自动截断为集合大小）。
---@return Group selected 随机选中的卡片集合。
function Group:RandomSelect(player, count) end

--- [协程挂起] 让玩家从集合中可取消地选择 `min..max` 张卡片；玩家取消时返回 `nil`。
--- C++: `scriptlib::group_cancelable_select`
---@param player Player 执行选择的玩家编号（0 或 1）。
---@param min integer 最少选择张数。
---@param max integer 最多选择张数。
---@param excluded Card|Group|nil 排除的单卡或集合（必填，无排除时显式传 `nil`）。
---@return Group|nil selected 选中的卡片集合；玩家取消或 `player` 非 0/1 时返回 `nil`。
function Group:CancelableSelect(player, min, max, excluded) end

--- 检查集合中是否存在至少 `count` 张满足过滤条件的卡片；`excluded` 必须显式传入（无排除时传 `nil`）。
--- C++: `scriptlib::group_is_exists`
---@param filter FilterFunction 过滤函数。
---@param count integer 所需最少张数。
---@param excluded Card|Group|nil 排除的单卡或集合（必填，无排除时显式传 `nil`）。
---@param ... any 透传给 `filter` 的附加参数。
---@return boolean exists 是否满足数量条件。
function Group:IsExists(filter, count, excluded, ...) end

--- 检查集合（并计入 `Duel.SetSelectedCard` 的必选卡）中能否选出 `min..max` 张卡使求值函数总和恰好等于 `acc`（调用后会清空 `must_select_cards`）。
--- C++: `scriptlib::group_check_with_sum_equal`
---@param valueFunc SumValueFunction 卡片求值函数（支持高低 16 位双值编码）。
---@param acc integer 目标总和。
---@param min integer 最少选取张数。
---@param max integer 最多选取张数。
---@param ... any 透传给 `valueFunc` 的附加参数。
---@return boolean possible 是否存在合法组合。
function Group:CheckWithSumEqual(valueFunc, acc, min, max, ...) end

--- [协程挂起] 让玩家从集合中选出 `min..max` 张卡（含必选卡）使总和恰好等于 `acc`（`max` 不得超过 255；无合法解时直接返回空集合）。
--- C++: `scriptlib::group_select_with_sum_equal`
---@param player Player 执行选择的玩家编号（0 或 1）。
---@param valueFunc SumValueFunction 卡片求值函数。
---@param acc integer 目标总和。
---@param min integer 最少选取张数。
---@param max integer 最多选取张数（上限 255）。
---@param ... any 透传给 `valueFunc` 的附加参数。
---@return Group|nil selected 选中的卡片集合（不含先前置入的 `must_select_cards`；`player` 非 0/1 无返回值）。
function Group:SelectWithSumEqual(player, valueFunc, acc, min, max, ...) end

--- 检查集合（含必选卡）中是否存在最小超额组合使求值总和大于等于 `acc`（调用后会清空 `must_select_cards`）。
--- C++: `scriptlib::group_check_with_sum_greater`
---@param valueFunc SumValueFunction 卡片求值函数。
---@param acc integer 目标下限总和。
---@param ... any 透传给 `valueFunc` 的附加参数。
---@return boolean possible 是否存在合法组合。
function Group:CheckWithSumGreater(valueFunc, acc, ...) end

--- [协程挂起] 让玩家从集合中选出最小超额组合使求值总和大于等于 `acc`（无合法解时返回空集合）。
--- C++: `scriptlib::group_select_with_sum_greater`
---@param player Player 执行选择的玩家编号（0 或 1）。
---@param valueFunc SumValueFunction 卡片求值函数。
---@param acc integer 目标下限总和。
---@param ... any 透传给 `valueFunc` 的附加参数。
---@return Group|nil selected 选中的卡片集合（`player` 非 0/1 无返回值）。
function Group:SelectWithSumGreater(player, valueFunc, acc, ...) end

--- 按求值函数找出集合中取值最小的所有卡片集合及对应的最小值（双返回值；空集合无返回值）。
--- C++: `scriptlib::group_get_min_group`
---@param valueFunc SumValueFunction 卡片整型求值函数。
---@param ... any 透传给 `valueFunc` 的附加参数。
---@return Group|nil minGroup 取得最小值的卡片集合（空集合返回 `nil`）。
---@return integer|nil minValue 最小数值。
function Group:GetMinGroup(valueFunc, ...) end

--- 按求值函数找出集合中取值最大的所有卡片集合及对应的最大值（双返回值；空集合无返回值）。
--- C++: `scriptlib::group_get_max_group`
---@param valueFunc SumValueFunction 卡片整型求值函数。
---@param ... any 透传给 `valueFunc` 的附加参数。
---@return Group|nil maxGroup 取得最大值的卡片集合（空集合返回 `nil`）。
---@return integer|nil maxValue 最大数值。
function Group:GetMaxGroup(valueFunc, ...) end

--- 对集合内每张卡调用求值函数并累加返回总和。
--- C++: `scriptlib::group_get_sum`
---@param valueFunc SumValueFunction 卡片整型求值函数（必须为函数）。
---@param ... any 透传给 `valueFunc` 的附加参数。
---@return integer sum 总和。
function Group:GetSum(valueFunc, ...) end

--- 统计集合内卡片经整型求值函数映射后的不同取值个数（如统计不同卡名/不同等级数）。
--- C++: `scriptlib::group_get_class_count`
---@param classFunc SumValueFunction 分类整型求值函数（如 `Card.GetCode`、`Card.GetLevel`）。
---@param ... any 透传给 `classFunc` 的附加参数。
---@return integer count 不同分类的数量。
function Group:GetClassCount(classFunc, ...) end

--- 原地从集合中移除所有满足过滤条件的卡片（只读集合忽略）；注意 `excluded` 仅接受 `Card|nil`（不接受 `Group`）。
--- C++: `scriptlib::group_remove`
---@param filter FilterFunction 过滤函数。
---@param excluded Card|nil 排除的单张卡片（必填，无排除时显式传 `nil`；不接受 `Group`）。
---@param ... any 透传给 `filter` 的附加参数。
---@return nil
function Group:Remove(filter, excluded, ...) end

--- 原地将 `other` 集合中的全部卡片并入当前集合（只读集合忽略；会重置迭代游标）。
--- C++: `scriptlib::group_merge`
---@param other Group 要合并进来的集合。
---@return nil
function Group:Merge(other) end

--- 原地从当前集合中减去 `other` 集合包含的全部卡片（只读集合忽略；会重置迭代游标）。
--- C++: `scriptlib::group_sub`
---@param other Group 要减去的集合。
---@return nil
function Group:Sub(other) end

--- 比较两个集合包含的卡片是否完全一致。
--- C++: `scriptlib::group_equal`
---@param other Group 要比较的集合。
---@return boolean equal
function Group:Equal(other) end

--- 检查当前集合是否包含指定卡片。
--- C++: `scriptlib::group_is_contains`
---@param card Card 要检查的卡片。
---@return boolean contains
function Group:IsContains(card) end

--- 在集合中查找第一张满足过滤函数的卡片；未找到时无返回值（`nil`）。
--- C++: `scriptlib::group_search_card`
---@param filter FilterFunction 过滤函数。
---@param ... any 透传给 `filter` 的附加参数。
---@return Card|nil card 命中的第一张卡片；未找到返回 `nil`。
function Group:SearchCard(filter, ...) end

--- 对集合内每张卡的整型求值结果按位或合并后统计置位比特数（popcount，常用于统计不同属性/种族数）。
--- C++: `scriptlib::group_get_bin_class_count`
---@param bitFunc SumValueFunction 返回位掩码的函数（如 `Card.GetAttribute`、`Card.GetRace`）。
---@param ... any 透传给 `bitFunc` 的附加参数。
---@return integer bitCount 合并后位掩码中 1 的个数。
function Group:GetBinClassCount(bitFunc, ...) end

--- 并集元方法 `a + b`：返回包含 `a` 与 `b` 全部卡片的新集合。
--- C++: `scriptlib::group_meta_add`
---@param lhs Card|Group 左操作数。
---@param rhs Card|Group 右操作数。
---@return Group union 新并集集合。
function Group.__add(lhs, rhs) end

--- 并集元方法 `a | b`（与 `a + b` 共享同一 C++ 实现）：返回包含 `a` 与 `b` 全部卡片的新集合。
--- C++: `scriptlib::group_meta_add`
---@param lhs Card|Group 左操作数。
---@param rhs Card|Group 右操作数。
---@return Group union 新并集集合。
function Group.__bor(lhs, rhs) end

--- 差集元方法 `a - b`：返回属于 `a` 但不属于 `b` 的新集合。
--- C++: `scriptlib::group_meta_sub`
---@param lhs Card|Group 左操作数。
---@param rhs Card|Group 右操作数。
---@return Group diff 新差集集合。
function Group.__sub(lhs, rhs) end

--- 交集元方法 `a & b`：返回同时属于 `a` 和 `b` 的新集合。
--- C++: `scriptlib::group_meta_band`
---@param lhs Card|Group 左操作数。
---@param rhs Card|Group 右操作数。
---@return Group intersection 新交集集合。
function Group.__band(lhs, rhs) end

--- 对称差集元方法 `a ~ b`：返回仅属于 `a` 或仅属于 `b`（不同时属于两者）的新集合。
--- C++: `scriptlib::group_meta_bxor`
---@param lhs Card|Group 左操作数。
---@param rhs Card|Group 右操作数。
---@return Group symDiff 新对称差集集合。
function Group.__bxor(lhs, rhs) end

-------------------------------------------------------------------------------
-- Duel API (229 functions)
-------------------------------------------------------------------------------

--- 按位或启用全局规则标志（`GLOBALFLAG_*`）。
--- C++: `scriptlib::duel_enable_global_flag`
---@param flag GlobalFlag 全局标志位掩码。
---@return nil
function Duel.EnableGlobalFlag(flag) end

--- 取得玩家当前 LP（`player` 非 0/1 无返回值）。
--- C++: `scriptlib::duel_get_lp`
---@param player Player 玩家编号（0 或 1）。
---@return integer|nil lp
function Duel.GetLP(player) end

--- 直接设置玩家 LP（负值钳位为 `0`）并向客户端广播 `MSG_LPUPDATE`。
--- C++: `scriptlib::duel_set_lp`
---@param player Player 玩家编号。
---@param lp integer 目标 LP。
---@return nil
function Duel.SetLP(player, lp) end

--- 判定 `player` 是否为当前回合玩家。
--- C++: `scriptlib::duel_is_turn_player`
---@param player Player 玩家编号。
---@return boolean isTurnPlayer
function Duel.IsTurnPlayer(player) end

--- 取得当前回合玩家编号（`0` 或 `1`）。
--- C++: `scriptlib::duel_get_turn_player`
---@return Player player
function Duel.GetTurnPlayer() end

--- 取得回合数：无参返回对局总回合数；传入 `player` 返回该玩家已进行的回合数。
--- C++: `scriptlib::duel_get_turn_count`
---@param player? Player 可选：玩家编号（0 或 1）。
---@return integer|nil turnCount
function Duel.GetTurnCount(player) end

--- 取得指定玩家抽卡阶段的通常抽卡张数。
--- C++: `scriptlib::duel_get_draw_count`
---@param player Player 玩家编号（0 或 1）。
---@return integer|nil drawCount
function Duel.GetDrawCount(player) end

--- 将效果注册为作用于指定玩家的场地/玩家效果。
--- C++: `scriptlib::duel_register_effect`
---@param effect Effect 效果对象。
---@param player Player 目标玩家编号（0 或 1）。
---@return nil
function Duel.RegisterEffect(effect, player) end

--- 为指定玩家注册标记效果（`FlagEffect`）并返回该效果对象。
--- C++: `scriptlib::duel_register_flag_effect`
---@param player Player 玩家编号（0 或 1）。
---@param code integer 标记代码。
---@param reset Reset 重置条件（含 `RESET_PHASE` 且未指定回合归属时自动补全双方回合）。
---@param property EffectProperty 属性标志。
---@param count integer 重置计数（`0` 自动修正为 `1`）。
---@param label? integer 初始标签值（默认 `0`）。
---@return Effect|nil flagEffect `player` 非 0/1 无返回值。
function Duel.RegisterFlagEffect(player, code, reset, property, count, label) end

--- 取得玩家身上匹配 `code` 的标记效果数量。
--- C++: `scriptlib::duel_get_flag_effect`
---@param player Player 玩家编号。
---@param code integer 标记代码。
---@return integer|nil count
function Duel.GetFlagEffect(player, code) end

--- 移除玩家身上所有匹配 `code` 的标记效果。
--- C++: `scriptlib::duel_reset_flag_effect`
---@param player Player 玩家编号。
---@param code integer 标记代码。
---@return nil
function Duel.ResetFlagEffect(player, code) end

--- 修改玩家首个匹配 `code` 的标记效果的整型标签。
--- C++: `scriptlib::duel_set_flag_effect_label`
---@param player Player 玩家编号。
---@param code integer 标记代码。
---@param label integer 新的标签值。
---@return boolean|nil updated
function Duel.SetFlagEffectLabel(player, code, label) end

--- 多值返回玩家身上所有匹配 `code` 的标记效果的首个标签；无匹配返回 `nil`。
--- C++: `scriptlib::duel_get_flag_effect_label`
---@param player Player 玩家编号。
---@param code integer 标记代码。
---@return integer ... 标签值列表；无匹配返回 `nil`。
function Duel.GetFlagEffectLabel(player, code) end

--- [协程挂起] 破坏单卡或集合，可选指定破坏后的去向与原因玩家，返回实际破坏张数。
--- C++: `scriptlib::duel_destroy`
---@param targets Card|Group 要破坏的单卡或集合。
---@param reason Reason 破坏原因（如 `REASON_EFFECT`）。
---@param dest? Location 破坏后移动到的区域（默认 `LOCATION_GRAVE`）。
---@param reasonPlayer? Player 原因玩家（默认当前 `reason_player`）。
---@return integer count 实际破坏张数。
function Duel.Destroy(targets, reason, dest, reasonPlayer) end

--- [协程挂起] 以指定表示形式除外单卡或集合，返回实际除外张数。
--- C++: `scriptlib::duel_remove`
---@param targets Card|Group 要除外的单卡或集合。
---@param position Position 除外表示形式（`POS_FACEUP` 或 `POS_FACEDOWN`）。
---@param reason Reason 除外原因。
---@param reasonPlayer? Player 原因玩家（默认当前 `reason_player`）。
---@return integer count 实际除外张数。
function Duel.Remove(targets, position, reason, reasonPlayer) end

--- [协程挂起] 将单卡或集合表侧送去墓地，返回实际送墓张数。
--- C++: `scriptlib::duel_sendto_grave`
---@param targets Card|Group 要送墓的单卡或集合。
---@param reason Reason 送墓原因。
---@param reasonPlayer? Player 原因玩家（默认当前 `reason_player`）。
---@return integer count 实际送墓张数。
function Duel.SendtoGrave(targets, reason, reasonPlayer) end

--- [协程挂起] 将单卡或集合加入手牌；`player` 为 `nil` 时各自回到持有者手牌。
--- C++: `scriptlib::duel_sendto_hand`
---@param targets Card|Group 目标单卡或集合。
---@param player Player|nil 目标玩家（必填；传 `nil` 则各自回持有者手牌）。
---@param reason Reason 移动原因。
---@param reasonPlayer? Player 原因玩家（默认当前 `reason_player`）。
---@return integer count 实际加入手牌张数。
function Duel.SendtoHand(targets, player, reason, reasonPlayer) end

--- [协程挂起] 将单卡或集合送回卡组（`sequence`: `SEQ_DECKTOP`/`SEQ_DECKBOTTOM`/`SEQ_DECKSHUFFLE`）。
--- C++: `scriptlib::duel_sendto_deck`
---@param targets Card|Group 目标单卡或集合。
---@param player Player|nil 目标玩家（必填；传 `nil` 则各自回持有者卡组）。
---@param sequence integer 放回位置（`SEQ_DECKTOP=0`、`SEQ_DECKBOTTOM=1`、`SEQ_DECKSHUFFLE=2`）。
---@param reason Reason 移动原因。
---@param reasonPlayer? Player 原因玩家（默认当前 `reason_player`）。
---@param sendActivating? boolean 是否允许送回正在发动中的魔陷（默认 `false`）。
---@return integer count 实际回到卡组张数。
function Duel.SendtoDeck(targets, player, sequence, reason, reasonPlayer, sendActivating) end

--- [协程挂起] 将灵摆怪兽等单卡或集合表侧加入额外卡组（`player` 为 `nil` 时各自回持有者额外卡组）。
--- C++: `scriptlib::duel_sendto_extra`
---@param targets Card|Group 目标单卡或集合。
---@param player Player|nil 目标玩家（必填；传 `nil` 各自回持有者额外）。
---@param reason Reason 移动原因。
---@return integer count 实际加入额外卡组张数。
function Duel.SendtoExtraP(targets, player, reason) end

--- 取得上一次移动/破坏/抽卡等操作实际影响到的卡片集合副本。
--- C++: `scriptlib::duel_get_operated_group`
---@return Group group
function Duel.GetOperatedGroup() end

--- [协程挂起] 让玩家通常召唤怪兽 `card`（伤害步骤直接忽略；连锁中会暂存至连锁后处理）。
--- C++: `scriptlib::duel_summon`
---@param player Player 执行通常召唤的玩家。
---@param card Card 要通常召唤的怪兽。
---@param ignoreCount boolean 是否忽略通常召唤次数限制。
---@param effect Effect|nil 赋予额外通常召唤的效果（必填，无则传 `nil`）。
---@param minTribute? integer 最少解放数（默认 `0`）。
---@param zone? Zone 允许召唤到的怪兽区位掩码（默认 `0x1f`）。
---@return nil
function Duel.Summon(player, card, ignoreCount, effect, minTribute, zone) end

--- [协程挂起] 让玩家按规则手续特殊召唤 `card`（伤害步骤忽略）。
--- C++: `scriptlib::duel_special_summon_rule`
---@param player Player 执行特殊召唤的玩家。
---@param card Card 要特殊召唤的怪兽。
---@param summonType? SummonType 召唤类型限制（默认 `0`）。
---@return nil
function Duel.SpecialSummonRule(player, card, summonType) end

--- [协程挂起] 让玩家同调召唤 `card`，可限定必选调整、候选素材组及素材数范围。
--- C++: `scriptlib::duel_synchro_summon`
---@param player Player 执行召唤的玩家。
---@param card Card 目标同调怪兽。
---@param tuner Card|nil 必选调整怪兽（必填，不限定则传 `nil`）。
---@param materials? Group|nil 候选素材集合（默认 `nil`）。
---@param minc? integer 最少素材数（默认 `0`）。
---@param maxc? integer 最多素材数（默认 `0`）。
---@return nil
function Duel.SynchroSummon(player, card, tuner, materials, minc, maxc) end

--- [协程挂起] 让玩家超量召唤 `card`，可限定候选素材组及素材数范围。
--- C++: `scriptlib::duel_xyz_summon`
---@param player Player 执行召唤的玩家。
---@param card Card 目标超量怪兽。
---@param materials Group|nil 候选素材集合（必填，不限定则传 `nil`）。
---@param minc? integer 最少素材数（默认 `0`）。
---@param maxc? integer 最多素材数（默认 `0`）。
---@return nil
function Duel.XyzSummon(player, card, materials, minc, maxc) end

--- [协程挂起] 让玩家连接召唤 `card`，可限定候选素材组、必选卡及素材数范围。
--- C++: `scriptlib::duel_link_summon`
---@param player Player 执行召唤的玩家。
---@param card Card 目标连接怪兽。
---@param materials Group|nil 候选素材集合（必填，不限定则传 `nil`）。
---@param mustCard? Card|nil 必选素材卡（默认 `nil`）。
---@param minc? integer 最少素材数（默认 `0`）。
---@param maxc? integer 最多素材数（默认 `0`）。
---@return nil
function Duel.LinkSummon(player, card, materials, mustCard, minc, maxc) end

--- [协程挂起] 让玩家通常盖放怪兽 `card`（参数与 `Duel.Summon` 相同，伤害步骤忽略）。
--- C++: `scriptlib::duel_setm`
---@param player Player 执行通常盖放的玩家。
---@param card Card 要盖放的怪兽。
---@param ignoreCount boolean 是否忽略通常召唤次数限制。
---@param effect Effect|nil 赋予额外通常盖放的效果（必填，无则传 `nil`）。
---@param minTribute? integer 最少解放数（默认 `0`）。
---@param zone? Zone 允许放置的怪兽区位掩码（默认 `0x1f`）。
---@return nil
function Duel.MSet(player, card, ignoreCount, effect, minTribute, zone) end

--- [协程挂起] 将魔陷卡或集合盖放到 `toPlayer` 场上，返回实际成功盖放张数。
--- C++: `scriptlib::duel_sets`
---@param player Player 执行盖放的玩家。
---@param targets Card|Group 要盖放的单卡或集合。
---@param toPlayer? Player 盖放到哪方场上（默认 `player`）。
---@param confirm? boolean 是否向对手展示确认（默认 `true`）。
---@return integer count 实际盖放张数。
function Duel.SSet(player, targets, toPlayer, confirm) end

--- 为玩家创建一个指定卡号的衍生物对象（`player` 非 0/1 时返回 `false`）。
--- C++: `scriptlib::duel_create_token`
---@param player Player 衍生物持有者（0 或 1）。
---@param code CardCode 衍生物卡号。
---@return Card|boolean token 成功返回 `Card`，玩家非法返回 `false`。
function Duel.CreateToken(player, code) end

--- [协程挂起] 一步特殊召唤单卡或集合到 `targetPlayer` 场上，返回实际成功特召张数。
--- C++: `scriptlib::duel_special_summon`
---@param targets Card|Group 要特殊召唤的单卡或集合。
---@param summonType SummonType 召唤类型（如 `0`、`SUMMON_TYPE_FUSION`）。
---@param sumPlayer Player 执行特殊召唤的玩家。
---@param targetPlayer Player 特殊召唤到哪方场上。
---@param nocheck boolean 是否无视召唤条件。
---@param nolimit boolean 是否无视苏生限制。
---@param position Position 特殊召唤表示形式。
---@param zone? Zone 允许召唤到的格子位掩码（默认 `0xff`）。
---@return integer count 实际特殊召唤张数。
function Duel.SpecialSummon(targets, summonType, sumPlayer, targetPlayer, nocheck, nolimit, position, zone) end

--- [协程挂起] 分步特殊召唤单张怪兽（须配对调用 `Duel.SpecialSummonComplete()` 完成结算），返回是否成功。
--- C++: `scriptlib::duel_special_summon_step`
---@param card Card 要特殊召唤的单张怪兽。
---@param summonType SummonType 召唤类型。
---@param sumPlayer Player 执行特殊召唤的玩家。
---@param targetPlayer Player 特殊召唤到哪方场上。
---@param nocheck boolean 是否无视召唤条件。
---@param nolimit boolean 是否无视苏生限制。
---@param position Position 表示形式。
---@param zone? Zone 允许召唤到的格子位掩码（默认 `0xff`）。
---@return boolean success
function Duel.SpecialSummonStep(card, summonType, sumPlayer, targetPlayer, nocheck, nolimit, position, zone) end

--- [协程挂起] 统一结算所有通过 `Duel.SpecialSummonStep` 暂存的特殊召唤，返回实际特召张数。
--- C++: `scriptlib::duel_special_summon_complete`
---@return integer count
function Duel.SpecialSummonComplete() end

--- 检查玩家能否放置指示物（1 参查全局放置权限；4 参查能否给指定卡放 `count` 个 `counterType`）。
--- C++: `scriptlib::duel_is_can_add_counter`
---@param player Player 玩家编号（0 或 1）。
---@param counterType? CounterType 可选：指示物类型编码。
---@param count? integer 可选：放置数量。
---@param card? Card 可选：目标卡片。
---@return boolean canAdd
---@overload fun(player: Player): boolean
---@overload fun(player: Player, counterType: CounterType, count: integer, card: Card): boolean
function Duel.IsCanAddCounter(player, counterType, count, card) end

--- [协程挂起] 从玩家视角己方 `s` / 对方 `o` 区域的卡片上取除指定种类和数量的指示物。
--- C++: `scriptlib::duel_remove_counter`
---@param player Player 执行取除的玩家（0 或 1）。
---@param selfLocation Location 己方区域掩码。
---@param opponentLocation Location 对方区域掩码。
---@param counterType CounterType 指示物类型编码。
---@param count integer 取除数量。
---@param reason Reason 原因位掩码。
---@return boolean|nil removed `player` 非 0/1 无返回值。
function Duel.RemoveCounter(player, selfLocation, opponentLocation, counterType, count, reason) end

--- 检查玩家能否从己方 `s` / 对方 `o` 区域取除指定种类与数量的指示物。
--- C++: `scriptlib::duel_is_can_remove_counter`
---@param player Player 玩家编号（0 或 1）。
---@param selfLocation Location 己方区域掩码。
---@param opponentLocation Location 对方区域掩码。
---@param counterType CounterType 指示物类型编码。
---@param count integer 取除数量。
---@param reason Reason 原因位掩码。
---@return boolean|nil canRemove
function Duel.IsCanRemoveCounter(player, selfLocation, opponentLocation, counterType, count, reason) end

--- 统计玩家视角己方 `s` / 对方 `o` 区域上指定类型指示物的总数。
--- C++: `scriptlib::duel_get_counter`
---@param player Player 玩家编号（0 或 1）。
---@param selfLocation Location 己方区域掩码。
---@param opponentLocation Location 对方区域掩码。
---@param counterType CounterType 指示物类型编码。
---@return integer|nil count
function Duel.GetCounter(player, selfLocation, opponentLocation, counterType) end

--- [协程挂起] 改变单卡或集合的表示形式；可按原表示（表攻/里攻/表守/里守）分别指定目标表示。
--- C++: `scriptlib::duel_change_form`
---@param targets Card|Group 目标单卡或集合。
---@param au Position 原表侧攻击表示的目标表示（同时作为 `ad`/`du`/`dd` 省略时的默认值）。
---@param ad? Position 原里侧攻击表示的目标表示（默认同 `au`）。
---@param du? Position 原表侧守备表示的目标表示（默认同 `au`）。
---@param dd? Position 原里侧守备表示的目标表示（默认同 `au`）。
---@param noFlip? boolean 为 `true` 时不触发反转效果（默认 `false`）。
---@return integer count 实际改变表示形式的张数。
function Duel.ChangePosition(targets, au, ad, du, dd, noFlip) end

--- [协程挂起] 解放单卡或集合，返回实际解放张数。
--- C++: `scriptlib::duel_release`
---@param targets Card|Group 目标单卡或集合。
---@param reason Reason 解放原因。
---@param reasonPlayer? Player 原因玩家（默认当前 `reason_player`）。
---@return integer count 实际解放张数。
function Duel.Release(targets, reason, reasonPlayer) end

--- [协程挂起] 将卡片直接移动到目标玩家场上指定区域（`LOCATION_FZONE`/`LOCATION_PZONE` 自动映射）。
--- C++: `scriptlib::duel_move_to_field`
---@param card Card 要放置的卡片。
---@param movePlayer Player 执行放置的玩家。
---@param targetPlayer Player 放置到哪方场上（0 或 1）。
---@param destination Location 目标区域（如 `LOCATION_MZONE`、`LOCATION_SZONE`、`LOCATION_FZONE`、`LOCATION_PZONE`）。
---@param position Position 放置后的表示形式。
---@param enable boolean 是否启用场上效果。
---@param zone? Zone 允许放置的格子掩码（默认 `0xff`）。
---@return boolean|nil moved
function Duel.MoveToField(card, movePlayer, targetPlayer, destination, position, enable, zone) end

--- [协程挂起] 将因 `REASON_TEMPORARY` 暂时离场的卡片返回原控制者原区域。
--- C++: `scriptlib::duel_return_to_field`
---@param card Card 带有暂时离场标记的卡片。
---@param position? Position 返回时的表示形式（默认 `card:GetPreviousPosition()`）。
---@param zone? Zone 允许返回的格子掩码（默认 `0xff`）。
---@return boolean|nil returned 无暂时离场标记时无返回值。
function Duel.ReturnToField(card, position, zone) end

--- 将卡片移动到当前所在区域的新序号 `sequence` 并触发 `EVENT_MOVE`。
--- C++: `scriptlib::duel_move_sequence`
---@param card Card 目标卡片。
---@param sequence integer 目标序号。
---@return nil
function Duel.MoveSequence(card, sequence) end

--- 交换同一控制者怪兽区中两张怪兽的位置并触发 `EVENT_MOVE`。
--- C++: `scriptlib::duel_swap_sequence`
---@param card1 Card 第一张怪兽。
---@param card2 Card 第二张怪兽。
---@return nil
function Duel.SwapSequence(card1, card2) end

--- 将效果 `effect` 压入引擎的立即发动处理队列（不挂起）。
--- C++: `scriptlib::duel_activate_effect`
---@param effect Effect 要发动的效果。
---@return nil
function Duel.Activate(effect) end

--- 为当前连锁项注册连锁限制回调 `f(e, rp, tp) -> boolean`。
--- C++: `scriptlib::duel_set_chain_limit`
---@param limitFunc ChainLimitFunction 连锁限制函数。
---@return nil
function Duel.SetChainLimit(limitFunc) end

--- 为当前连锁串后续全部连锁窗口注册临时连锁限制回调。
--- C++: `scriptlib::duel_set_chain_limit_p`
---@param limitFunc ChainLimitFunction 连锁限制函数。
---@return nil
function Duel.SetChainLimitTillChainEnd(limitFunc) end

--- 取得作用于指定玩家的首个 `EFFECT_CHAIN_MATERIAL` 效果。
--- C++: `scriptlib::duel_get_chain_material`
---@param player Player 玩家编号。
---@return Effect|nil effect
function Duel.GetChainMaterial(player) end

--- [协程挂起] 双方确认指定玩家主卡组顶最多 `count` 张卡。
--- C++: `scriptlib::duel_confirm_decktop`
---@param player Player 玩家编号。
---@param count integer 确认张数。
---@return nil
function Duel.ConfirmDecktop(player, count) end

--- [协程挂起] 确认指定玩家额外卡组里侧顶（跳过表侧灵摆）最多 `count` 张卡。
--- C++: `scriptlib::duel_confirm_extratop`
---@param player Player 玩家编号。
---@param count integer 确认张数。
---@return nil
function Duel.ConfirmExtratop(player, count) end

--- [协程挂起] 向指定玩家展示单卡或集合（空集合直接跳过）。
--- C++: `scriptlib::duel_confirm_cards`
---@param player Player 观看展示的玩家编号。
---@param targets Card|Group 要展示的单卡或集合。
---@param skipPanel? boolean 为 `true` 时不弹出客户端确认面板（默认 `false`）。
---@return nil
function Duel.ConfirmCards(player, targets, skipPanel) end

--- [协程挂起] 让 `sortPlayer` 排列 `targetPlayer` 主卡组顶 `count` 张卡的顺序。
--- C++: `scriptlib::duel_sort_decktop`
---@param sortPlayer Player 执行排序的玩家。
---@param targetPlayer Player 卡组所属玩家。
---@param count integer 排序张数。
---@return nil
function Duel.SortDecktop(sortPlayer, targetPlayer, count) end

--- 检查当前是否正在处理事件 `event`；`getInfo=true` 且命中时返回 7 个值 `(true, eg, ep, ev, re, r, rp)`。
--- C++: `scriptlib::duel_check_event`
---@param event EffectCode 事件代码（`EVENT_*`）。
---@param getInfo? boolean 是否在命中时一并返回事件 6 元组（默认 `false`）。
---@return boolean matched 是否命中事件。
---@return Group|nil eg 事件卡组（仅 `getInfo=true` 且命中时）。
---@return Player? ep 事件玩家。
---@return integer? ev 事件值。
---@return Effect|nil re 触发效果。
---@return Reason? r 触发原因。
---@return Player? rp 触发玩家。
---@overload fun(event: EffectCode): boolean
---@overload fun(event: EffectCode, getInfo: true): boolean, Group|nil, Player?, integer?, Effect|nil, Reason?, Player?
function Duel.CheckEvent(event, getInfo) end

--- [协程挂起] 手动触发一个全局事件并立即推进瞬时事件处理。
--- C++: `scriptlib::duel_raise_event`
---@param eg Card|Group 事件相关卡片或集合。
---@param code EffectCode 事件代码（`EVENT_*`）。
---@param re Effect|nil 触发原因效果。
---@param r Reason 触发原因位掩码。
---@param rp Player 触发原因玩家。
---@param ep Player 事件对象玩家。
---@param ev integer 事件数值。
---@return nil
function Duel.RaiseEvent(eg, code, re, r, rp, ep, ev) end

--- [协程挂起] 对单张卡片触发单体事件并立即推进瞬时事件处理。
--- C++: `scriptlib::duel_raise_single_event`
---@param card Card 目标卡片。
---@param code EffectCode 单体事件代码（`EVENT_*`）。
---@param re Effect|nil 触发原因效果。
---@param r Reason 触发原因位掩码。
---@param rp Player 触发原因玩家。
---@param ep Player 事件对象玩家。
---@param ev integer 事件数值。
---@return nil
function Duel.RaiseSingleEvent(card, code, re, r, rp, ep, ev) end

--- 检查当前任意玩家的提示时点是否命中 `timing` 位掩码。
--- C++: `scriptlib::duel_check_timing`
---@param timing Timing 时点位掩码（`TIMING_*`）。
---@return boolean matched
function Duel.CheckTiming(timing) end

--- 判定场上/墓地是否存在表侧的指定卡号场地/环境（无表侧场地魔法时同步检查 `EFFECT_CHANGE_ENVIRONMENT`）。
--- C++: `scriptlib::duel_is_environment`
---@param code CardCode 环境卡号（如海 `22702055`）。
---@param player? Player 检查哪方（默认 `PLAYER_ALL` 双方）。
---@param location? Location 检查区域（默认 `LOCATION_ONFIELD`）。
---@return boolean|nil active
function Duel.IsEnvironment(code, player, location) end

--- 立即判定玩家胜出（`player` 可为 `0`、`1` 或 `PLAYER_NONE=2` 平局）。
--- C++: `scriptlib::duel_win`
---@param player Player 胜出玩家（0、1 或 2 平局）。
---@param reason integer 特殊胜利代码（`WIN_REASON_*`）。
---@return nil
function Duel.Win(player, reason) end

--- [协程挂起] 让玩家抽 `count` 张卡，返回实际抽卡张数。
--- C++: `scriptlib::duel_draw`
---@param player Player 玩家编号（0 或 1）。
---@param count integer 抽卡张数。
---@param reason Reason 抽卡原因。
---@return integer|nil drawn
function Duel.Draw(player, count, reason) end

--- [协程挂起] 给予玩家效果伤害，返回实际造成的伤害值；`isStep=true` 时暂存等待 `Duel.RDComplete()` 统一结算。
--- C++: `scriptlib::duel_damage`
---@param player Player 受伤玩家（0 或 1）。
---@param amount integer 伤害值（负值归 0）。
---@param reason Reason 伤害原因。
---@param isStep? boolean 是否分步结算（默认 `false`）。
---@return integer|nil damage
function Duel.Damage(player, amount, reason, isStep) end

--- [协程挂起] 回复玩家 LP，返回实际回复值；`isStep=true` 时暂存等待 `Duel.RDComplete()` 统一结算。
--- C++: `scriptlib::duel_recover`
---@param player Player 回复玩家（0 或 1）。
---@param amount integer 回复量（负值归 0）。
---@param reason Reason 回复原因。
---@param isStep? boolean 是否分步结算（默认 `false`）。
---@return integer|nil recovered
function Duel.Recover(player, amount, reason, isStep) end

--- [协程挂起] 统一结算所有通过 `isStep=true` 暂存的效果伤害与 LP 回复。
--- C++: `scriptlib::duel_rd_complete`
---@return nil
function Duel.RDComplete() end

--- [协程挂起] 将 `equipCard` 装备给 `target`（`step=true` 时需配对调用 `Duel.EquipComplete()`）。
--- C++: `scriptlib::duel_equip`
---@param player Player 执行装备的玩家（0 或 1）。
---@param equipCard Card 装备卡。
---@param target Card 被装备怪兽。
---@param faceup? boolean 是否表侧表示装备（默认 `true`）。
---@param step? boolean 是否分步装备（默认 `false`）。
---@return boolean|nil equipped
function Duel.Equip(player, equipCard, target, faceup, step) end

--- [协程挂起] 启用所有分步装备卡的效果并触发 `EVENT_EQUIP`。
--- C++: `scriptlib::duel_equip_complete`
---@return nil
function Duel.EquipComplete() end

--- [协程挂起] 将单卡或集合的控制权转移给 `player`，返回实际转移张数。
--- C++: `scriptlib::duel_get_control`
---@param targets Card|Group 要转移控制权的单卡或集合。
---@param player Player 接收控制权的玩家（0 或 1）。
---@param resetPhase? Phase 归还控制权的阶段（默认 `0` 永久）。
---@param resetCount? integer 经过多少次该阶段后归还（默认 `0`）。
---@param zone? Zone 允许放置的怪兽区位掩码（默认 `0xff`）。
---@return integer|nil moved
function Duel.GetControl(targets, player, resetPhase, resetCount, zone) end

--- [协程挂起] 交换两张卡或两个等量卡片集合的控制权（参数 1 与参数 2 必须同为 `Card` 或同为 `Group`）。
--- C++: `scriptlib::duel_swap_control`
---@param targets1 Card|Group 第一方卡片或集合。
---@param targets2 Card|Group 第二方卡片或集合（须与 `targets1` 同类型）。
---@param resetPhase? Phase 归还阶段（默认 `0` 永久）。
---@param resetCount? integer 归还回合计数（默认 `0`）。
---@return boolean swapped
---@overload fun(card1: Card, card2: Card, resetPhase?: Phase, resetCount?: integer): boolean
---@overload fun(group1: Group, group2: Group, resetPhase?: Phase, resetCount?: integer): boolean
function Duel.SwapControl(targets1, targets2, resetPhase, resetCount) end

--- 检查玩家能否支付指定数值的 LP 代价（`cost <= 0` 返回 `false`）。
--- C++: `scriptlib::duel_check_lp_cost`
---@param player Player 玩家编号（0 或 1）。
---@param cost integer 需支付的 LP 数量。
---@param mustPay? boolean 是否为强制支付（不计入免支付替代效果，默认 `false`）。
---@return boolean|nil payable
function Duel.CheckLPCost(player, cost, mustPay) end

--- [协程挂起] 让玩家支付指定数值的 LP 代价。
--- C++: `scriptlib::duel_pay_lp_cost`
---@param player Player 玩家编号（0 或 1）。
---@param cost integer 支付的 LP 数量。
---@param mustPay? boolean 是否为强制支付（默认 `false`）。
---@return nil
function Duel.PayLPCost(player, cost, mustPay) end

--- [协程挂起] 将玩家主卡组顶 `count` 张卡送去墓地，返回实际送墓张数。
--- C++: `scriptlib::duel_discard_deck`
---@param player Player 玩家编号。
---@param count integer 张数。
---@param reason Reason 原因。
---@return integer count
function Duel.DiscardDeck(player, count, reason) end

--- [协程挂起] 按过滤条件让玩家从手牌选择 `min..max` 张卡丢弃（送墓），返回实际丢弃张数。
--- C++: `scriptlib::duel_discard_hand`
---@param player Player 玩家编号（0 或 1）。
---@param filter FilterFunction|nil 手牌过滤函数（`nil` 表示任意手牌）。
---@param min integer 最少丢弃张数。
---@param max integer 最多丢弃张数。
---@param reason Reason 丢弃原因（通常含 `REASON_DISCARD`）。
---@param excluded? Card|Group|nil 排除的单卡或集合（默认 `nil`）。
---@param ... any 透传给 `filter` 的附加参数。
---@return integer|nil discarded
function Duel.DiscardHand(player, filter, min, max, reason, excluded, ...) end

--- 开启或关闭引擎的自动卡组洗切检查。
--- C++: `scriptlib::duel_disable_shuffle_check`
---@param disable? boolean 是否禁用自动洗切检查（默认 `true`）。
---@return nil
function Duel.DisableShuffleCheck(disable) end

--- 开启或关闭引擎的自毁状态检查。
--- C++: `scriptlib::duel_disable_self_destroy_check`
---@param disable? boolean 是否禁用自毁检查（默认 `true`）。
---@return nil
function Duel.DisableSelfDestroyCheck(disable) end

--- 开启或关闭从卡组选卡时是否向玩家公开卡组序号。
--- C++: `scriptlib::duel_reveal_select_deck_sequence`
---@param reveal? boolean 是否公开序号（默认 `true`）。
---@return nil
function Duel.RevealSelectDeckSequence(reveal) end

--- 洗切指定玩家的主卡组。
--- C++: `scriptlib::duel_shuffle_deck`
---@param player Player 玩家编号。
---@return nil
function Duel.ShuffleDeck(player) end

--- 洗切指定玩家的额外卡组。
--- C++: `scriptlib::duel_shuffle_extra`
---@param player Player 玩家编号。
---@return nil
function Duel.ShuffleExtra(player) end

--- 洗切指定玩家的手牌。
--- C++: `scriptlib::duel_shuffle_hand`
---@param player Player 玩家编号。
---@return nil
function Duel.ShuffleHand(player) end

--- 将同控制者同区域（主怪兽区/主魔陷区 0~4）的多张里侧卡随机洗牌换位并触发 `EVENT_MOVE`。
--- C++: `scriptlib::duel_shuffle_setcard`
---@param group Group 同区域里侧卡片集合。
---@return nil
function Duel.ShuffleSetCard(group) end

--- 将当前战斗的攻击怪兽替换为 `attacker`。
--- C++: `scriptlib::duel_change_attacker`
---@param attacker Card 新的攻击怪兽。
---@param ignoreCount? boolean 是否忽略攻击次数记录（默认 `false`）。
---@return nil
function Duel.ChangeAttacker(attacker, ignoreCount) end

--- 更改当前战斗的攻击对象（传 `nil` 改为直接攻击），返回是否成功。
--- C++: `scriptlib::duel_change_attack_target`
---@param target Card|nil 新攻击对象怪兽，传 `nil` 为直接攻击。
---@return boolean changed
function Duel.ChangeAttackTarget(target) end

--- [协程挂起] 强制进入伤害步骤进行一次战斗伤害计算（`target=nil` 表示直接攻击）。
--- C++: `scriptlib::duel_calculate_damage`
---@param attacker Card 攻击怪兽。
---@param target Card|nil 被攻击怪兽（`nil` 为直接攻击）。
---@param newAttack? boolean 是否视为新攻击（默认 `false`）。
---@return nil
function Duel.CalculateDamage(attacker, target, newAttack) end

--- 取得指定玩家本次战斗将受到的战斗伤害值。
--- C++: `scriptlib::duel_get_battle_damage`
---@param player Player 玩家编号（0 或 1）。
---@return integer|nil damage
function Duel.GetBattleDamage(player) end

--- 修改指定玩家本次战斗伤害值（`check=true` 且原伤害为 `0` 时不修改）。
--- C++: `scriptlib::duel_change_battle_damage`
---@param player Player 玩家编号。
---@param damage integer 新战斗伤害值。
---@param check? boolean 原伤害为 `0` 时是否跳过修改（默认 `true`）。
---@return nil
function Duel.ChangeBattleDamage(player, damage, check) end

--- 替换指定连锁项的目标卡片集合。
--- C++: `scriptlib::duel_change_target`
---@param chainIndex integer 连锁序号（1-based）。
---@param targets Group 新的目标集合。
---@return nil
function Duel.ChangeTargetCard(chainIndex, targets) end

--- 替换指定连锁项的目标玩家。
--- C++: `scriptlib::duel_change_target_player`
---@param chainIndex integer 连锁序号。
---@param player Player 新的目标玩家。
---@return nil
function Duel.ChangeTargetPlayer(chainIndex, player) end

--- 替换指定连锁项的目标参数值。
--- C++: `scriptlib::duel_change_target_param`
---@param chainIndex integer 连锁序号。
---@param param integer 新的目标参数。
---@return nil
function Duel.ChangeTargetParam(chainIndex, param) end

--- [协程挂起] 插入效果处理断点（区分“那之后”与同时处理）并触发 `EVENT_BREAK_EFFECT`。
--- C++: `scriptlib::duel_break_effect`
---@return nil
function Duel.BreakEffect() end

--- 替换指定连锁项的处理函数（`operation`）。
--- C++: `scriptlib::duel_change_effect`
---@param chainIndex integer 连锁序号。
---@param operation OperationFunction 新的效果处理函数。
---@return nil
function Duel.ChangeChainOperation(chainIndex, operation) end

--- 无效指定连锁项的效果发动，返回是否成功。
--- C++: `scriptlib::duel_negate_activate`
---@param chainIndex integer 连锁序号。
---@return boolean negated
function Duel.NegateActivation(chainIndex) end

--- 无效指定连锁项的效果处理，返回是否成功。
--- C++: `scriptlib::duel_negate_effect`
---@param chainIndex integer 连锁序号。
---@param forced? boolean 是否强制无效（默认 `false`）。
---@return boolean negated
function Duel.NegateEffect(chainIndex, forced) end

--- 给 `card` 附加 `EFFECT_DISABLE_CHAIN`，无效当前连锁串中仍与该卡关联的该卡效果。
--- C++: `scriptlib::duel_negate_related_chain`
---@param card Card 目标卡片。
---@param reset Reset 重置条件。
---@return nil
function Duel.NegateRelatedChain(card, reset) end

--- 无效当前正在进行的召唤/反转召唤/特殊召唤并触发对应的 `EVENT_*_NEGATED`。
--- C++: `scriptlib::duel_disable_summon`
---@param targets Card|Group 被无效召唤的单卡或集合。
---@return nil
function Duel.NegateSummon(targets) end

--- 增加当前 `reason_player` 的通常召唤次数（传入 `card` 且存在额外通召权时优先消耗额外通召权）。
--- C++: `scriptlib::duel_increase_summon_count`
---@param card? Card 可选：正在召唤的怪兽。
---@return nil
function Duel.IncreaseSummonedCount(card) end

--- 检查当前 `reason_player` 是否仍保有通常召唤次数（或对 `card` 的额外通召权）。
--- C++: `scriptlib::duel_check_summon_count`
---@param card? Card 可选：要召唤的怪兽。
---@return boolean available
function Duel.CheckSummonedCount(card) end

--- 查询玩家主怪兽区/魔陷区的可用空位数（可为负）与可用格子位掩码（双返回值）。
--- C++: `scriptlib::duel_get_location_count`
---@param player Player 场地所属玩家（0 或 1）。
---@param location Location `LOCATION_MZONE` 或 `LOCATION_SZONE`。
---@param uplayer? Player|nil 使用格子的玩家（默认当前 `reason_player`）。
---@param reason? integer 占用原因（默认 `LOCATION_REASON_TOFIELD`）。
---@param zone? Zone 限定的格子位掩码（默认 `0xff`）。
---@return integer|nil count 可用空位数（`player` 非 0/1 无返回值）。
---@return Zone|nil usableZone 可用格子位掩码。
function Duel.GetLocationCount(player, location, uplayer, reason, zone) end

--- 模拟 `targets` 离场后，查询玩家主怪兽区的可用空位数与可用格子位掩码（双返回值）。
--- C++: `scriptlib::duel_get_mzone_count`
---@param player Player 场地所属玩家（0 或 1）。
---@param targets? Card|Group|nil 模拟即将离场的卡片或集合（默认 `nil`）。
---@param uplayer? Player 使用格子的玩家（省略时默认 `reason_player`；注意 C++ 未判 `nil`，显式传 `nil` 会被转为 `0`！）。
---@param reason? integer 占用原因（默认 `LOCATION_REASON_TOFIELD`）。
---@param zone? Zone 限定的格子位掩码（默认 `0xff`）。
---@return integer|nil count 可用空位数。
---@return Zone|nil usableZone 可用格子位掩码。
function Duel.GetMZoneCount(player, targets, uplayer, reason, zone) end

--- 模拟 `targets` 离场后，查询玩家魔陷区的可用空位数与可用格子位掩码（双返回值）。
--- C++: `scriptlib::duel_get_szone_count`
---@param player Player 场地所属玩家（0 或 1）。
---@param targets? Card|Group|nil 模拟即将离场的卡片或集合（默认 `nil`）。
---@param uplayer? Player 使用格子的玩家（省略时默认 `reason_player`；注意显式传 `nil` 会被转为 `0`！）。
---@param reason? integer 占用原因（默认 `LOCATION_REASON_TOFIELD`）。
---@param zone? Zone 限定的格子位掩码（默认 `0xff`）。
---@return integer|nil count 可用空位数。
---@return Zone|nil usableZone 可用格子位掩码。
function Duel.GetSZoneCount(player, targets, uplayer, reason, zone) end

--- 模拟 `targets` 离场后，计算从额外卡组特招 `sc` 到 `player` 场上的可用空位数与可用格子位掩码（双返回值）。
--- C++: `scriptlib::duel_get_location_count_fromex`
---@param player Player 场地所属玩家（0 或 1）。
---@param uplayer? Player|nil 执行特召的玩家（默认当前 `reason_player`，支持传 `nil`）。
---@param targets? Card|Group|nil 模拟即将离场的素材卡或集合（默认 `nil`）。
---@param sc? Card|CardType|nil 要从额外特招的具体怪兽 `Card` 或种类掩码（如 `TYPE_LINK`/`TYPE_PENDULUM`）。
---@param zone? Zone 限定的格子位掩码（默认 `0xff`）。
---@return integer|nil count 可用空位数。
---@return Zone|nil usableZone 可用格子位掩码。
function Duel.GetLocationCountFromEx(player, uplayer, targets, sc, zone) end

--- 取得玩家主怪兽区与额外怪兽区合计可用的总格数。
--- C++: `scriptlib::duel_get_usable_mzone_count`
---@param player Player 场地所属玩家。
---@param uplayer? Player 使用者玩家（默认 `reason_player`）。
---@return integer|nil count
function Duel.GetUsableMZoneCount(player, uplayer) end

--- 取得指定玩家视角己方 `s` / 对方 `o` 区域中处于连接状态的全部卡片集合。
--- C++: `scriptlib::duel_get_linked_group`
---@param player Player 视角玩家。
---@param selfLocation Location 己方区域。
---@param opponentLocation Location 对方区域。
---@return Group|nil group
function Duel.GetLinkedGroup(player, selfLocation, opponentLocation) end

--- 取得指定玩家视角己方 `s` / 对方 `o` 区域中处于连接状态的卡片数量。
--- C++: `scriptlib::duel_get_linked_group_count`
---@param player Player 视角玩家。
---@param selfLocation Location 己方区域。
---@param opponentLocation Location 对方区域。
---@return integer|nil count
function Duel.GetLinkedGroupCount(player, selfLocation, opponentLocation) end

--- 取得指定玩家视角的全局连接端格子位掩码。
--- C++: `scriptlib::duel_get_linked_zone`
---@param player Player 视角玩家。
---@return Zone|nil zone
function Duel.GetLinkedZone(player) end

--- 取得指定玩家、区域与序号位置的单张卡片（无卡或正在召唤中返回 `nil`）。
--- C++: `scriptlib::duel_get_field_card`
---@param player Player 玩家编号。
---@param location Location 区域（如 `LOCATION_MZONE`、`LOCATION_SZONE`、`LOCATION_PZONE`）。
---@param sequence integer 格子序号。
---@return Card|nil card
function Duel.GetFieldCard(player, location, sequence) end

--- 检查指定玩家的指定区域格子是否空闲且可用。
--- C++: `scriptlib::duel_check_location`
---@param player Player 玩家编号。
---@param location Location 区域。
---@param sequence integer 格子序号。
---@return boolean|nil usable
function Duel.CheckLocation(player, location, sequence) end

--- 取得当前连锁堆叠长度（`0` 表示不在连锁中）。
--- C++: `scriptlib::duel_get_current_chain`
---@return integer chainCount
function Duel.GetCurrentChain() end

--- 取得当前已完成取对象的就绪连锁数量。
--- C++: `scriptlib::duel_get_ready_chain`
---@return integer readyChainCount
function Duel.GetReadyChain() end

--- 按 1..N 个 `CHAININFO_*` 标志查询连锁 `chainIndex`（`0` 表示当前正在处理的连锁），按序多值返回对应结果。
--- C++: `scriptlib::duel_get_chain_info`
---@param chainIndex integer 连锁序号（`0` 为当前处理连锁，正数为 1-based 序号）。
---@param ... ChainInfo 要查询的连锁信息标志（`CHAININFO_*`）。
---@return any ... 与输入标志一一对应的返回值（`integer`/`Effect`/`Group`/`nil`）。
function Duel.GetChainInfo(chainIndex, ...) end

--- 返回指定连锁项发动时捕获的 6 元组事件信息（连锁不存在时无返回值）。
--- C++: `scriptlib::duel_get_chain_event`
---@param chainIndex integer 连锁序号。
---@return Group|nil eg 事件卡组。
---@return Player? ep 事件玩家。
---@return integer? ev 事件数值。
---@return Effect|nil re 触发效果。
---@return Reason? r 触发原因。
---@return Player? rp 触发玩家。
function Duel.GetChainEvent(chainIndex) end

--- 将当前处理连锁的全部对象卡片依次作为多返回值返回（无对象时返回 `nil`）。
--- C++: `scriptlib::duel_get_first_target`
---@return Card ... 当前连锁的所有对象卡片。
function Duel.GetFirstTarget() end

--- 取得当前处理连锁中仍然保持关联的对象卡片集合。
--- C++: `scriptlib::duel_get_targets_relate_to_chain`
---@return Group targets
function Duel.GetTargetsRelateToChain() end

--- 判定当前阶段是否严格等于 `phase`。
--- C++: `scriptlib::duel_is_phase`
---@param phase Phase 阶段常量（`PHASE_*`）。
---@return boolean matched
function Duel.IsPhase(phase) end

--- 判定当前是否处于主要阶段 1 或主要阶段 2。
--- C++: `scriptlib::duel_is_main_phase`
---@return boolean isMainPhase
function Duel.IsMainPhase() end

--- 判定当前是否处于战斗阶段（`PHASE_BATTLE_START`～`PHASE_BATTLE`）。
--- C++: `scriptlib::duel_is_battle_phase`
---@return boolean isBattlePhase
function Duel.IsBattlePhase() end

--- 取得当前阶段常量（`PHASE_*`）。
--- C++: `scriptlib::duel_get_current_phase`
---@return Phase phase
function Duel.GetCurrentPhase() end

--- 跳过指定玩家的某个阶段（DP/SP/M1/BP/M2/EP）。
--- C++: `scriptlib::duel_skip_phase`
---@param player Player 目标玩家。
---@param phase Phase 要跳过的阶段。
---@param reset Reset 重置条件。
---@param resetCount integer 重置计数（`<= 0` 自动修正为 `1`）。
---@param value? integer 附加给跳过效果的整型 value（默认 `0`）。
---@return nil
function Duel.SkipPhase(player, phase, reset, resetCount, value) end

--- 判定本次战斗是否已完成伤害计算。
--- C++: `scriptlib::duel_is_damage_calculated`
---@return boolean calculated
function Duel.IsDamageCalculated() end

--- 取得当前战斗的攻击怪兽。
--- C++: `scriptlib::duel_get_attacker`
---@return Card|nil attacker
function Duel.GetAttacker() end

--- 取得当前战斗的被攻击怪兽（直接攻击时返回 `nil`）。
--- C++: `scriptlib::duel_get_attack_target`
---@return Card|nil target
function Duel.GetAttackTarget() end

--- 按 `(己方战斗怪兽, 对方战斗怪兽)` 双值返回当前参与战斗的双方怪兽。
--- C++: `scriptlib::duel_get_battle_monster`
---@param player Player 基准视角玩家（强校验整数）。
---@return Card|nil selfMonster
---@return Card|nil opponentMonster
function Duel.GetBattleMonster(player) end

--- [协程挂起] 无效当前攻击，返回是否成功。
--- C++: `scriptlib::duel_disable_attack`
---@return boolean negated
function Duel.NegateAttack() end

--- 赋予当前攻击怪兽一次额外连续攻击机会，可选限定只能攻击 `target`。
--- C++: `scriptlib::duel_chain_attack`
---@param target? Card 可选：限定连击目标怪兽。
---@return nil
function Duel.ChainAttack(target) end

--- [可能协程挂起] 请求引擎重新调整场面状态（含死循环保护）。
--- C++: `scriptlib::duel_readjust`
---@return nil
function Duel.Readjust() end

--- 立即重算场上持续效果（传入 `card` 时先刷新其无效关联卡）。
--- C++: `scriptlib::duel_adjust_instantly`
---@param card? Card 可选：先刷新关联的卡片。
---@return nil
function Duel.AdjustInstantly(card) end

--- [协程挂起] 执行完整的场面状态调整与规则/自毁检查。
--- C++: `scriptlib::duel_adjust_all`
---@return nil
function Duel.AdjustAll() end

--- 取得玩家视角己方 `selfLocation` 与对方 `opponentLocation` 的全部卡片集合。
--- C++: `scriptlib::duel_get_field_group`
---@param player Player 视角玩家。
---@param selfLocation Location 己方区域掩码。
---@param opponentLocation Location 对方区域掩码。
---@return Group group
function Duel.GetFieldGroup(player, selfLocation, opponentLocation) end

--- 取得玩家视角己方 `selfLocation` 与对方 `opponentLocation` 的卡片总数。
--- C++: `scriptlib::duel_get_field_group_count`
---@param player Player 视角玩家。
---@param selfLocation Location 己方区域掩码。
---@param opponentLocation Location 对方区域掩码。
---@return integer count
function Duel.GetFieldGroupCount(player, selfLocation, opponentLocation) end

--- 取得玩家主卡组顶最多 `count` 张卡的集合。
--- C++: `scriptlib::duel_get_decktop_group`
---@param player Player 玩家编号。
---@param count integer 张数。
---@return Group group
function Duel.GetDecktopGroup(player, count) end

--- 取得玩家额外卡组里侧顶（跳过表侧灵摆）最多 `count` 张卡的集合。
--- C++: `scriptlib::duel_get_extratop_group`
---@param player Player 玩家编号。
---@param count integer 张数。
---@return Group group
function Duel.GetExtraTopGroup(player, count) end

--- 筛选玩家视角指定区域中满足过滤条件的全部卡片集合。
--- C++: `scriptlib::duel_get_matching_group`
---@param filter FilterFunction|nil 过滤函数（`nil` 表示不过滤）。
---@param player Player 区域视角玩家。
---@param selfLocation Location 己方区域掩码。
---@param opponentLocation Location 对方区域掩码。
---@param excluded Card|Group|nil 排除的单卡或集合（必填，无则传 `nil`）。
---@param ... any 透传给 `filter` 的附加参数。
---@return Group group
function Duel.GetMatchingGroup(filter, player, selfLocation, opponentLocation, excluded, ...) end

--- 统计玩家视角指定区域中满足过滤条件的卡片数量。
--- C++: `scriptlib::duel_get_matching_count`
---@param filter FilterFunction|nil 过滤函数（`nil` 表示不过滤）。
---@param player Player 区域视角玩家。
---@param selfLocation Location 己方区域掩码。
---@param opponentLocation Location 对方区域掩码。
---@param excluded Card|Group|nil 排除的单卡或集合（必填，无则传 `nil`）。
---@param ... any 透传给 `filter` 的附加参数。
---@return integer count
function Duel.GetMatchingGroupCount(filter, player, selfLocation, opponentLocation, excluded, ...) end

--- 查找并返回指定区域中第一张满足过滤条件的卡片；未找到返回 `nil`。
--- C++: `scriptlib::duel_get_first_matching_card`
---@param filter FilterFunction|nil 过滤函数。
---@param player Player 区域视角玩家。
---@param selfLocation Location 己方区域掩码。
---@param opponentLocation Location 对方区域掩码。
---@param excluded Card|Group|nil 排除的单卡或集合（必填，无则传 `nil`）。
---@param ... any 透传给 `filter` 的附加参数。
---@return Card|nil card
function Duel.GetFirstMatchingCard(filter, player, selfLocation, opponentLocation, excluded, ...) end

--- 检查指定区域中满足过滤条件的卡片是否至少达到 `count` 张。
--- C++: `scriptlib::duel_is_existing_matching_card`
---@param filter FilterFunction|nil 过滤函数。
---@param player Player 区域视角玩家。
---@param selfLocation Location 己方区域掩码。
---@param opponentLocation Location 对方区域掩码。
---@param count integer 最少所需张数。
---@param excluded Card|Group|nil 排除的单卡或集合（必填，无则传 `nil`）。
---@param ... any 透传给 `filter` 的附加参数。
---@return boolean exists
function Duel.IsExistingMatchingCard(filter, player, selfLocation, opponentLocation, count, excluded, ...) end

--- [协程挂起] 让 `player` 从 `selectPlayer` 视角指定区域中选择 `min..max` 张满足条件的卡片。
--- C++: `scriptlib::duel_select_matching_cards`
---@param player Player 执行选择的玩家（0 或 1）。
---@param filter FilterFunction|nil 过滤函数。
---@param selectPlayer Player 区域视角玩家。
---@param selfLocation Location 视角方区域掩码。
---@param opponentLocation Location 视角对手方区域掩码。
---@param min integer 最少选择张数。
---@param max integer 最多选择张数。
---@param excluded Card|Group|nil 排除的单卡或集合（必填，无则传 `nil`）。
---@param ... any 透传给 `filter` 的附加参数。
---@return Group|nil selected
function Duel.SelectMatchingCard(player, filter, selectPlayer, selfLocation, opponentLocation, min, max, excluded, ...) end

--- 取得玩家可用于解放的卡片集合。
--- C++: `scriptlib::duel_get_release_group`
---@param player Player 玩家编号。
---@param useHand? boolean 是否包含手牌（默认 `false`）。
---@param reason? Reason 解放原因（默认 `REASON_COST`）。
---@return Group|nil group
function Duel.GetReleaseGroup(player, useHand, reason) end

--- 取得玩家可用于解放的卡片数量。
--- C++: `scriptlib::duel_get_release_group_count`
---@param player Player 玩家编号。
---@param useHand? boolean 是否包含手牌（默认 `false`）。
---@param reason? Reason 解放原因（默认 `REASON_COST`）。
---@return integer|nil count
function Duel.GetReleaseGroupCount(player, useHand, reason) end

--- 检查玩家场上是否存在至少 `count` 张符合条件的 `REASON_COST` 可解放卡（并清空 `must_select_cards`）。
--- C++: `scriptlib::duel_check_release_group`
---@param player Player 玩家编号。
---@param filter FilterFunction|nil 过滤函数。
---@param count integer 最少张数。
---@param excluded Card|Group|nil 排除项（必填，无则传 `nil`）。
---@param ... any 透传给 `filter` 的附加参数。
---@return boolean|nil exists
function Duel.CheckReleaseGroup(player, filter, count, excluded, ...) end

--- [协程挂起] 让玩家从场上选择 `min..max` 张符合条件的 `REASON_COST` 可解放卡。
--- C++: `scriptlib::duel_select_release_group`
---@param player Player 玩家编号。
---@param filter FilterFunction|nil 过滤函数。
---@param min integer 最少张数。
---@param max integer 最多张数。
---@param excluded Card|Group|nil 排除项（必填，无则传 `nil`）。
---@param ... any 透传给 `filter` 的附加参数。
---@return Group|nil selected
function Duel.SelectReleaseGroup(player, filter, min, max, excluded, ...) end

--- `CheckReleaseGroup` 扩展版：可自定义 `reason` 与是否包含手牌 `useHand`（并清空 `must_select_cards`）。
--- C++: `scriptlib::duel_check_release_group_ex`
---@param player Player 玩家编号。
---@param filter FilterFunction|nil 过滤函数。
---@param count integer 最少张数。
---@param reason Reason 解放原因。
---@param useHand boolean 是否包含手牌。
---@param excluded Card|Group|nil 排除项（必填，无则传 `nil`）。
---@param ... any 透传给 `filter` 的附加参数。
---@return boolean|nil exists
function Duel.CheckReleaseGroupEx(player, filter, count, reason, useHand, excluded, ...) end

--- [协程挂起] `SelectReleaseGroup` 扩展版：可自定义 `reason` 与是否包含手牌 `useHand`。
--- C++: `scriptlib::duel_select_release_group_ex`
---@param player Player 玩家编号。
---@param filter FilterFunction|nil 过滤函数。
---@param min integer 最少张数。
---@param max integer 最多张数。
---@param reason Reason 解放原因。
---@param useHand boolean 是否包含手牌。
---@param excluded Card|Group|nil 排除项（必填，无则传 `nil`）。
---@param ... any 透传给 `filter` 的附加参数。
---@return Group|nil selected
function Duel.SelectReleaseGroupEx(player, filter, min, max, reason, useHand, excluded, ...) end

--- 取得通常召唤 `target` 时可作为祭品的卡片集合。
--- C++: `scriptlib::duel_get_tribute_group`
---@param target Card 要上位召唤的怪兽。
---@return Group group
function Duel.GetTributeGroup(target) end

--- 统计通常召唤 `target` 可提供的祭品当量（计入双祭品怪兽）。
--- C++: `scriptlib::duel_get_tribute_count`
---@param target Card 目标怪兽。
---@param mg? Group|nil 限定祭品候选集合（默认 `nil`）。
---@param ex? boolean 是否允许解放对方怪兽（默认 `false`）。
---@return integer count
function Duel.GetTributeCount(target, mg, ex) end

--- 检查是否能为 `target` 凑齐 `min..max` 个上位召唤祭品并腾出 `zone`。
--- C++: `scriptlib::duel_check_tribute`
---@param target Card 要上位召唤的怪兽。
---@param min integer 最少祭品数。
---@param max? integer|nil 最多祭品数（默认等于 `min`）。
---@param mg? Group|nil 限定祭品候选集合（默认 `nil`）。
---@param toPlayer? Player|nil 召唤到哪方场上（默认 `target:GetControler()`）。
---@param zone? Zone|nil 允许召唤到的格子掩码（默认 `0x1f`）。
---@return boolean valid
function Duel.CheckTribute(target, min, max, mg, toPlayer, zone) end

--- [协程挂起] 让玩家选择 `min..max` 个用于上位召唤 `target` 的祭品。
--- C++: `scriptlib::duel_select_tribute`
---@param player Player 执行选择的玩家（0 或 1）。
---@param target Card 要上位召唤的怪兽。
---@param min integer 最少祭品数。
---@param max integer 最多祭品数。
---@param mg? Group|nil 限定祭品候选集合（默认 `nil`）。
---@param toPlayer? Player 召唤到哪方场上（默认 `player`）。
---@return Group|nil selected
function Duel.SelectTribute(player, target, min, max, mg, toPlayer) end

--- 统计指定区域中满足过滤条件且可作为当前效果取对象目标的卡片数量（返回 `integer`）。
--- C++: `scriptlib::duel_get_target_count`
---@param filter FilterFunction|nil 过滤函数。
---@param player Player 区域视角玩家。
---@param selfLocation Location 己方区域掩码。
---@param opponentLocation Location 对方区域掩码。
---@param excluded Card|Group|nil 排除项（必填，无则传 `nil`）。
---@param ... any 透传给 `filter` 的附加参数。
---@return integer count
function Duel.GetTargetCount(filter, player, selfLocation, opponentLocation, excluded, ...) end

--- 检查指定区域中满足过滤条件且可作为当前效果对象的卡片是否至少达到 `count` 张。
--- C++: `scriptlib::duel_is_existing_target`
---@param filter FilterFunction|nil 过滤函数。
---@param player Player 区域视角玩家。
---@param selfLocation Location 己方区域掩码。
---@param opponentLocation Location 对方区域掩码。
---@param count integer 最少所需张数。
---@param excluded Card|Group|nil 排除项（必填，无则传 `nil`）。
---@param ... any 透传给 `filter` 的附加参数。
---@return boolean exists
function Duel.IsExistingTarget(filter, player, selfLocation, opponentLocation, count, excluded, ...) end

--- [协程挂起] 让玩家选择 `min..max` 张效果对象，自动登记到当前连锁 `target_cards` 并建立关联。
--- C++: `scriptlib::duel_select_target`
---@param player Player 执行选择的玩家（0 或 1）。
---@param filter FilterFunction|nil 过滤函数。
---@param selectPlayer Player 区域视角玩家。
---@param selfLocation Location 视角方区域掩码。
---@param opponentLocation Location 视角对手方区域掩码。
---@param min integer 最少选择张数。
---@param max integer 最多选择张数。
---@param excluded Card|Group|nil 排除项（必填，无则传 `nil`）。
---@param ... any 透传给 `filter` 的附加参数。
---@return Group|nil selected
function Duel.SelectTarget(player, filter, selectPlayer, selfLocation, opponentLocation, min, max, excluded, ...) end

--- 取得玩家受 `EFFECT_MUST_BE_*MATERIAL` 约束必须使用的素材卡片集合。
--- C++: `scriptlib::duel_get_must_material`
---@param player Player 玩家编号。
---@param limit EffectCode 强制素材效果代码（如 `EFFECT_MUST_BE_SMATERIAL`）。
---@return Group|nil group
function Duel.GetMustMaterial(player, limit) end

--- 检查候选素材 `materials` 是否包含全部必选素材。
--- C++: `scriptlib::duel_check_must_material`
---@param player Player 玩家编号。
---@param materials Card|Group|nil 候选素材。
---@param limit EffectCode 强制素材效果代码。
---@return boolean|nil valid
function Duel.CheckMustMaterial(player, materials, limit) end

--- [协程挂起] 让玩家从候选组 `mg` 中选择融合怪兽 `fc` 的融合素材（传入 `gc` 时自动并入结果）。
--- C++: `scriptlib::duel_select_fusion_material`
---@param player Player 执行选择的玩家。
---@param fc Card 目标融合怪兽。
---@param mg Group 候选融合素材集合。
---@param gc? Card|nil 必选素材卡（默认 `nil`）。
---@param chkf? Player 检查玩家标志（默认 `PLAYER_NONE`）。
---@param notMaterial? boolean 是否按非素材模式选择（默认 `false`）。
---@return Group|nil selected
function Duel.SelectFusionMaterial(player, fc, mg, gc, chkf, notMaterial) end

--- 直接设置当前融合召唤流程使用的素材集合 `core.fusion_materials`。
--- C++: `scriptlib::duel_set_fusion_material`
---@param group Group 融合素材集合。
---@return nil
function Duel.SetFusionMaterial(group) end

--- 直接设置当前同调召唤流程使用的素材集合 `core.synchro_materials`。
--- C++: `scriptlib::duel_set_synchro_material`
---@param group Group 同调素材集合。
---@return nil
function Duel.SetSynchroMaterial(group) end

--- 取得玩家可用于同调召唤的素材集合。
--- C++: `scriptlib::duel_get_synchro_material`
---@param player Player 玩家编号。
---@param facedown? boolean 是否包含怪兽区里侧卡（默认 `false`）。
---@return Group|nil group
function Duel.GetSynchroMaterial(player, facedown) end

--- [协程挂起] 让玩家选择同调召唤 `sc` 所需的素材集合（玩家取消召唤返回 `nil`；传入 `mg` 时会忽略 `smat`）。
--- C++: `scriptlib::duel_select_synchro_material`
---@param player Player 执行选择的玩家。
---@param sc Card 目标同调怪兽。
---@param f1 FilterFunction|nil 调整过滤函数。
---@param f2 FilterFunction|nil 非调整过滤函数。
---@param min integer 非调整最少张数。
---@param max integer 非调整最多张数。
---@param smat? Card|nil 必选素材卡（传入 `mg` 时被忽略）。
---@param mg? Group|nil 限定候选素材集合。
---@return Group|nil selected
function Duel.SelectSynchroMaterial(player, sc, f1, f2, min, max, smat, mg) end

--- 检查是否存在满足同调怪兽 `sc` 条件的合法同调素材组合。
--- C++: `scriptlib::duel_check_synchro_material`
---@param sc Card 目标同调怪兽。
---@param f1 FilterFunction|nil 调整过滤函数。
---@param f2 FilterFunction|nil 非调整过滤函数。
---@param min integer 非调整最少张数。
---@param max integer 非调整最多张数。
---@param smat? Card|nil 必选素材卡。
---@param mg? Group|nil 限定候选素材集合。
---@return boolean valid
function Duel.CheckSynchroMaterial(sc, f1, f2, min, max, smat, mg) end

--- [协程挂起] 固定指定 `tuner` 作为调整，让玩家选择其余同调素材（取消返回 `nil`）。
--- C++: `scriptlib::duel_select_tuner_material`
---@param player Player 执行选择的玩家。
---@param sc Card 目标同调怪兽。
---@param tuner Card 固定使用的调整怪兽。
---@param f1 FilterFunction|nil 调整过滤函数。
---@param f2 FilterFunction|nil 非调整过滤函数。
---@param min integer 非调整最少张数。
---@param max integer 非调整最多张数。
---@param mg? Group|nil 限定候选素材集合。
---@return Group|nil selected
function Duel.SelectTunerMaterial(player, sc, tuner, f1, f2, min, max, mg) end

--- 检查固定以 `tuner` 为调整时能否凑齐同调怪兽 `sc` 所需素材。
--- C++: `scriptlib::duel_check_tuner_material`
---@param sc Card 目标同调怪兽。
---@param tuner Card 固定调整怪兽。
---@param f1 FilterFunction|nil 调整过滤函数。
---@param f2 FilterFunction|nil 非调整过滤函数。
---@param min integer 非调整最少张数。
---@param max integer 非调整最多张数。
---@param mg? Group|nil 限定候选素材集合。
---@return boolean valid
function Duel.CheckTunerMaterial(sc, tuner, f1, f2, min, max, mg) end

--- 取得玩家用于仪式召唤（按等级）的解放素材集合。
--- C++: `scriptlib::duel_get_ritual_material`
---@param player Player 玩家编号。
---@return Group|nil group
function Duel.GetRitualMaterial(player) end

--- 取得玩家用于非等级仪式召唤（如按攻击力仪式）的解放素材集合。
--- C++: `scriptlib::duel_get_ritual_material_ex`
---@param player Player 玩家编号。
---@return Group|nil group
function Duel.GetRitualMaterialEx(player) end

--- [协程挂起] 按所在区域结算仪式召唤素材（墓地除外、超量素材/额外送墓、其余解放）。
--- C++: `scriptlib::duel_release_ritual_material`
---@param materials Group 仪式素材集合。
---@return nil
function Duel.ReleaseRitualMaterial(materials) end

--- 返回玩家可用于融合召唤的全部素材集合，以及仅来自基础区域的素材集合（双返回值）。
--- C++: `scriptlib::duel_get_fusion_material`
---@param player Player 玩家编号（0 或 1）。
---@param location? Location 基础融合区域（默认 `LOCATION_HAND + LOCATION_MZONE`）。
---@return Group|nil allMaterials 全部可用融合素材（含 `EFFECT_EXTRA_FUSION_MATERIAL`）。
---@return Group|nil baseMaterials 仅基础区域的融合素材。
function Duel.GetFusionMaterial(player, location) end

--- 检查当前召唤流程是否允许玩家取消。
--- C++: `scriptlib::duel_is_summon_cancelable`
---@return boolean cancelable
function Duel.IsSummonCancelable() end

--- 设置 `core.must_select_cards` 必选卡片列表（供 `CheckWithSum*`/`CheckReleaseGroup` 等使用）。
--- C++: `scriptlib::duel_set_must_select_cards`
---@param cards Card|Group 必选单卡或集合。
---@return nil
function Duel.SetSelectedCard(cards) end

--- 取出并清空 `core.must_select_cards` 返回为新集合。
--- C++: `scriptlib::duel_grab_must_select_cards`
---@return Group group
function Duel.GrabSelectedCard() end

--- 将卡片或集合设为当前连锁的对象卡片并建立连锁关联。
--- C++: `scriptlib::duel_set_target_card`
---@param targets Card|Group 对象单卡或集合。
---@return nil
function Duel.SetTargetCard(targets) end

--- 清空当前连锁记录的全部对象卡片。
--- C++: `scriptlib::duel_clear_target_card`
---@return nil
function Duel.ClearTargetCard() end

--- 设置当前连锁的目标玩家（`CHAININFO_TARGET_PLAYER`）。
--- C++: `scriptlib::duel_set_target_player`
---@param player Player 目标玩家编号。
---@return nil
function Duel.SetTargetPlayer(player) end

--- 设置当前连锁的目标参数（`CHAININFO_TARGET_PARAM`）。
--- C++: `scriptlib::duel_set_target_param`
---@param param integer 整型参数值。
---@return nil
function Duel.SetTargetParam(param) end

--- 为指定连锁项登记处理预报信息（用于灰流丽/星尘龙等判定效果分类与预期目标）。
--- C++: `scriptlib::duel_set_operation_info`
---@param chainIndex integer 连锁序号（`0` 为当前构造中的连锁）。
---@param category EffectCategory 效果分类位掩码（`CATEGORY_*`）。
---@param targets Card|Group|nil 预期目标单卡或集合（不确定时传 `nil`）。
---@param count integer 预期数量。
---@param targetPlayer Player 预期目标玩家。
---@param targetParam integer 预期参数（如区域或伤害值）。
---@return nil
function Duel.SetOperationInfo(chainIndex, category, targets, count, targetPlayer, targetParam) end

--- 查询指定连锁项是否登记了某 `category` 的处理预报信息；命中返回 5 个值，未命中返回单个 `false`。
--- C++: `scriptlib::duel_get_operation_info`
---@param chainIndex integer 连锁序号。
---@param category EffectCategory 要查询的效果分类。
---@return boolean|nil found 是否存在该分类记录（连锁不存在无返回值）。
---@return Group|nil targets 登记的目标集合。
---@return integer? count 登记的数量。
---@return Player? targetPlayer 登记的目标玩家。
---@return integer? targetParam 登记的目标参数。
function Duel.GetOperationInfo(chainIndex, category) end

--- 取得指定连锁项已登记的 OperationInfo 条目数。
--- C++: `scriptlib::duel_get_operation_count`
---@param chainIndex integer 连锁序号。
---@return integer|nil count
function Duel.GetOperationCount(chainIndex) end

--- 清空并释放指定连锁项的全部 OperationInfo。
--- C++: `scriptlib::duel_clear_operation_info`
---@param chainIndex integer 连锁序号。
---@return nil
function Duel.ClearOperationInfo(chainIndex) end

--- 检查是否存在满足超量怪兽 `sc` 条件的合法超量素材（注意第 6 参 `mg` 必传，不限定时显式传 `nil`）。
--- C++: `scriptlib::duel_check_xyz_material`
---@param sc Card 目标超量怪兽。
---@param filter FilterFunction|nil 素材过滤函数。
---@param lv integer 所需超量素材等级。
---@param minc integer 最少素材数。
---@param maxc integer 最多素材数。
---@param mg Group|nil 限定候选素材集合（必填，不限定则显式传 `nil`）。
---@return boolean valid
function Duel.CheckXyzMaterial(sc, filter, lv, minc, maxc, mg) end

--- [协程挂起] 让玩家选择超量怪兽 `sc` 的素材（玩家取消返回 `nil`；第 7 参 `mg` 可省略）。
--- C++: `scriptlib::duel_select_xyz_material`
---@param player Player 执行选择的玩家。
---@param sc Card 目标超量怪兽。
---@param filter FilterFunction|nil 素材过滤函数。
---@param lv integer 所需等级。
---@param minc integer 最少素材数。
---@param maxc integer 最多素材数。
---@param mg? Group|nil 限定候选素材集合（默认 `nil`）。
---@return Group|nil selected
function Duel.SelectXyzMaterial(player, sc, filter, lv, minc, maxc, mg) end

--- [协程挂起] 将卡片或集合重叠到超量怪兽 `target` 下方作为超量素材。
--- C++: `scriptlib::duel_overlay`
---@param target Card 目标超量怪兽。
---@param materials Card|Group 要叠放的单卡或集合。
---@param adjust? boolean 叠放后是否触发 `adjust_all`（默认 `true`）。
---@return nil
function Duel.Overlay(target, materials, adjust) end

--- 取得指定区域上的全部超量素材集合。
--- C++: `scriptlib::duel_get_overlay_group`
---@param player Player 视角玩家。
---@param selfLocation Location 己方区域。
---@param opponentLocation Location 对方区域。
---@return Group|nil group
function Duel.GetOverlayGroup(player, selfLocation, opponentLocation) end

--- 取得指定区域上的超量素材总数。
--- C++: `scriptlib::duel_get_overlay_count`
---@param player Player 视角玩家。
---@param selfLocation Location 己方区域。
---@param opponentLocation Location 对方区域。
---@return integer|nil count
function Duel.GetOverlayCount(player, selfLocation, opponentLocation) end

--- 检查玩家能否从指定区域取除至少 `count` 个超量素材。
--- C++: `scriptlib::duel_check_remove_overlay_card`
---@param player Player 玩家编号。
---@param selfLocation Location 己方区域。
---@param opponentLocation Location 对方区域。
---@param count integer 最少张数。
---@param reason Reason 原因。
---@return boolean|nil canRemove
function Duel.CheckRemoveOverlayCard(player, selfLocation, opponentLocation, count, reason) end

--- [协程挂起] 让玩家从指定区域取除 `min..max` 个超量素材，返回实际取除数。
--- C++: `scriptlib::duel_remove_overlay_card`
---@param player Player 玩家编号。
---@param selfLocation Location 己方区域。
---@param opponentLocation Location 对方区域。
---@param min integer 最少取除数。
---@param max integer 最多取除数。
---@param reason Reason 原因。
---@return integer|nil removed
function Duel.RemoveOverlayCard(player, selfLocation, opponentLocation, min, max, reason) end

--- 向客户端发送 `MSG_HINT` 提示消息（`description` 必须为整型 ID，如 `HINTMSG_*` 或 `aux.Stringid(...)`）。
--- C++: `scriptlib::duel_hint`
---@param hintType Hint 提示类型（`HINT_*`）。
---@param player Player 玩家编号（`HINT_OPSELECTED` 会自动转为通知对手 `1 - player`）。
---@param description integer 提示文本或卡号整数值。
---@return nil
function Duel.Hint(hintType, player, description) end

--- 读取玩家最近一次收到的 `HINT_SELECTMSG` 提示值。
--- C++: `scriptlib::duel_get_last_select_hint`
---@param player? Player 玩家编号（默认当前 `reason_player`）。
---@return integer|nil hint
function Duel.GetLastSelectHint(player) end

--- 向客户端发送 `MSG_BECOME_TARGET` 高亮提示所选非取对象卡片集合。
--- C++: `scriptlib::duel_hint_selection`
---@param group Group 要高亮提示的卡片集合。
---@return nil
function Duel.HintSelection(group) end

--- [协程挂起] 询问玩家是否适用/发动卡片 `card` 的效果。
--- C++: `scriptlib::duel_select_effect_yesno`
---@param player Player 玩家编号。
---@param card Card 关联卡片。
---@param description? integer 提示文本 ID（默认 `95`）。
---@return boolean|nil yes
function Duel.SelectEffectYesNo(player, card, description) end

--- [协程挂起] 向玩家弹出是否确认框（`description` 为整型描述 ID）。
--- C++: `scriptlib::duel_select_yesno`
---@param player Player 玩家编号。
---@param description integer 提示文本 ID。
---@return boolean|nil yes
function Duel.SelectYesNo(player, description) end

--- [协程挂起] 向玩家展示 1..N 个文本描述选项并返回选中的 0-based 索引（自动向对手广播 `HINT_OPSELECTED`）。
--- C++: `scriptlib::duel_select_option`
---@param player Player 玩家编号。
---@param ... integer 选项描述文本 ID 列表。
---@return integer|nil optionIndex 0-based 选中索引。
function Duel.SelectOption(player, ...) end

--- 保留空占位函数（仅检查操作权限，无参数无返回值）。
--- C++: `scriptlib::duel_select_sequence`
---@return nil
function Duel.SelectSequence() end

--- [协程挂起] 让玩家为卡片 `card` 从候选表示形式掩码中选择一种表示形式。
--- C++: `scriptlib::duel_select_position`
---@param player Player 玩家编号。
---@param card Card 目标卡片。
---@param positions Position 可选表示形式位掩码。
---@return Position position
function Duel.SelectPosition(player, card, positions) end

--- [协程挂起] 按玩家视角任选 `count` 个格子（不要求空闲）；传入 `hintCode` 时切换为 `PROCESSOR_SELECT_PLACE`。
--- C++: `scriptlib::duel_select_field`
---@param player Player 玩家编号。
---@param count integer 格子数。
---@param selfLocation Location 己方区域。
---@param opponentLocation Location 对方区域。
---@param filter Zone 排除的格子掩码。
---@param hintCode? integer 可选：发往 `HINT_SELECTMSG` 的提示卡号/ID。
---@return Zone|nil zone
function Duel.SelectField(player, count, selfLocation, opponentLocation, filter, hintCode) end

--- [协程挂起] 让玩家从当前可用空格中选择 `count` 个格子并返回格子位掩码。
--- C++: `scriptlib::duel_select_disable_field`
---@param player Player 玩家编号。
---@param count integer 需选择的格子数。
---@param selfLocation Location 己方区域。
---@param opponentLocation Location 对方区域。
---@param filter Zone 排除/禁止选择的格子位掩码。
---@return Zone|nil zone
function Duel.SelectDisableField(player, count, selfLocation, opponentLocation, filter) end

--- [协程挂起] 让玩家从 `available` 掩码中宣言 `count` 个种族。
--- C++: `scriptlib::duel_announce_race`
---@param player Player 玩家编号。
---@param count integer 宣言数量。
---@param available Race 可选种族位掩码。
---@return Race race
function Duel.AnnounceRace(player, count, available) end

--- [协程挂起] 让玩家从 `available` 掩码中宣言 `count` 个属性。
--- C++: `scriptlib::duel_announce_attribute`
---@param player Player 玩家编号。
---@param count integer 宣言数量。
---@param available Attribute 可选属性位掩码。
---@return Attribute attribute
function Duel.AnnounceAttribute(player, count, available) end

--- [协程挂起] 让玩家在 `[min, max]` 区间内宣言一个等级，返回 `(等级值, 0-based 索引)` 双值。
--- C++: `scriptlib::duel_announce_level`
---@param player Player 玩家编号。
---@param min? integer|nil 最小等级（默认 `1`）。
---@param max? integer|nil 最大等级（默认 `12`）。
---@param ... integer|nil 额外排除的等级值（`nil` 忽略）。
---@return integer|nil level 宣言的等级值。
---@return integer|nil index 选项索引。
function Duel.AnnounceLevel(player, min, max, ...) end

--- [协程挂起] 按可选后缀表达式（`OPCODE_*`）过滤后让玩家宣言一个卡名，返回所宣言卡号。
--- C++: `scriptlib::duel_announce_card`
---@param player Player 玩家编号。
---@param ... Opcode|integer 可选后缀表达式过滤指令流（省略默认允许全部卡）。
---@return CardCode code
function Duel.AnnounceCard(player, ...) end

--- [协程挂起] 让玩家宣言卡片种类（`0`=怪兽、`1`=魔法、`2`=陷阱）。
--- C++: `scriptlib::duel_announce_type`
---@param player Player 玩家编号。
---@return integer cardKind 0=怪兽，1=魔法，2=陷阱。
function Duel.AnnounceType(player) end

--- [协程挂起] 让玩家从候选整数列表中宣言一个数字，返回 `(选中数值, 0-based 索引)` 双值。
--- C++: `scriptlib::duel_announce_number`
---@param player Player 玩家编号。
---@param num integer 首个候选数字。
---@param ... integer 其余候选数字。
---@return integer value 选中的数字。
---@return integer index 选中的 0-based 索引。
function Duel.AnnounceNumber(player, num, ...) end

--- [协程挂起] 让玩家宣言硬币正反面（`0`=正面/表、`1`=反面/里）。
--- C++: `scriptlib::duel_announce_coin`
---@param player Player 玩家编号。
---@return integer side 0=正面，1=反面。
function Duel.AnnounceCoin(player) end

--- [协程挂起] 掷 `count` 枚硬币（`-1` 为特殊标记），多值返回每枚硬币结果（`1`=正、`0`=反）。
--- C++: `scriptlib::duel_toss_coin`
---@param player Player 玩家编号。
---@param count integer 硬币数量。
---@return integer ... 硬币结果列表。
function Duel.TossCoin(player, count) end

--- [协程挂起] 掷骰子（己方 `count1` 个、对方 `count2` 个，合计上限 5），多值返回各骰子点数。
--- C++: `scriptlib::duel_toss_dice`
---@param player Player 玩家编号。
---@param count1 integer 己方骰子数。
---@param count2? integer 对方骰子数（默认 `0`）。
---@return integer ... 骰子点数列表。
function Duel.TossDice(player, count1, count2) end

--- [协程挂起] 让双方猜拳，返回获胜玩家编号。
--- C++: `scriptlib::duel_rock_paper_scissors`
---@param repeatOnDraw? boolean 平局是否重来（默认 `true`）。
---@return Player winner
function Duel.RockPaperScissors(repeatOnDraw) end

--- 多值返回最近一次掷硬币的全部结果。
--- C++: `scriptlib::duel_get_coin_result`
---@return integer ... 硬币结果列表。
function Duel.GetCoinResult() end

--- 固定返回最近一次掷骰子的 5 个槽位点数。
--- C++: `scriptlib::duel_get_dice_result`
---@return integer d1
---@return integer d2
---@return integer d3
---@return integer d4
---@return integer d5
function Duel.GetDiceResult() end

--- 覆写最近一次掷硬币结果（非 0/1 归一化为 `0`）。
--- C++: `scriptlib::duel_set_coin_result`
---@param ... integer 硬币结果（最多读取 `MAX_COIN_COUNT` 个）。
---@return nil
function Duel.SetCoinResult(...) end

--- 覆写最近一次掷骰子结果（最多 5 个，超出 `1..255` 归一化为 `1`）。
--- C++: `scriptlib::duel_set_dice_result`
---@param ... integer 骰子点数（最多 5 个）。
---@return nil
function Duel.SetDiceResult(...) end

--- 多值返回作用于指定玩家且满足次数限制的全部匹配 `code` 效果；无匹配返回 `nil`。
--- C++: `scriptlib::duel_is_player_affected_by_effect`
---@param player Player 玩家编号（0 或 1）。
---@param code EffectCode 效果代码。
---@return Effect ... 匹配的效果列表；无匹配返回 `nil`。
function Duel.IsPlayerAffectedByEffect(player, code) end

--- 检查玩家能否抽卡（`count > 0` 时同时检查主卡组张数 `>= count`）。
--- C++: `scriptlib::duel_is_player_can_draw`
---@param player Player 玩家编号。
---@param count? integer 抽卡张数（默认 `0` 仅查禁止抽卡效果）。
---@return boolean allowed
function Duel.IsPlayerCanDraw(player, count) end

--- 检查玩家能否将主卡组顶 `count` 张卡送去墓地。
--- C++: `scriptlib::duel_is_player_can_discard_deck`
---@param player Player 玩家编号。
---@param count integer 张数。
---@return boolean allowed
function Duel.IsPlayerCanDiscardDeck(player, count) end

--- 检查玩家能否作为代价将主卡组顶 `count` 张卡送去墓地。
--- C++: `scriptlib::duel_is_player_can_discard_deck_as_cost`
---@param player Player 玩家编号。
---@param count integer 张数。
---@return boolean allowed
function Duel.IsPlayerCanDiscardDeckAsCost(player, count) end

--- 检查玩家能否通常召唤（1 参查整体 `EFFECT_CANNOT_SUMMON`；3 参查能否以 `summonType` 通常召唤 `card`）。
--- C++: `scriptlib::duel_is_player_can_summon`
---@param player Player 玩家编号。
---@param summonType? SummonType 可选：召唤类型。
---@param card? Card 可选：目标怪兽（传第 2 参时必传）。
---@return boolean allowed
---@overload fun(player: Player): boolean
---@overload fun(player: Player, summonType: SummonType, card: Card): boolean
function Duel.IsPlayerCanSummon(player, summonType, card) end

--- 检查玩家能否通常盖放怪兽（1 参查整体 `EFFECT_CANNOT_MSET`；3 参查指定怪兽）。
--- C++: `scriptlib::duel_is_player_can_mset`
---@param player Player 玩家编号。
---@param summonType? SummonType 可选：召唤类型。
---@param card? Card 可选：目标怪兽（传第 2 参时必传）。
---@return boolean allowed
---@overload fun(player: Player): boolean
---@overload fun(player: Player, summonType: SummonType, card: Card): boolean
function Duel.IsPlayerCanMSet(player, summonType, card) end

--- 检查玩家能否盖放魔陷卡（1 参查玩家整体权限；2 参查对指定卡片的权限）。
--- C++: `scriptlib::duel_is_player_can_sset`
---@param player Player 玩家编号（0 或 1）。
---@param card? Card 可选：指定要检查的卡片。
---@return boolean allowed
---@overload fun(player: Player): boolean
---@overload fun(player: Player, card: Card): boolean
function Duel.IsPlayerCanSSet(player, card) end

--- 检查玩家能否特殊召唤（1 参查整体权限；5 参查能否按指定参数特召 `card`）。
--- C++: `scriptlib::duel_is_player_can_spsummon`
---@param player Player 玩家编号。
---@param summonType? SummonType 可选：召唤类型。
---@param position? Position 可选：表示形式。
---@param toPlayer? Player 可选：召唤目标玩家。
---@param card? Card 可选：目标怪兽（传 >=2 参时必须提供全部 5 参）。
---@return boolean allowed
---@overload fun(player: Player): boolean
---@overload fun(player: Player, summonType: SummonType, position: Position, toPlayer: Player, card: Card): boolean
function Duel.IsPlayerCanSpecialSummon(player, summonType, position, toPlayer, card) end

--- 检查玩家能否反转召唤（1 参查玩家整体权限；2 参查对指定卡片的权限）。
--- C++: `scriptlib::duel_is_player_can_flipsummon`
---@param player Player 玩家编号（0 或 1）。
---@param card? Card 可选：指定要检查的卡片。
---@return boolean allowed
---@overload fun(player: Player): boolean
---@overload fun(player: Player, card: Card): boolean
function Duel.IsPlayerCanFlipSummon(player, card) end

--- 检查玩家能否将虚拟属性（衍生物/陷阱怪兽等）按指定参数特殊召唤（3~9 参为 `nil` 时沿用 `code` 的卡库默认值）。
--- C++: `scriptlib::duel_is_player_can_spsummon_monster`
---@param player Player 执行召唤的玩家。
---@param code CardCode 卡号（用于读取卡库初始值）。
---@param setcode? SetCode|nil 覆写字段编码。
---@param cardType? CardType|nil 覆写种类。
---@param atk? integer|nil 覆写攻击力。
---@param def? integer|nil 覆写守备力。
---@param level? integer|nil 覆写等级。
---@param race? Race|nil 覆写种族。
---@param attribute? Attribute|nil 覆写属性。
---@param position? Position 表示形式（默认 `POS_FACEUP`）。
---@param toPlayer? Player 召唤到哪方场上（默认 `player`）。
---@param summonType? SummonType 召唤类型（默认 `0`）。
---@return boolean allowed
function Duel.IsPlayerCanSpecialSummonMonster(player, code, setcode, cardType, atk, def, level, race, attribute, position, toPlayer, summonType) end

--- 检查玩家本回合剩余特召次数上限是否允许再特召 `count` 次。
--- C++: `scriptlib::duel_is_player_can_spsummon_count`
---@param player Player 玩家编号。
---@param count integer 需要的特召次数。
---@return boolean allowed
function Duel.IsPlayerCanSpecialSummonCount(player, count) end

--- 检查玩家能否解放（1 参查玩家整体权限；2~3 参查对指定卡片的权限）。
--- C++: `scriptlib::duel_is_player_can_release`
---@param player Player 玩家编号（0 或 1）。
---@param card? Card 可选：指定卡片。
---@param reason? Reason 可选：原因位掩码（默认 `REASON_COST`）。
---@return boolean allowed
---@overload fun(player: Player): boolean
---@overload fun(player: Player, card: Card, reason?: Reason): boolean
function Duel.IsPlayerCanRelease(player, card, reason) end

--- 检查玩家能否除外（1 参查玩家整体权限；2~3 参查对指定卡片的权限）。
--- C++: `scriptlib::duel_is_player_can_remove`
---@param player Player 玩家编号（0 或 1）。
---@param card? Card 可选：指定卡片。
---@param reason? Reason 可选：原因位掩码（默认 `REASON_EFFECT`）。
---@return boolean allowed
---@overload fun(player: Player): boolean
---@overload fun(player: Player, card: Card, reason?: Reason): boolean
function Duel.IsPlayerCanRemove(player, card, reason) end

--- 检查玩家能否将卡片加入手牌（1 参查玩家整体权限；2 参查对指定卡片的权限）。
--- C++: `scriptlib::duel_is_player_can_send_to_hand`
---@param player Player 玩家编号（0 或 1）。
---@param card? Card 可选：指定要检查的卡片。
---@return boolean allowed
---@overload fun(player: Player): boolean
---@overload fun(player: Player, card: Card): boolean
function Duel.IsPlayerCanSendtoHand(player, card) end

--- 检查玩家能否将卡片送去墓地（1 参查玩家整体权限；2 参查对指定卡片的权限）。
--- C++: `scriptlib::duel_is_player_can_send_to_grave`
---@param player Player 玩家编号（0 或 1）。
---@param card? Card 可选：指定要检查的卡片。
---@return boolean allowed
---@overload fun(player: Player): boolean
---@overload fun(player: Player, card: Card): boolean
function Duel.IsPlayerCanSendtoGrave(player, card) end

--- 检查玩家能否将卡片送回卡组（1 参查玩家整体权限；2 参查对指定卡片的权限）。
--- C++: `scriptlib::duel_is_player_can_send_to_deck`
---@param player Player 玩家编号（0 或 1）。
---@param card? Card 可选：指定要检查的卡片。
---@return boolean allowed
---@overload fun(player: Player): boolean
---@overload fun(player: Player, card: Card): boolean
function Duel.IsPlayerCanSendtoDeck(player, card) end

--- 检查玩家本回合是否尚未使用额外通常召唤标记。
--- C++: `scriptlib::duel_is_player_can_additional_summon`
---@param player Player 玩家编号。
---@return boolean allowed
function Duel.IsPlayerCanAdditionalSummon(player) end

--- 检查当前是否正处于连锁逆序结算阶段。
--- C++: `scriptlib::duel_is_chain_solving`
---@return boolean solving
function Duel.IsChainSolving() end

--- 历史兼容接口：检查连锁能否无效发动（要求 1 参，原生层恒返回 `true`）。
--- C++: `scriptlib::duel_is_chain_negatable`
---@param chainIndex integer 连锁序号（未使用）。
---@return boolean negatable
function Duel.IsChainNegatable(chainIndex) end

--- 在连锁结算期间检查指定连锁项的效果能否被无效（非结算期恒返回 `true`）。
--- C++: `scriptlib::duel_is_chain_disablable`
---@param chainIndex integer 连锁序号。
---@return boolean disablable
function Duel.IsChainDisablable(chainIndex) end

--- 在连锁结算期间检查指定连锁项是否已被无效（非结算期恒返回 `false`）。
--- C++: `scriptlib::duel_is_chain_disabled`
---@param chainIndex integer 连锁序号。
---@return boolean disabled
function Duel.IsChainDisabled(chainIndex) end

--- 检查卡片 `card` 是否可以成为指定连锁项的合法转移对象。
--- C++: `scriptlib::duel_check_chain_target`
---@param chainIndex integer 连锁序号。
---@param card Card 候选新对象卡片。
---@return boolean valid
function Duel.CheckChainTarget(chainIndex, card) end

--- 检查当前连锁串中是否不存在同名卡发动（空连锁返回 `true`）。
--- C++: `scriptlib::duel_check_chain_uniqueness`
---@return boolean unique
function Duel.CheckChainUniqueness() end

--- 查询玩家本回合一项或多项内置行动计数（`ACTIVITY_SUMMON=1`、`NORMALSUMMON=2`、`SPSUMMON=3`、`FLIPSUMMON=4`、`ATTACK=5`、`BATTLE_PHASE=6`）。
--- C++: `scriptlib::duel_get_activity_count`
---@param player Player 玩家编号（0 或 1）。
---@param activityType ActivityType 首个行动类型常量。
---@param ... ActivityType 更多要同时查询的行动类型。
---@return integer ... 与输入行动类型一一对应的计数值。
function Duel.GetActivityCount(player, activityType, ...) end

--- 检查当前阶段是否已经发生过玩家行动。
--- C++: `scriptlib::duel_check_phase_activity`
---@return boolean active
function Duel.CheckPhaseActivity() end

--- 注册自定义行动计数器（用于“此卡发动的回合，自己不是 XX 怪兽不能特召”等誓约判定）。
--- C++: `scriptlib::duel_add_custom_activity_counter`
---@param counterId integer 计数器 ID（通常为卡号）。
---@param activityType ActivityType 监听的行动类型（支持 `SUMMON`/`NORMALSUMMON`/`SPSUMMON`/`FLIPSUMMON`/`ATTACK`/`CHAIN`）。
---@param filter FilterFunction|EffectFilterFunction 满足条件时不计入违约的过滤函数（`ACTIVITY_CHAIN` 时回调参数为 `Effect`，其余为 `Card`）。
---@return nil
function Duel.AddCustomActivityCounter(counterId, activityType, filter) end

--- 查询指定自定义行动计数器的本回合违约计数。
--- C++: `scriptlib::duel_get_custom_activity_count`
---@param counterId integer 计数器 ID。
---@param player Player 玩家编号（0 或 1）。
---@param activityType ActivityType 行动类型。
---@return integer count
function Duel.GetCustomActivityCount(counterId, player, activityType) end

--- 取得玩家本回合怪兽进行战斗的次数。
--- C++: `scriptlib::duel_get_battled_count`
---@param player Player 玩家编号（0 或 1）。
---@return integer|nil count
function Duel.GetBattledCount(player) end

--- 检查当前回合是否允许进入战斗阶段。
--- C++: `scriptlib::duel_is_able_to_enter_bp`
---@return boolean able
function Duel.IsAbleToEnterBP() end

--- 互换指定玩家的主卡组与墓地（现世与冥界的逆转专用）。
--- C++: `scriptlib::duel_swap_deck_and_grave`
---@param player Player 玩家编号。
---@return nil
function Duel.SwapDeckAndGrave(player) end

--- 救世星龙/救世魔龙专用：克隆 `copyTarget` 的初始怪兽入连锁效果给 `dst` 至回合结束（必发转选发）。
--- C++: `scriptlib::duel_majestic_copy`
---@param dst Card 接收效果的卡片。
---@param copyTarget Card 被复制效果的源怪兽。
---@return nil
function Duel.MajesticCopy(dst, copyTarget) end

-------------------------------------------------------------------------------
-- Debug API (11 functions)
-------------------------------------------------------------------------------

--- 将任意 Lua 值经 `tostring` 转换后作为调试消息（message_type=2）发送给宿主。
--- C++: `scriptlib::debug_message`
---@param message any 任意可转为字符串的调试内容。
---@return nil
function Debug.Message(message) end

--- 在残局/调试脚本中向指定区域直接放置一张卡片；若目标怪兽区已有超量怪兽则附加为其超量素材。
--- C++: `scriptlib::debug_add_card`
---@param code CardCode 卡号。
---@param owner Player 卡片持有者（0 或 1）。
---@param player Player 放置目标玩家（0 或 1）。
---@param location Location 目标区域（支持 `LOCATION_PZONE` 自动映射灵摆格）。
---@param sequence integer 区域内序号。
---@param position Position 表示形式（`LOCATION_EXTRA` 且传 `0` 时自动置为 `POS_FACEDOWN_DEFENSE`）。
---@param proc? boolean 是否同时设置 `STATUS_PROC_COMPLETE`（视为已完成正规召唤手续，默认 `false`）。
---@return Card|nil card 成功创建并放置时返回新卡片；玩家或目标格子非法时无返回值（`nil`）。
function Debug.AddCard(code, owner, player, location, sequence, position, proc) end

--- 设置调试对局中玩家的 LP、初手张数与每回合通常抽卡数。
--- C++: `scriptlib::debug_set_player_info`
---@param player Player 玩家编号（0 或 1）。
---@param lp integer 初始生命值。
---@param startCount integer 初始手牌张数。
---@param drawCount integer 每回合通常抽卡张数。
---@return nil
function Debug.SetPlayerInfo(player, lp, startCount, drawCount) end

--- 为已放置的卡片预先写入召唤方式与召唤前所在区域（用于残局环境模拟）。
--- C++: `scriptlib::debug_pre_summon`
---@param card Card 目标卡片。
---@param summonType SummonType 召唤类型常量（如 `SUMMON_TYPE_SYNCHRO`）。
---@param summonLocation? Location 召唤前所在区域（默认 `0`）。
---@return nil
function Debug.PreSummon(card, summonType, summonLocation) end

--- 在残局初始化阶段将魔陷区的装备卡预先装备给场上表侧怪兽。
--- C++: `scriptlib::debug_pre_equip`
---@param equipCard Card 位于 `LOCATION_SZONE` 的装备卡。
---@param target Card 位于 `LOCATION_MZONE` 且为表侧表示的装备目标怪兽。
---@return boolean success 满足位置与表侧条件并成功装备返回 `true`，否则返回 `false`。
function Debug.PreEquip(equipCard, target) end

--- 在残局初始化阶段预先建立 `card` 指向 `target` 的持续取对象关联。
--- C++: `scriptlib::debug_pre_set_target`
---@param card Card 取对象来源卡片。
---@param target Card 被持续取对象的目标卡片。
---@return nil
function Debug.PreSetTarget(card, target) end

--- 在残局初始化阶段直接向卡片累加指定种类的指示物（不经过效果检查）。
--- C++: `scriptlib::debug_pre_add_counter`
---@param card Card 目标卡片。
---@param counterType CounterType 指示物类型编码。
---@param count? integer 放置数量（默认 `1`）。
---@return nil
function Debug.PreAddCounter(card, counterType, count) end

--- 清空当前对局状态并开始残局重载，配置对局选项与大师规则版本。
--- C++: `scriptlib::debug_reload_field_begin`
---@param flag DuelOption 对局选项位掩码（`DUEL_*`）。
---@param rule? integer 大师规则编号（省略或 `0` 时：含 `DUEL_OBSOLETE_RULING` 取 `1`，否则取最新规则 `5`）。
---@return nil
function Debug.ReloadFieldBegin(flag, rule) end

--- 结束残局重载并向客户端广播完整场地快照（`MSG_RELOAD_FIELD`）。
--- C++: `scriptlib::debug_reload_field_end`
---@return nil
function Debug.ReloadFieldEnd() end

--- 向客户端发送 `MSG_AI_NAME` 设置残局/AI 对手名称（最长截断至 127 字节）。
--- C++: `scriptlib::debug_set_ai_name`
---@param name string AI 名称字符串。
---@return nil
function Debug.SetAIName(name) end

--- 将任意值经 `tostring` 转换后作为 `MSG_SHOW_HINT` 提示框文本发送给客户端（最长截断至 1023 字节）。
--- C++: `scriptlib::debug_show_hint`
---@param message any 提示内容。
---@return nil
function Debug.ShowHint(message) end
