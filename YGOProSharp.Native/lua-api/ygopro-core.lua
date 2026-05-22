---@meta
---@diagnostic disable: lowercase-global, missing-return, unused-local, duplicate-set-field

--- ygopro-core Lua API 类型声明。
---
--- 该文件由 `xmake gen-lua-api -P YGOProSharp.Native` 从 YGOProSharp.Native/ygopro-core/lib*.cpp 的
--- luaL_Reg 注册表生成，覆盖 native core 实际注册到 Lua 的 Card、Effect、Group、
--- Duel、Debug API。它只用于 LuaLS/EmmyLua 补全和静态检查，不应被对局运行时加载。
--- 高频 API 采用明确签名；无法从 C++ 注册表安全推断的长尾 API 使用保守签名，并保留来源函数。

---@alias Player integer 玩家编号，通常为 0 或 1。
---@alias CardCode integer 卡号。
---@alias Location integer 位置 bitmask，例如 LOCATION_MZONE / LOCATION_GRAVE。
---@alias Position integer 表示形式 bitmask，例如 POS_FACEUP_ATTACK。
---@alias Reason integer 原因 bitmask，例如 REASON_EFFECT / REASON_COST。
---@alias Reset integer reset bitmask，例如 RESET_EVENT + RESETS_STANDARD。
---@alias EffectCode integer 效果代码，例如 EFFECT_UPDATE_ATTACK。
---@alias EffectType integer 效果类型 bitmask，例如 EFFECT_TYPE_SINGLE。
---@alias EffectProperty integer 效果属性 bitmask，例如 EFFECT_FLAG_CARD_TARGET。
---@alias EffectCategory integer 效果分类 bitmask，例如 CATEGORY_DESTROY。
---@alias Race integer 种族 bitmask。
---@alias Attribute integer 属性 bitmask。
---@alias Phase integer 阶段常量，例如 PHASE_MAIN1。
---@alias SummonType integer 召唤类型常量。
---@alias ChainInfo integer 连锁信息查询常量，例如 CHAININFO_TARGET_CARDS。
---@alias QueryFlag integer query_card / query_field_card 查询 bitmask。
---@alias Hint integer 提示类型，例如 HINT_SELECTMSG。
---@alias Zone integer 区域 bitmask。
---@alias CountLimitCode integer 次数限制 code，可以是卡号或自定义 id。

---@alias FilterFunction fun(c: Card, ...: any): boolean|integer
---@alias MaterialFilter fun(c: Card, ...: any): boolean|integer
---@alias ClassValueFunction fun(c: Card, ...: any): integer|string|boolean
---@alias SumValueFunction fun(c: Card, ...: any): integer
---@alias ValueFunction fun(e: Effect, c: Card|nil, ...: any): integer|boolean|Card|Group|nil
---@alias ChainLimitFunction fun(e: Effect, ep: Player, tp: Player): boolean|integer
---@alias ConditionFunction fun(e: Effect, tp: Player, eg: Group, ep: Player, ev: integer, re: Effect, r: Reason, rp: Player): boolean|integer
---@alias CostFunction fun(e: Effect, tp: Player, eg: Group, ep: Player, ev: integer, re: Effect, r: Reason, rp: Player, chk: integer): boolean|integer|nil
---@alias TargetFunction fun(e: Effect, tp: Player, eg: Group, ep: Player, ev: integer, re: Effect, r: Reason, rp: Player, chk: integer, chkc?: Card): boolean|integer|nil
---@alias OperationFunction fun(e: Effect, tp: Player, eg: Group, ep: Player, ev: integer, re: Effect, r: Reason, rp: Player): nil
---@alias InitialEffectFunction fun(c: Card): nil

---@class Card
Card = {}

---@class Effect
Effect = {}

---@class Group
Group = {}

---@class Duel
Duel = {}

---@class Debug
Debug = {}

-------------------------------------------------------------------------------
-- Card API (276 functions)
-------------------------------------------------------------------------------

--- 取得当前代码；部分状态下可能额外返回另一个代码。
--- C++: `scriptlib::card_get_code`
---@return CardCode code
---@return CardCode? anotherCode
function Card:GetCode() end

--- 取得原始卡号。
--- C++: `scriptlib::card_get_origin_code`
---@return CardCode code
function Card:GetOriginalCode() end

--- 取得规则视角原始卡号，可能返回两个代码。
--- C++: `scriptlib::card_get_origin_code_rule`
---@return CardCode code
---@return CardCode? secondCode
function Card:GetOriginalCodeRule() end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_fusion_code`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetFusionCode(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_link_code`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetLinkCode(...) end

--- 检查卡片条件 IsFusionCode。
--- C++: `scriptlib::card_is_fusion_code`
---@param ... any
---@return boolean result
function Card:IsFusionCode(...) end

--- 检查卡片条件 IsLinkCode。
--- C++: `scriptlib::card_is_link_code`
---@param ... any
---@return boolean result
function Card:IsLinkCode(...) end

--- 检查卡片条件 IsSetCard。
--- C++: `scriptlib::card_is_set_card`
---@param ... any
---@return boolean result
function Card:IsSetCard(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_origin_set_card`
---@param ... any
---@return any result
function Card:IsOriginalSetCard(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_pre_set_card`
---@param ... any
---@return any result
function Card:IsPreviousSetCard(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_fusion_set_card`
---@param ... any
---@return any result
function Card:IsFusionSetCard(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_link_set_card`
---@param ... any
---@return any result
function Card:IsLinkSetCard(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_special_summon_set_card`
---@param ... any
---@return any result
function Card:IsSpecialSummonSetCard(...) end

--- 取得卡片字段 GetType。
--- C++: `scriptlib::card_get_type`
---@param ... any
---@return integer value
function Card:GetType() end

--- 取得卡片字段 GetOriginalType。
--- C++: `scriptlib::card_get_origin_type`
---@param ... any
---@return integer value
function Card:GetOriginalType() end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_fusion_type`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetFusionType(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_synchro_type`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetSynchroType(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_xyz_type`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetXyzType(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_link_type`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetLinkType(...) end

--- 取得卡片字段 GetLevel。
--- C++: `scriptlib::card_get_level`
---@param ... any
---@return integer value
function Card:GetLevel() end

--- 取得卡片字段 GetRank。
--- C++: `scriptlib::card_get_rank`
---@param ... any
---@return integer value
function Card:GetRank() end

--- 取得卡片字段 GetLink。
--- C++: `scriptlib::card_get_link`
---@param ... any
---@return integer value
function Card:GetLink() end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_synchro_level`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetSynchroLevel(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_ritual_level`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetRitualLevel(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_origin_level`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetOriginalLevel(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_origin_rank`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetOriginalRank(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_xyz_level`
---@param ... any
---@return any result
function Card:IsXyzLevel(...) end

--- 取得卡片字段 GetLeftScale。
--- C++: `scriptlib::card_get_lscale`
---@param ... any
---@return integer value
function Card:GetLeftScale() end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_origin_lscale`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetOriginalLeftScale(...) end

--- 取得卡片字段 GetRightScale。
--- C++: `scriptlib::card_get_rscale`
---@param ... any
---@return integer value
function Card:GetRightScale() end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_origin_rscale`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetOriginalRightScale(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_current_scale`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetCurrentScale(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_link_marker`
---@param ... any
---@return any result
function Card:IsLinkMarker(...) end

--- 取得此连接怪兽连接端指向的卡片集合。
--- C++: `scriptlib::card_get_linked_group`
---@return Group group
function Card:GetLinkedGroup() end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_linked_group_count`
---@param ... any
---@return integer count
function Card:GetLinkedGroupCount(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_linked_zone`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetLinkedZone(...) end

--- 取得互相连接的卡片集合。
--- C++: `scriptlib::card_get_mutual_linked_group`
---@return Group group
function Card:GetMutualLinkedGroup() end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_mutual_linked_group_count`
---@param ... any
---@return integer count
function Card:GetMutualLinkedGroupCount(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_mutual_linked_zone`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetMutualLinkedZone(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_link_state`
---@param ... any
---@return any result
function Card:IsLinkState(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_extra_link_state`
---@param ... any
---@return any result
function Card:IsExtraLinkState(...) end

--- 取得同列相关卡片集合。
--- C++: `scriptlib::card_get_column_group`
---@param left? integer
---@param right? integer
---@return Group group
function Card:GetColumnGroup(left, right) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_column_group_count`
---@param ... any
---@return integer count
function Card:GetColumnGroupCount(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_column_zone`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetColumnZone(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_all_column`
---@param ... any
---@return any result
function Card:IsAllColumn(...) end

--- 取得卡片字段 GetAttribute。
--- C++: `scriptlib::card_get_attribute`
---@param ... any
---@return integer value
function Card:GetAttribute() end

--- 取得卡片字段 GetOriginalAttribute。
--- C++: `scriptlib::card_get_origin_attribute`
---@param ... any
---@return integer value
function Card:GetOriginalAttribute() end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_fusion_attribute`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetFusionAttribute(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_link_attribute`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetLinkAttribute(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_attribute_in_grave`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetAttributeInGrave(...) end

--- 取得卡片字段 GetRace。
--- C++: `scriptlib::card_get_race`
---@param ... any
---@return integer value
function Card:GetRace() end

--- 取得卡片字段 GetOriginalRace。
--- C++: `scriptlib::card_get_origin_race`
---@param ... any
---@return integer value
function Card:GetOriginalRace() end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_link_race`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetLinkRace(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_race_in_grave`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetRaceInGrave(...) end

--- 取得卡片字段 GetAttack。
--- C++: `scriptlib::card_get_attack`
---@param ... any
---@return integer value
function Card:GetAttack() end

--- 取得卡片字段 GetBaseAttack。
--- C++: `scriptlib::card_get_origin_attack`
---@param ... any
---@return integer value
function Card:GetBaseAttack() end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_text_attack`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetTextAttack(...) end

--- 取得卡片字段 GetDefense。
--- C++: `scriptlib::card_get_defense`
---@param ... any
---@return integer value
function Card:GetDefense() end

--- 取得卡片字段 GetBaseDefense。
--- C++: `scriptlib::card_get_origin_defense`
---@param ... any
---@return integer value
function Card:GetBaseDefense() end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_text_defense`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetTextDefense(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_previous_code_onfield`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetPreviousCodeOnField(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_previous_type_onfield`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetPreviousTypeOnField(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_previous_level_onfield`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetPreviousLevelOnField(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_previous_rank_onfield`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetPreviousRankOnField(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_previous_attribute_onfield`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetPreviousAttributeOnField(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_previous_race_onfield`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetPreviousRaceOnField(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_previous_attack_onfield`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetPreviousAttackOnField(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_previous_defense_onfield`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetPreviousDefenseOnField(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_previous_overlay_count_onfield`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetPreviousOverlayCountOnField(...) end

--- 取得卡片字段 GetOwner。
--- C++: `scriptlib::card_get_owner`
---@param ... any
---@return integer value
function Card:GetOwner() end

--- 取得卡片字段 GetControler。
--- C++: `scriptlib::card_get_controler`
---@param ... any
---@return integer value
function Card:GetControler() end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_previous_controler`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetPreviousControler(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_set_reason`
---@param ... any
---@return any result
function Card:SetReason(...) end

--- 取得卡片字段 GetReason。
--- C++: `scriptlib::card_get_reason`
---@param ... any
---@return integer value
function Card:GetReason() end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_reason_card`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetReasonCard(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_reason_player`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetReasonPlayer(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_reason_effect`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetReasonEffect(...) end

--- 取得卡片字段 GetPosition。
--- C++: `scriptlib::card_get_position`
---@param ... any
---@return integer value
function Card:GetPosition() end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_previous_position`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetPreviousPosition(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_battle_position`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetBattlePosition(...) end

--- 取得卡片字段 GetLocation。
--- C++: `scriptlib::card_get_location`
---@param ... any
---@return integer value
function Card:GetLocation() end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_previous_location`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetPreviousLocation(...) end

--- 取得卡片字段 GetSequence。
--- C++: `scriptlib::card_get_sequence`
---@param ... any
---@return integer value
function Card:GetSequence() end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_previous_sequence`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetPreviousSequence(...) end

--- 取得卡片字段 GetSummonType。
--- C++: `scriptlib::card_get_summon_type`
---@param ... any
---@return integer value
function Card:GetSummonType() end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_summon_location`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetSummonLocation(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_summon_player`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetSummonPlayer(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_special_summon_info`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetSpecialSummonInfo(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_destination`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetDestination(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_leave_field_dest`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetLeaveFieldDest(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_turnid`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetTurnID(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_fieldid`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetFieldID(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_fieldidr`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetRealFieldID(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_origin_code_rule`
---@param ... any
---@return any result
function Card:IsOriginalCodeRule(...) end

--- 检查卡片条件 IsCode。
--- C++: `scriptlib::card_is_code`
---@param ... any
---@return boolean result
function Card:IsCode(...) end

--- 检查卡片条件 IsType。
--- C++: `scriptlib::card_is_type`
---@param ... any
---@return boolean result
function Card:IsType(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_all_types`
---@param ... any
---@return any result
function Card:IsAllTypes(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_fusion_type`
---@param ... any
---@return any result
function Card:IsFusionType(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_synchro_type`
---@param ... any
---@return any result
function Card:IsSynchroType(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_xyz_type`
---@param ... any
---@return any result
function Card:IsXyzType(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_link_type`
---@param ... any
---@return any result
function Card:IsLinkType(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_level`
---@param ... any
---@return any result
function Card:IsLevel(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_rank`
---@param ... any
---@return any result
function Card:IsRank(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_link`
---@param ... any
---@return any result
function Card:IsLink(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_attack`
---@param ... any
---@return any result
function Card:IsAttack(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_defense`
---@param ... any
---@return any result
function Card:IsDefense(...) end

--- 检查卡片条件 IsRace。
--- C++: `scriptlib::card_is_race`
---@param ... any
---@return boolean result
function Card:IsRace(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_link_race`
---@param ... any
---@return any result
function Card:IsLinkRace(...) end

--- 检查卡片条件 IsAttribute。
--- C++: `scriptlib::card_is_attribute`
---@param ... any
---@return boolean result
function Card:IsAttribute(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_fusion_attribute`
---@param ... any
---@return any result
function Card:IsFusionAttribute(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_link_attribute`
---@param ... any
---@return any result
function Card:IsLinkAttribute(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_non_attribute`
---@param ... any
---@return any result
function Card:IsNonAttribute(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_extra_deck_monster`
---@param ... any
---@return any result
function Card:IsExtraDeckMonster(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_reason`
---@param ... any
---@return any result
function Card:IsReason(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_all_reasons`
---@param ... any
---@return any result
function Card:IsAllReasons(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_summon_type`
---@param ... any
---@return any result
function Card:IsSummonType(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_summon_location`
---@param ... any
---@return any result
function Card:IsSummonLocation(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_summon_player`
---@param ... any
---@return any result
function Card:IsSummonPlayer(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_status`
---@param ... any
---@return any result
function Card:IsStatus(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_not_tuner`
---@param ... any
---@return any result
function Card:IsNotTuner(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_tuner`
---@param ... any
---@return any result
function Card:IsTuner(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_original_effect_property`
---@param ... any
---@return any result
function Card:IsOriginalEffectProperty(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_effect_property`
---@param ... any
---@return any result
function Card:IsEffectProperty(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_set_status`
---@param ... any
---@return any result
function Card:SetStatus(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_dual_state`
---@param ... any
---@return any result
function Card:IsDualState(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_enable_dual_state`
---@param ... any
---@return any result
function Card:EnableDualState(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_set_turn_counter`
---@param ... any
---@return any result
function Card:SetTurnCounter(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_turn_counter`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetTurnCounter(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_set_material`
---@param ... any
---@return any result
function Card:SetMaterial(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_material`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetMaterial(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_material_count`
---@param ... any
---@return integer count
function Card:GetMaterialCount(...) end

--- 取得装备到此卡的集合。
--- C++: `scriptlib::card_get_equip_group`
---@return Group group
function Card:GetEquipGroup() end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_equip_count`
---@param ... any
---@return integer count
function Card:GetEquipCount(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_equip_target`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetEquipTarget(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_pre_equip_target`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetPreviousEquipTarget(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_check_equip_target`
---@param ... any
---@return any result
function Card:CheckEquipTarget(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_check_union_target`
---@param ... any
---@return any result
function Card:CheckUnionTarget(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_union_count`
---@param ... any
---@return integer count
function Card:GetUnionCount(...) end

--- 取得叠放素材集合。
--- C++: `scriptlib::card_get_overlay_group`
---@return Group group
function Card:GetOverlayGroup() end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_overlay_count`
---@param ... any
---@return integer count
function Card:GetOverlayCount(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_overlay_target`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetOverlayTarget(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_check_remove_overlay_card`
---@param ... any
---@return any result
function Card:CheckRemoveOverlayCard(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_remove_overlay_card`
---@param ... any
---@return any result
function Card:RemoveOverlayCard(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_attacked_group`
---@param ... any
---@return Group group
function Card:GetAttackedGroup(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_attacked_group_count`
---@param ... any
---@return integer count
function Card:GetAttackedGroupCount(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_attacked_count`
---@param ... any
---@return integer count
function Card:GetAttackedCount(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_battled_group`
---@param ... any
---@return Group group
function Card:GetBattledGroup(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_battled_group_count`
---@param ... any
---@return integer count
function Card:GetBattledGroupCount(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_attack_announced_count`
---@param ... any
---@return integer count
function Card:GetAttackAnnouncedCount(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_direct_attacked`
---@param ... any
---@return any result
function Card:IsDirectAttacked(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_set_card_target`
---@param ... any
---@return any result
function Card:SetCardTarget(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_card_target`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetCardTarget(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_first_card_target`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetFirstCardTarget(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_card_target_count`
---@param ... any
---@return integer count
function Card:GetCardTargetCount(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_has_card_target`
---@param ... any
---@return any result
function Card:IsHasCardTarget(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_cancel_card_target`
---@param ... any
---@return any result
function Card:CancelCardTarget(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_owner_target`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetOwnerTarget(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_owner_target_count`
---@param ... any
---@return integer count
function Card:GetOwnerTargetCount(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_activate_effect`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetActivateEffect(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_check_activate_effect`
---@param ... any
---@return any result
function Card:CheckActivateEffect(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_tuner_limit`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetTunerLimit(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_hand_synchro`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetHandSynchro(...) end

--- 把效果注册到当前卡片。
--- C++: `scriptlib::card_register_effect`
---@param effect Effect
---@param forced? boolean|integer
---@param player? Player
---@return integer|Effect|nil result
function Card:RegisterEffect(effect, forced, player) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_has_effect`
---@param ... any
---@return any result
function Card:IsHasEffect(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_reset_effect`
---@param ... any
---@return any result
function Card:ResetEffect(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_effect_count`
---@param ... any
---@return integer count
function Card:GetEffectCount(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_register_flag_effect`
---@param ... any
---@return any result
function Card:RegisterFlagEffect(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_flag_effect`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetFlagEffect(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_reset_flag_effect`
---@param ... any
---@return any result
function Card:ResetFlagEffect(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_set_flag_effect_label`
---@param ... any
---@return any result
function Card:SetFlagEffectLabel(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_flag_effect_label`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetFlagEffectLabel(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_create_relation`
---@param ... any
---@return any result
function Card:CreateRelation(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_release_relation`
---@param ... any
---@return any result
function Card:ReleaseRelation(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_create_effect_relation`
---@param ... any
---@return any result
function Card:CreateEffectRelation(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_release_effect_relation`
---@param ... any
---@return any result
function Card:ReleaseEffectRelation(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_clear_effect_relation`
---@param ... any
---@return any result
function Card:ClearEffectRelation(...) end

--- 检查卡片条件 IsRelateToEffect。
--- C++: `scriptlib::card_is_relate_to_effect`
---@param ... any
---@return boolean result
function Card:IsRelateToEffect(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_relate_to_chain`
---@param ... any
---@return any result
function Card:IsRelateToChain(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_relate_to_card`
---@param ... any
---@return any result
function Card:IsRelateToCard(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_relate_to_battle`
---@param ... any
---@return any result
function Card:IsRelateToBattle(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_copy_effect`
---@param ... any
---@return any result
function Card:CopyEffect(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_replace_effect`
---@param ... any
---@return any result
function Card:ReplaceEffect(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_enable_revive_limit`
---@param ... any
---@return any result
function Card:EnableReviveLimit(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_complete_procedure`
---@param ... any
---@return any result
function Card:CompleteProcedure(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_disabled`
---@param ... any
---@return any result
function Card:IsDisabled(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_destructable`
---@param ... any
---@return any result
function Card:IsDestructable(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_summonable`
---@param ... any
---@return any result
function Card:IsSummonableCard(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_special_summonable_card`
---@param ... any
---@return any result
function Card:IsSpecialSummonableCard(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_fusion_summonable_card`
---@param ... any
---@return any result
function Card:IsFusionSummonableCard(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_special_summonable`
---@param ... any
---@return any result
function Card:IsSpecialSummonable(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_synchro_summonable`
---@param ... any
---@return any result
function Card:IsSynchroSummonable(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_xyz_summonable`
---@param ... any
---@return any result
function Card:IsXyzSummonable(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_link_summonable`
---@param ... any
---@return any result
function Card:IsLinkSummonable(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_can_be_summoned`
---@param ... any
---@return any result
function Card:IsSummonable(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_msetable`
---@param ... any
---@return any result
function Card:IsMSetable(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_ssetable`
---@param ... any
---@return any result
function Card:IsSSetable(...) end

--- 检查卡片条件 IsCanBeSpecialSummoned。
--- C++: `scriptlib::card_is_can_be_special_summoned`
---@param ... any
---@return boolean result
function Card:IsCanBeSpecialSummoned(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_can_be_placed_on_field`
---@param ... any
---@return any result
function Card:IsCanBePlacedOnField(...) end

--- 检查卡片条件 IsAbleToHand。
--- C++: `scriptlib::card_is_able_to_hand`
---@param ... any
---@return boolean result
function Card:IsAbleToHand(...) end

--- 检查卡片条件 IsAbleToDeck。
--- C++: `scriptlib::card_is_able_to_deck`
---@param ... any
---@return boolean result
function Card:IsAbleToDeck(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_able_to_extra`
---@param ... any
---@return any result
function Card:IsAbleToExtra(...) end

--- 检查卡片条件 IsAbleToGrave。
--- C++: `scriptlib::card_is_able_to_grave`
---@param ... any
---@return boolean result
function Card:IsAbleToGrave(...) end

--- 检查卡片条件 IsAbleToRemove。
--- C++: `scriptlib::card_is_able_to_remove`
---@param ... any
---@return boolean result
function Card:IsAbleToRemove(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_able_to_hand_as_cost`
---@param ... any
---@return any result
function Card:IsAbleToHandAsCost(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_able_to_deck_as_cost`
---@param ... any
---@return any result
function Card:IsAbleToDeckAsCost(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_able_to_extra_as_cost`
---@param ... any
---@return any result
function Card:IsAbleToExtraAsCost(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_able_to_deck_or_extra_as_cost`
---@param ... any
---@return any result
function Card:IsAbleToDeckOrExtraAsCost(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_able_to_grave_as_cost`
---@param ... any
---@return any result
function Card:IsAbleToGraveAsCost(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_able_to_remove_as_cost`
---@param ... any
---@return any result
function Card:IsAbleToRemoveAsCost(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_releasable`
---@param ... any
---@return any result
function Card:IsReleasable(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_releasable_by_effect`
---@param ... any
---@return any result
function Card:IsReleasableByEffect(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_discardable`
---@param ... any
---@return any result
function Card:IsDiscardable(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_attackable`
---@param ... any
---@return any result
function Card:IsAttackable(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_chain_attackable`
---@param ... any
---@return any result
function Card:IsChainAttackable(...) end

--- 检查卡片条件 IsFaceup。
--- C++: `scriptlib::card_is_faceup`
---@param ... any
---@return boolean result
function Card:IsFaceup(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_faceup_ex`
---@param ... any
---@return any result
function Card:IsFaceupEx(...) end

--- 检查卡片条件 IsAttackPos。
--- C++: `scriptlib::card_is_attack_pos`
---@param ... any
---@return boolean result
function Card:IsAttackPos(...) end

--- 检查卡片条件 IsFacedown。
--- C++: `scriptlib::card_is_facedown`
---@param ... any
---@return boolean result
function Card:IsFacedown(...) end

--- 检查卡片条件 IsDefensePos。
--- C++: `scriptlib::card_is_defense_pos`
---@param ... any
---@return boolean result
function Card:IsDefensePos(...) end

--- 检查卡片条件 IsPosition。
--- C++: `scriptlib::card_is_position`
---@param ... any
---@return boolean result
function Card:IsPosition(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_pre_position`
---@param ... any
---@return any result
function Card:IsPreviousPosition(...) end

--- 检查卡片条件 IsControler。
--- C++: `scriptlib::card_is_controler`
---@param ... any
---@return boolean result
function Card:IsControler(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_pre_controler`
---@param ... any
---@return any result
function Card:IsPreviousControler(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_onfield`
---@param ... any
---@return any result
function Card:IsOnField(...) end

--- 检查卡片条件 IsLocation。
--- C++: `scriptlib::card_is_location`
---@param ... any
---@return boolean result
function Card:IsLocation(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_pre_location`
---@param ... any
---@return any result
function Card:IsPreviousLocation(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_level_below`
---@param ... any
---@return any result
function Card:IsLevelBelow(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_level_above`
---@param ... any
---@return any result
function Card:IsLevelAbove(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_rank_below`
---@param ... any
---@return any result
function Card:IsRankBelow(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_rank_above`
---@param ... any
---@return any result
function Card:IsRankAbove(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_link_below`
---@param ... any
---@return any result
function Card:IsLinkBelow(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_link_above`
---@param ... any
---@return any result
function Card:IsLinkAbove(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_attack_below`
---@param ... any
---@return any result
function Card:IsAttackBelow(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_attack_above`
---@param ... any
---@return any result
function Card:IsAttackAbove(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_defense_below`
---@param ... any
---@return any result
function Card:IsDefenseBelow(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_defense_above`
---@param ... any
---@return any result
function Card:IsDefenseAbove(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_has_level`
---@param ... any
---@return any result
function Card:IsHasLevel(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_has_defense`
---@param ... any
---@return any result
function Card:IsHasDefense(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_public`
---@param ... any
---@return any result
function Card:IsPublic(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_forbidden`
---@param ... any
---@return any result
function Card:IsForbidden(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_able_to_change_controler`
---@param ... any
---@return any result
function Card:IsAbleToChangeControler(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_controler_can_be_changed`
---@param ... any
---@return any result
function Card:IsControlerCanBeChanged(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_add_counter`
---@param ... any
---@return any result
function Card:AddCounter(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_remove_counter`
---@param ... any
---@return any result
function Card:RemoveCounter(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_counter`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetCounter(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_enable_counter_permit`
---@param ... any
---@return any result
function Card:EnableCounterPermit(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_set_counter_limit`
---@param ... any
---@return any result
function Card:SetCounterLimit(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_can_change_position`
---@param ... any
---@return any result
function Card:IsCanChangePosition(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_can_turn_set`
---@param ... any
---@return any result
function Card:IsCanTurnSet(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_can_add_counter`
---@param ... any
---@return any result
function Card:IsCanAddCounter(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_can_remove_counter`
---@param ... any
---@return any result
function Card:IsCanRemoveCounter(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_can_have_counter`
---@param ... any
---@return any result
function Card:IsCanHaveCounter(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_can_overlay`
---@param ... any
---@return any result
function Card:IsCanOverlay(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_can_be_fusion_material`
---@param ... any
---@return any result
function Card:IsCanBeFusionMaterial(...) end

--- 检查卡片条件 IsCanBeSynchroMaterial。
--- C++: `scriptlib::card_is_can_be_synchro_material`
---@param ... any
---@return boolean result
function Card:IsCanBeSynchroMaterial(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_can_be_ritual_material`
---@param ... any
---@return any result
function Card:IsCanBeRitualMaterial(...) end

--- 检查卡片条件 IsCanBeXyzMaterial。
--- C++: `scriptlib::card_is_can_be_xyz_material`
---@param ... any
---@return boolean result
function Card:IsCanBeXyzMaterial(...) end

--- 检查卡片条件 IsCanBeLinkMaterial。
--- C++: `scriptlib::card_is_can_be_link_material`
---@param ... any
---@return boolean result
function Card:IsCanBeLinkMaterial(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_check_fusion_material`
---@param ... any
---@return any result
function Card:CheckFusionMaterial(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_check_fusion_substitute`
---@param ... any
---@return any result
function Card:CheckFusionSubstitute(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_immune_to_effect`
---@param ... any
---@return any result
function Card:IsImmuneToEffect(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_can_be_disabled_by_effect`
---@param ... any
---@return any result
function Card:IsCanBeDisabledByEffect(...) end

--- 检查卡片条件 IsCanBeEffectTarget。
--- C++: `scriptlib::card_is_can_be_effect_target`
---@param ... any
---@return boolean result
function Card:IsCanBeEffectTarget(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_is_can_be_battle_target`
---@param ... any
---@return any result
function Card:IsCanBeBattleTarget(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_add_monster_attribute`
---@param ... any
---@return any result
function Card:AddMonsterAttribute(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_cancel_to_grave`
---@param ... any
---@return any result
function Card:CancelToGrave(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_tribute_requirement`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetTributeRequirement(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_battle_target`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetBattleTarget(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_get_attackable_target`
---@param ... any
---@return integer|Card|Effect|Group|nil value
function Card:GetAttackableTarget(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_set_hint`
---@param ... any
---@return any result
function Card:SetHint(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_reverse_in_deck`
---@param ... any
---@return any result
function Card:ReverseInDeck(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_set_unique_onfield`
---@param ... any
---@return any result
function Card:SetUniqueOnField(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_check_unique_onfield`
---@param ... any
---@return any result
function Card:CheckUniqueOnField(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_reset_negate_effect`
---@param ... any
---@return any result
function Card:ResetNegateEffect(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_assume_prop`
---@param ... any
---@return any result
function Card:AssumeProperty(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::card_set_spsummon_once`
---@param ... any
---@return any result
function Card:SetSPSummonOnce(...) end

-------------------------------------------------------------------------------
-- Effect API (55 functions)
-------------------------------------------------------------------------------

--- 创建绑定到处理者卡片的效果对象（Effect object）。
--- C++: `scriptlib::effect_new`
---@param handler Card 效果处理者卡片。
---@return Effect effect 新效果对象。
function Effect.CreateEffect(handler) end

--- 创建全局效果对象（global Effect）。
--- C++: `scriptlib::effect_newex`
---@return Effect effect 全局效果对象。
function Effect.GlobalEffect() end

--- 克隆当前效果对象。
--- C++: `scriptlib::effect_clone`
---@return Effect effect 克隆出的效果对象。
function Effect:Clone() end

--- 重置当前效果对象。
--- C++: `scriptlib::effect_reset`
---@return nil
function Effect:Reset() end

--- 取得效果在 duel 内部的 field id。
--- C++: `scriptlib::effect_get_field_id`
---@return integer fieldId
function Effect:GetFieldID() end

--- 设置效果描述文本 id 或描述值。
--- C++: `scriptlib::effect_set_description`
---@param description integer|string 描述 id 或文本。
---@return nil
function Effect:SetDescription(description) end

--- 设置效果代码（EffectCode）。
--- C++: `scriptlib::effect_set_code`
---@param code EffectCode
---@return nil
function Effect:SetCode(code) end

--- 设置效果适用位置范围。
--- C++: `scriptlib::effect_set_range`
---@param range Location
---@return nil
function Effect:SetRange(range) end

--- 设置双方目标范围。
--- C++: `scriptlib::effect_set_target_range`
---@param selfRange Location
---@param opponentRange Location
---@return nil
function Effect:SetTargetRange(selfRange, opponentRange) end

--- 按指定玩家视角设置绝对范围。
--- C++: `scriptlib::effect_set_absolute_range`
---@param player Player
---@param selfRange Location
---@param opponentRange Location
---@return nil
function Effect:SetAbsoluteRange(player, selfRange, opponentRange) end

--- 设置每回合/每局次数限制。
--- C++: `scriptlib::effect_set_count_limit`
---@param count integer
---@param code? integer
---@param flags? integer
---@return nil
function Effect:SetCountLimit(count, code, flags) end

--- 设置 reset 条件和次数。
--- C++: `scriptlib::effect_set_reset`
---@param reset Reset
---@param count? integer
---@return nil
function Effect:SetReset(reset, count) end

--- 设置效果类型（EffectType）。
--- C++: `scriptlib::effect_set_type`
---@param type EffectType
---@return nil
function Effect:SetType(type) end

--- 设置效果属性 bitmask。
--- C++: `scriptlib::effect_set_property`
---@param property EffectProperty
---@return nil
function Effect:SetProperty(property) end

--- 保存一个或多个整型 label。
--- C++: `scriptlib::effect_set_label`
---@param ... integer
---@return nil
function Effect:SetLabel(...) end

--- 保存关联对象 label object。
--- C++: `scriptlib::effect_set_label_object`
---@param object Card|Effect|Group|nil
---@return nil
function Effect:SetLabelObject(object) end

--- 设置效果分类（EffectCategory）。
--- C++: `scriptlib::effect_set_category`
---@param category EffectCategory
---@return nil
function Effect:SetCategory(category) end

--- 设置提示时点（hint timing）。
--- C++: `scriptlib::effect_set_hint_timing`
---@param selfTiming integer
---@param opponentTiming? integer
---@return nil
function Effect:SetHintTiming(selfTiming, opponentTiming) end

--- 设置发动/适用条件回调。
--- C++: `scriptlib::effect_set_condition`
---@param condition ConditionFunction
---@return nil
function Effect:SetCondition(condition) end

--- 设置目标选择回调。
--- C++: `scriptlib::effect_set_target`
---@param target TargetFunction
---@return nil
function Effect:SetTarget(target) end

--- 设置 cost 支付回调。
--- C++: `scriptlib::effect_set_cost`
---@param cost CostFunction
---@return nil
function Effect:SetCost(cost) end

--- 设置固定值或动态值回调。
--- C++: `scriptlib::effect_set_value`
---@param value integer|boolean|ValueFunction
---@return nil
function Effect:SetValue(value) end

--- 设置效果处理回调。
--- C++: `scriptlib::effect_set_operation`
---@param operation OperationFunction
---@return nil
function Effect:SetOperation(operation) end

--- 设置效果拥有玩家。
--- C++: `scriptlib::effect_set_owner_player`
---@param player Player
---@return nil
function Effect:SetOwnerPlayer(player) end

--- 取得效果字段 GetDescription。
--- C++: `scriptlib::effect_get_description`
---@return integer value
function Effect:GetDescription() end

--- 取得效果字段 GetCode。
--- C++: `scriptlib::effect_get_code`
---@return integer value
function Effect:GetCode() end

--- 取得效果字段 GetType。
--- C++: `scriptlib::effect_get_type`
---@return integer value
function Effect:GetType() end

--- 取得效果字段 GetProperty。
--- C++: `scriptlib::effect_get_property`
---@return integer value
function Effect:GetProperty() end

--- 取得效果字段 GetLabel。
--- C++: `scriptlib::effect_get_label`
---@return integer value
function Effect:GetLabel() end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::effect_get_label_object`
---@param ... any
---@return Card|Effect|Group|nil object
function Effect:GetLabelObject(...) end

--- 取得效果字段 GetCategory。
--- C++: `scriptlib::effect_get_category`
---@return integer value
function Effect:GetCategory() end

--- 取得效果字段 GetRange。
--- C++: `scriptlib::effect_get_range`
---@return integer value
function Effect:GetRange() end

--- 取得效果关联卡片 GetOwner。
--- C++: `scriptlib::effect_get_owner`
---@return Card|nil card
function Effect:GetOwner() end

--- 取得效果关联卡片 GetHandler。
--- C++: `scriptlib::effect_get_handler`
---@return Card|nil card
function Effect:GetHandler() end

--- 取得 Lua 回调 GetCondition。
--- C++: `scriptlib::effect_get_condition`
---@return function|integer|nil callback
function Effect:GetCondition() end

--- 取得 Lua 回调 GetTarget。
--- C++: `scriptlib::effect_get_target`
---@return function|integer|nil callback
function Effect:GetTarget() end

--- 取得 Lua 回调 GetCost。
--- C++: `scriptlib::effect_get_cost`
---@return function|integer|nil callback
function Effect:GetCost() end

--- 取得 Lua 回调 GetValue。
--- C++: `scriptlib::effect_get_value`
---@return function|integer|nil callback
function Effect:GetValue() end

--- 取得 Lua 回调 GetOperation。
--- C++: `scriptlib::effect_get_operation`
---@return function|integer|nil callback
function Effect:GetOperation() end

--- 取得效果字段 GetActiveType。
--- C++: `scriptlib::effect_get_active_type`
---@return integer value
function Effect:GetActiveType() end

--- 检查效果状态 IsActiveType。
--- C++: `scriptlib::effect_is_active_type`
---@param ... any
---@return boolean result
function Effect:IsActiveType(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::effect_get_owner_player`
---@param ... any
---@return integer|function|Card|Effect|Group|nil value
function Effect:GetOwnerPlayer(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::effect_get_handler_player`
---@param ... any
---@return integer|function|Card|Effect|Group|nil value
function Effect:GetHandlerPlayer(...) end

--- 检查效果状态 IsHasProperty。
--- C++: `scriptlib::effect_is_has_property`
---@param ... any
---@return boolean result
function Effect:IsHasProperty(...) end

--- 检查效果状态 IsHasCategory。
--- C++: `scriptlib::effect_is_has_category`
---@param ... any
---@return boolean result
function Effect:IsHasCategory(...) end

--- 检查效果状态 IsHasType。
--- C++: `scriptlib::effect_is_has_type`
---@param ... any
---@return boolean result
function Effect:IsHasType(...) end

--- 检查效果状态 IsHasRange。
--- C++: `scriptlib::effect_is_has_range`
---@param ... any
---@return boolean result
function Effect:IsHasRange(...) end

--- 检查效果状态 IsActivatable。
--- C++: `scriptlib::effect_is_activatable`
---@param ... any
---@return boolean result
function Effect:IsActivatable(...) end

--- 检查效果状态 IsActivated。
--- C++: `scriptlib::effect_is_activated`
---@param ... any
---@return boolean result
function Effect:IsActivated(...) end

--- 检查效果状态 IsCostChecked。
--- C++: `scriptlib::effect_is_cost_checked`
---@param ... any
---@return boolean result
function Effect:IsCostChecked(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::effect_set_cost_check`
---@param ... any
---@return nil
function Effect:SetCostCheck(...) end

--- 取得效果字段 GetActivateLocation。
--- C++: `scriptlib::effect_get_activate_location`
---@return integer value
function Effect:GetActivateLocation() end

--- 取得效果字段 GetActivateSequence。
--- C++: `scriptlib::effect_get_activate_sequence`
---@return integer value
function Effect:GetActivateSequence() end

--- 检查效果状态 CheckCountLimit。
--- C++: `scriptlib::effect_check_count_limit`
---@param ... any
---@return boolean result
function Effect:CheckCountLimit(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::effect_use_count_limit`
---@param ... any
---@return nil
function Effect:UseCountLimit(...) end

-------------------------------------------------------------------------------
-- Group API (40 functions)
-------------------------------------------------------------------------------

--- 创建空卡片集合。
--- C++: `scriptlib::group_new`
---@return Group group
function Group.CreateGroup() end

--- 保持集合跨调用存活。
--- C++: `scriptlib::group_keep_alive`
---@return nil
function Group:KeepAlive() end

--- 释放由 KeepAlive 保持的集合。
--- C++: `scriptlib::group_delete`
---@return nil
function Group:DeleteGroup() end

--- 克隆当前集合。
--- C++: `scriptlib::group_clone`
---@return Group group
function Group:Clone() end

--- 从若干卡片创建集合。
--- C++: `scriptlib::group_from_cards`
---@param ... Card
---@return Group group
function Group.FromCards(...) end

--- 清空集合。
--- C++: `scriptlib::group_clear`
---@return nil
function Group:Clear() end

--- 向集合加入卡片。
--- C++: `scriptlib::group_add_card`
---@param card Card
---@return nil
function Group:AddCard(card) end

--- 从集合移除卡片。
--- C++: `scriptlib::group_remove_card`
---@param card Card
---@return nil
function Group:RemoveCard(card) end

--- 取得集合迭代游标的下一个卡片。
--- C++: `scriptlib::group_get_next`
---@return Card|nil card
function Group:GetNext() end

--- 取得集合第一个卡片，并重置迭代游标。
--- C++: `scriptlib::group_get_first`
---@return Card|nil card
function Group:GetFirst() end

--- 取得集合内卡片数量。
--- C++: `scriptlib::group_get_count`
---@return integer count
function Group:GetCount() end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::group_get_count`
---@param ... any
---@return integer count
function Group:__len(...) end

--- 按过滤函数返回新集合。
--- C++: `scriptlib::group_filter`
---@param filter FilterFunction
---@param excluded? Card|Group|nil
---@param ... any
---@return Group group
function Group:Filter(filter, excluded, ...) end

--- 统计满足过滤函数的卡片数量。
--- C++: `scriptlib::group_filter_count`
---@param filter FilterFunction
---@param excluded? Card|Group|nil
---@param ... any
---@return integer count
function Group:FilterCount(filter, excluded, ...) end

--- 按过滤函数让玩家选择卡片。
--- C++: `scriptlib::group_filter_select`
---@param player Player
---@param filter FilterFunction
---@param min integer
---@param max integer
---@param excluded? Card|Group|nil
---@param ... any
---@return Group selected
function Group:FilterSelect(player, filter, min, max, excluded, ...) end

--- 让玩家从集合中选择卡片。
--- C++: `scriptlib::group_select`
---@param player Player
---@param min integer
---@param max integer
---@param cancelable? boolean|integer
---@return Group selected
function Group:Select(player, min, max, cancelable) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::group_select_unselect`
---@param ... any
---@return any result
function Group:SelectUnselect(...) end

--- 随机选择集合内卡片。
--- C++: `scriptlib::group_random_select`
---@param player Player
---@param count integer
---@return Group selected
function Group:RandomSelect(player, count) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::group_cancelable_select`
---@param ... any
---@return any result
function Group:CancelableSelect(...) end

--- 检查集合内是否存在满足过滤条件的指定数量卡片。
--- C++: `scriptlib::group_is_exists`
---@param filter FilterFunction
---@param count integer
---@param excluded? Card|Group|nil
---@param ... any
---@return boolean result
function Group:IsExists(filter, count, excluded, ...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::group_check_with_sum_equal`
---@param ... any
---@return any result
function Group:CheckWithSumEqual(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::group_select_with_sum_equal`
---@param ... any
---@return any result
function Group:SelectWithSumEqual(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::group_check_with_sum_greater`
---@param ... any
---@return any result
function Group:CheckWithSumGreater(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::group_select_with_sum_greater`
---@param ... any
---@return any result
function Group:SelectWithSumGreater(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::group_get_min_group`
---@param ... any
---@return any result
function Group:GetMinGroup(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::group_get_max_group`
---@param ... any
---@return any result
function Group:GetMaxGroup(...) end

--- 按求值函数计算总和。
--- C++: `scriptlib::group_get_sum`
---@param value SumValueFunction|integer
---@param ... any
---@return integer sum
function Group:GetSum(value, ...) end

--- 统计分类数量。
--- C++: `scriptlib::group_get_class_count`
---@param classFunction ClassValueFunction
---@param ... any
---@return integer count
function Group:GetClassCount(classFunction, ...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::group_remove`
---@param ... any
---@return any result
function Group:Remove(...) end

--- 合并另一个集合。
--- C++: `scriptlib::group_merge`
---@param other Group
---@return nil
function Group:Merge(other) end

--- 从当前集合减去另一个集合。
--- C++: `scriptlib::group_sub`
---@param other Group
---@return nil
function Group:Sub(other) end

--- 检查两个集合是否相同。
--- C++: `scriptlib::group_equal`
---@param other Group
---@return boolean result
function Group:Equal(other) end

--- 检查集合是否包含卡片。
--- C++: `scriptlib::group_is_contains`
---@param card Card
---@return boolean result
function Group:IsContains(card) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::group_search_card`
---@param ... any
---@return any result
function Group:SearchCard(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::group_get_bin_class_count`
---@param ... any
---@return integer count
function Group:GetBinClassCount(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::group_meta_add`
---@param ... any
---@return any result
function Group:__add(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::group_meta_add`
---@param ... any
---@return any result
function Group:__bor(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::group_meta_sub`
---@param ... any
---@return any result
function Group:__sub(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::group_meta_band`
---@param ... any
---@return any result
function Group:__band(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::group_meta_bxor`
---@param ... any
---@return any result
function Group:__bxor(...) end

-------------------------------------------------------------------------------
-- Duel API (229 functions)
-------------------------------------------------------------------------------

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_enable_global_flag`
---@param ... any
---@return any result
function Duel.EnableGlobalFlag(...) end

--- 取得玩家 LP。
--- C++: `scriptlib::duel_get_lp`
---@param player Player
---@return integer lp
function Duel.GetLP(player) end

--- 设置玩家 LP。
--- C++: `scriptlib::duel_set_lp`
---@param player Player
---@param lp integer
---@return nil
function Duel.SetLP(player, lp) end

--- 检查当前回合玩家。
--- C++: `scriptlib::duel_is_turn_player`
---@param player Player
---@return boolean result
function Duel.IsTurnPlayer(player) end

--- 取得当前回合玩家。
--- C++: `scriptlib::duel_get_turn_player`
---@return Player player
function Duel.GetTurnPlayer() end

--- 取得当前回合数。
--- C++: `scriptlib::duel_get_turn_count`
---@return integer count
function Duel.GetTurnCount() end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_draw_count`
---@param ... any
---@return integer count
function Duel.GetDrawCount(...) end

--- 注册全局/玩家效果。
--- C++: `scriptlib::duel_register_effect`
---@param effect Effect
---@param player Player
---@return integer|nil result
function Duel.RegisterEffect(effect, player) end

--- 为玩家注册 flag effect。
--- C++: `scriptlib::duel_register_flag_effect`
---@param player Player
---@param code integer
---@param reset Reset
---@param property integer
---@param count integer
---@param label? integer
---@return integer result
function Duel.RegisterFlagEffect(player, code, reset, property, count, label) end

--- 取得 flag effect 数量。
--- C++: `scriptlib::duel_get_flag_effect`
---@param player Player
---@param code integer
---@return integer count
function Duel.GetFlagEffect(player, code) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_reset_flag_effect`
---@param ... any
---@return any result
function Duel.ResetFlagEffect(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_set_flag_effect_label`
---@param ... any
---@return any result
function Duel.SetFlagEffectLabel(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_flag_effect_label`
---@param ... any
---@return any result
function Duel.GetFlagEffectLabel(...) end

--- 破坏卡片或集合。
--- C++: `scriptlib::duel_destroy`
---@param targets Card|Group
---@param reason Reason
---@return integer count
function Duel.Destroy(targets, reason) end

--- 除外卡片或集合。
--- C++: `scriptlib::duel_remove`
---@param targets Card|Group
---@param position Position
---@param reason Reason
---@return integer count
function Duel.Remove(targets, position, reason) end

--- 把卡片或集合送去墓地。
--- C++: `scriptlib::duel_sendto_grave`
---@param targets Card|Group
---@param reason Reason
---@return integer count
function Duel.SendtoGrave(targets, reason) end

--- 把卡片或集合加入手牌。
--- C++: `scriptlib::duel_sendto_hand`
---@param targets Card|Group
---@param player? Player
---@param reason Reason
---@return integer count
function Duel.SendtoHand(targets, player, reason) end

--- 把卡片或集合送回卡组。
--- C++: `scriptlib::duel_sendto_deck`
---@param targets Card|Group
---@param player? Player
---@param sequence integer
---@param reason Reason
---@return integer count
function Duel.SendtoDeck(targets, player, sequence, reason) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_sendto_extra`
---@param ... any
---@return any result
function Duel.SendtoExtraP(...) end

--- 取得最近一次操作影响的卡片集合。
--- C++: `scriptlib::duel_get_operated_group`
---@return Group group
function Duel.GetOperatedGroup() end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_summon`
---@param ... any
---@return any result
function Duel.Summon(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_special_summon_rule`
---@param ... any
---@return any result
function Duel.SpecialSummonRule(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_synchro_summon`
---@param ... any
---@return any result
function Duel.SynchroSummon(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_xyz_summon`
---@param ... any
---@return any result
function Duel.XyzSummon(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_link_summon`
---@param ... any
---@return any result
function Duel.LinkSummon(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_setm`
---@param ... any
---@return any result
function Duel.MSet(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_sets`
---@param ... any
---@return any result
function Duel.SSet(...) end

--- 创建 token 卡片。
--- C++: `scriptlib::duel_create_token`
---@param player Player
---@param code CardCode
---@return Card token
function Duel.CreateToken(player, code) end

--- 特殊召唤卡片或集合。
--- C++: `scriptlib::duel_special_summon`
---@param targets Card|Group
---@param summonType SummonType
---@param player Player
---@param targetPlayer Player
---@param nocheck boolean|integer
---@param nolimit boolean|integer
---@param position Position
---@param zone? integer
---@return integer count
function Duel.SpecialSummon(targets, summonType, player, targetPlayer, nocheck, nolimit, position, zone) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_special_summon_step`
---@param ... any
---@return any result
function Duel.SpecialSummonStep(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_special_summon_complete`
---@param ... any
---@return any result
function Duel.SpecialSummonComplete(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_is_can_add_counter`
---@param ... any
---@return any result
function Duel.IsCanAddCounter(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_remove_counter`
---@param ... any
---@return any result
function Duel.RemoveCounter(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_is_can_remove_counter`
---@param ... any
---@return any result
function Duel.IsCanRemoveCounter(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_counter`
---@param ... any
---@return any result
function Duel.GetCounter(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_change_form`
---@param ... any
---@return any result
function Duel.ChangePosition(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_release`
---@param ... any
---@return any result
function Duel.Release(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_move_to_field`
---@param ... any
---@return any result
function Duel.MoveToField(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_return_to_field`
---@param ... any
---@return any result
function Duel.ReturnToField(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_move_sequence`
---@param ... any
---@return any result
function Duel.MoveSequence(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_swap_sequence`
---@param ... any
---@return any result
function Duel.SwapSequence(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_activate_effect`
---@param ... any
---@return any result
function Duel.Activate(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_set_chain_limit`
---@param ... any
---@return any result
function Duel.SetChainLimit(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_set_chain_limit_p`
---@param ... any
---@return any result
function Duel.SetChainLimitTillChainEnd(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_chain_material`
---@param ... any
---@return any result
function Duel.GetChainMaterial(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_confirm_decktop`
---@param ... any
---@return any result
function Duel.ConfirmDecktop(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_confirm_extratop`
---@param ... any
---@return any result
function Duel.ConfirmExtratop(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_confirm_cards`
---@param ... any
---@return any result
function Duel.ConfirmCards(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_sort_decktop`
---@param ... any
---@return any result
function Duel.SortDecktop(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_check_event`
---@param ... any
---@return any result
function Duel.CheckEvent(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_raise_event`
---@param ... any
---@return any result
function Duel.RaiseEvent(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_raise_single_event`
---@param ... any
---@return any result
function Duel.RaiseSingleEvent(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_check_timing`
---@param ... any
---@return any result
function Duel.CheckTiming(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_is_environment`
---@param ... any
---@return any result
function Duel.IsEnvironment(...) end

--- 立即判定玩家胜利。
--- C++: `scriptlib::duel_win`
---@param player Player
---@param reason integer
---@return nil
function Duel.Win(player, reason) end

--- 让玩家抽卡。
--- C++: `scriptlib::duel_draw`
---@param player Player
---@param count integer
---@param reason Reason
---@return integer count
function Duel.Draw(player, count, reason) end

--- 给予玩家伤害。
--- C++: `scriptlib::duel_damage`
---@param player Player
---@param amount integer
---@param reason Reason
---@param isStep? boolean|integer
---@return integer amount
function Duel.Damage(player, amount, reason, isStep) end

--- 回复玩家 LP。
--- C++: `scriptlib::duel_recover`
---@param player Player
---@param amount integer
---@param reason Reason
---@param isStep? boolean|integer
---@return integer amount
function Duel.Recover(player, amount, reason, isStep) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_rd_complete`
---@param ... any
---@return any result
function Duel.RDComplete(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_equip`
---@param ... any
---@return any result
function Duel.Equip(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_equip_complete`
---@param ... any
---@return any result
function Duel.EquipComplete(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_control`
---@param ... any
---@return any result
function Duel.GetControl(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_swap_control`
---@param ... any
---@return any result
function Duel.SwapControl(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_check_lp_cost`
---@param ... any
---@return any result
function Duel.CheckLPCost(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_pay_lp_cost`
---@param ... any
---@return any result
function Duel.PayLPCost(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_discard_deck`
---@param ... any
---@return any result
function Duel.DiscardDeck(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_discard_hand`
---@param ... any
---@return any result
function Duel.DiscardHand(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_disable_shuffle_check`
---@param ... any
---@return any result
function Duel.DisableShuffleCheck(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_disable_self_destroy_check`
---@param ... any
---@return any result
function Duel.DisableSelfDestroyCheck(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_reveal_select_deck_sequence`
---@param ... any
---@return any result
function Duel.RevealSelectDeckSequence(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_shuffle_deck`
---@param ... any
---@return any result
function Duel.ShuffleDeck(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_shuffle_extra`
---@param ... any
---@return any result
function Duel.ShuffleExtra(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_shuffle_hand`
---@param ... any
---@return any result
function Duel.ShuffleHand(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_shuffle_setcard`
---@param ... any
---@return any result
function Duel.ShuffleSetCard(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_change_attacker`
---@param ... any
---@return any result
function Duel.ChangeAttacker(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_change_attack_target`
---@param ... any
---@return any result
function Duel.ChangeAttackTarget(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_calculate_damage`
---@param ... any
---@return any result
function Duel.CalculateDamage(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_battle_damage`
---@param ... any
---@return any result
function Duel.GetBattleDamage(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_change_battle_damage`
---@param ... any
---@return any result
function Duel.ChangeBattleDamage(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_change_target`
---@param ... any
---@return any result
function Duel.ChangeTargetCard(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_change_target_player`
---@param ... any
---@return any result
function Duel.ChangeTargetPlayer(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_change_target_param`
---@param ... any
---@return any result
function Duel.ChangeTargetParam(...) end

--- 切断效果处理时点。
--- C++: `scriptlib::duel_break_effect`
---@return nil
function Duel.BreakEffect() end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_change_effect`
---@param ... any
---@return any result
function Duel.ChangeChainOperation(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_negate_activate`
---@param ... any
---@return any result
function Duel.NegateActivation(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_negate_effect`
---@param ... any
---@return any result
function Duel.NegateEffect(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_negate_related_chain`
---@param ... any
---@return any result
function Duel.NegateRelatedChain(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_disable_summon`
---@param ... any
---@return any result
function Duel.NegateSummon(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_increase_summon_count`
---@param ... any
---@return integer count
function Duel.IncreaseSummonedCount(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_check_summon_count`
---@param ... any
---@return integer count
function Duel.CheckSummonedCount(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_location_count`
---@param ... any
---@return integer count
function Duel.GetLocationCount(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_mzone_count`
---@param ... any
---@return integer count
function Duel.GetMZoneCount(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_szone_count`
---@param ... any
---@return integer count
function Duel.GetSZoneCount(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_location_count_fromex`
---@param ... any
---@return any result
function Duel.GetLocationCountFromEx(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_usable_mzone_count`
---@param ... any
---@return integer count
function Duel.GetUsableMZoneCount(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_linked_group`
---@param ... any
---@return Group group
function Duel.GetLinkedGroup(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_linked_group_count`
---@param ... any
---@return integer count
function Duel.GetLinkedGroupCount(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_linked_zone`
---@param ... any
---@return any result
function Duel.GetLinkedZone(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_field_card`
---@param ... any
---@return Card|Group|nil card
function Duel.GetFieldCard(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_check_location`
---@param ... any
---@return any result
function Duel.CheckLocation(...) end

--- 取得当前连锁序号。
--- C++: `scriptlib::duel_get_current_chain`
---@return integer chainIndex
function Duel.GetCurrentChain() end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_ready_chain`
---@param ... any
---@return any result
function Duel.GetReadyChain(...) end

--- 取得连锁信息；返回值随 ChainInfo 参数变化。
--- C++: `scriptlib::duel_get_chain_info`
---@param chainIndex integer
---@param ... ChainInfo
---@return any ...
function Duel.GetChainInfo(chainIndex, ...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_chain_event`
---@param ... any
---@return any result
function Duel.GetChainEvent(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_first_target`
---@param ... any
---@return any result
function Duel.GetFirstTarget(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_targets_relate_to_chain`
---@param ... any
---@return any result
function Duel.GetTargetsRelateToChain(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_is_phase`
---@param ... any
---@return any result
function Duel.IsPhase(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_is_main_phase`
---@param ... any
---@return any result
function Duel.IsMainPhase(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_is_battle_phase`
---@param ... any
---@return any result
function Duel.IsBattlePhase(...) end

--- 取得当前阶段。
--- C++: `scriptlib::duel_get_current_phase`
---@return Phase phase
function Duel.GetCurrentPhase() end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_skip_phase`
---@param ... any
---@return any result
function Duel.SkipPhase(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_is_damage_calculated`
---@param ... any
---@return any result
function Duel.IsDamageCalculated(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_attacker`
---@param ... any
---@return any result
function Duel.GetAttacker(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_attack_target`
---@param ... any
---@return any result
function Duel.GetAttackTarget(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_battle_monster`
---@param ... any
---@return any result
function Duel.GetBattleMonster(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_disable_attack`
---@param ... any
---@return any result
function Duel.NegateAttack(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_chain_attack`
---@param ... any
---@return any result
function Duel.ChainAttack(...) end

--- 重新调整场面合法性。
--- C++: `scriptlib::duel_readjust`
---@return nil
function Duel.Readjust() end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_adjust_instantly`
---@param ... any
---@return any result
function Duel.AdjustInstantly(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_adjust_all`
---@param ... any
---@return any result
function Duel.AdjustAll(...) end

--- 取得指定玩家双方位置的卡片集合。
--- C++: `scriptlib::duel_get_field_group`
---@param player Player
---@param selfLocation Location
---@param opponentLocation Location
---@return Group group
function Duel.GetFieldGroup(player, selfLocation, opponentLocation) end

--- 取得指定玩家双方位置的卡片数量。
--- C++: `scriptlib::duel_get_field_group_count`
---@param player Player
---@param selfLocation Location
---@param opponentLocation Location
---@return integer count
function Duel.GetFieldGroupCount(player, selfLocation, opponentLocation) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_decktop_group`
---@param ... any
---@return Group group
function Duel.GetDecktopGroup(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_extratop_group`
---@param ... any
---@return Group group
function Duel.GetExtraTopGroup(...) end

--- 取得满足过滤条件的卡片集合。
--- C++: `scriptlib::duel_get_matching_group`
---@param filter FilterFunction
---@param player Player
---@param selfLocation Location
---@param opponentLocation Location
---@param excluded? Card|Group|nil
---@param ... any
---@return Group group
function Duel.GetMatchingGroup(filter, player, selfLocation, opponentLocation, excluded, ...) end

--- 统计满足过滤条件的卡片数量。
--- C++: `scriptlib::duel_get_matching_count`
---@param filter FilterFunction
---@param player Player
---@param selfLocation Location
---@param opponentLocation Location
---@param excluded? Card|Group|nil
---@param ... any
---@return integer count
function Duel.GetMatchingGroupCount(filter, player, selfLocation, opponentLocation, excluded, ...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_first_matching_card`
---@param ... any
---@return Card|Group|nil card
function Duel.GetFirstMatchingCard(...) end

--- 检查是否存在满足过滤条件的卡片。
--- C++: `scriptlib::duel_is_existing_matching_card`
---@param filter FilterFunction
---@param player Player
---@param selfLocation Location
---@param opponentLocation Location
---@param count integer
---@param excluded? Card|Group|nil
---@param ... any
---@return boolean result
function Duel.IsExistingMatchingCard(filter, player, selfLocation, opponentLocation, count, excluded, ...) end

--- 让玩家选择满足过滤条件的卡片。
--- C++: `scriptlib::duel_select_matching_cards`
---@param player Player
---@param filter FilterFunction
---@param selectPlayer Player
---@param selfLocation Location
---@param opponentLocation Location
---@param min integer
---@param max integer
---@param excluded? Card|Group|nil
---@param ... any
---@return Group selected
function Duel.SelectMatchingCard(player, filter, selectPlayer, selfLocation, opponentLocation, min, max, excluded, ...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_release_group`
---@param ... any
---@return Group group
function Duel.GetReleaseGroup(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_release_group_count`
---@param ... any
---@return integer count
function Duel.GetReleaseGroupCount(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_check_release_group`
---@param ... any
---@return any result
function Duel.CheckReleaseGroup(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_select_release_group`
---@param ... any
---@return Group|integer|boolean result
function Duel.SelectReleaseGroup(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_check_release_group_ex`
---@param ... any
---@return any result
function Duel.CheckReleaseGroupEx(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_select_release_group_ex`
---@param ... any
---@return Group|integer|boolean result
function Duel.SelectReleaseGroupEx(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_tribute_group`
---@param ... any
---@return Group group
function Duel.GetTributeGroup(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_tribute_count`
---@param ... any
---@return integer count
function Duel.GetTributeCount(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_check_tribute`
---@param ... any
---@return any result
function Duel.CheckTribute(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_select_tribute`
---@param ... any
---@return any result
function Duel.SelectTribute(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_target_count`
---@param ... any
---@return integer count
function Duel.GetTargetCount(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_is_existing_target`
---@param ... any
---@return any result
function Duel.IsExistingTarget(...) end

--- 让玩家选择效果目标。
--- C++: `scriptlib::duel_select_target`
---@param player Player
---@param filter FilterFunction
---@param selectPlayer Player
---@param selfLocation Location
---@param opponentLocation Location
---@param min integer
---@param max integer
---@param excluded? Card|Group|nil
---@param ... any
---@return Group selected
function Duel.SelectTarget(player, filter, selectPlayer, selfLocation, opponentLocation, min, max, excluded, ...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_must_material`
---@param ... any
---@return any result
function Duel.GetMustMaterial(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_check_must_material`
---@param ... any
---@return any result
function Duel.CheckMustMaterial(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_select_fusion_material`
---@param ... any
---@return any result
function Duel.SelectFusionMaterial(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_set_fusion_material`
---@param ... any
---@return any result
function Duel.SetFusionMaterial(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_set_synchro_material`
---@param ... any
---@return any result
function Duel.SetSynchroMaterial(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_synchro_material`
---@param ... any
---@return any result
function Duel.GetSynchroMaterial(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_select_synchro_material`
---@param ... any
---@return any result
function Duel.SelectSynchroMaterial(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_check_synchro_material`
---@param ... any
---@return any result
function Duel.CheckSynchroMaterial(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_select_tuner_material`
---@param ... any
---@return any result
function Duel.SelectTunerMaterial(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_check_tuner_material`
---@param ... any
---@return any result
function Duel.CheckTunerMaterial(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_ritual_material`
---@param ... any
---@return any result
function Duel.GetRitualMaterial(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_ritual_material_ex`
---@param ... any
---@return any result
function Duel.GetRitualMaterialEx(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_release_ritual_material`
---@param ... any
---@return any result
function Duel.ReleaseRitualMaterial(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_fusion_material`
---@param ... any
---@return any result
function Duel.GetFusionMaterial(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_is_summon_cancelable`
---@param ... any
---@return any result
function Duel.IsSummonCancelable(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_set_must_select_cards`
---@param ... any
---@return any result
function Duel.SetSelectedCard(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_grab_must_select_cards`
---@param ... any
---@return any result
function Duel.GrabSelectedCard(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_set_target_card`
---@param ... any
---@return any result
function Duel.SetTargetCard(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_clear_target_card`
---@param ... any
---@return any result
function Duel.ClearTargetCard(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_set_target_player`
---@param ... any
---@return any result
function Duel.SetTargetPlayer(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_set_target_param`
---@param ... any
---@return any result
function Duel.SetTargetParam(...) end

--- 设置当前连锁操作信息。
--- C++: `scriptlib::duel_set_operation_info`
---@param chainIndex integer
---@param category EffectCategory
---@param targets Card|Group|nil
---@param count integer
---@param player Player
---@param value integer
---@return nil
function Duel.SetOperationInfo(chainIndex, category, targets, count, player, value) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_operation_info`
---@param ... any
---@return any result
function Duel.GetOperationInfo(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_operation_count`
---@param ... any
---@return integer count
function Duel.GetOperationCount(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_clear_operation_info`
---@param ... any
---@return any result
function Duel.ClearOperationInfo(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_check_xyz_material`
---@param ... any
---@return any result
function Duel.CheckXyzMaterial(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_select_xyz_material`
---@param ... any
---@return any result
function Duel.SelectXyzMaterial(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_overlay`
---@param ... any
---@return any result
function Duel.Overlay(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_overlay_group`
---@param ... any
---@return Group group
function Duel.GetOverlayGroup(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_overlay_count`
---@param ... any
---@return integer count
function Duel.GetOverlayCount(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_check_remove_overlay_card`
---@param ... any
---@return any result
function Duel.CheckRemoveOverlayCard(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_remove_overlay_card`
---@param ... any
---@return any result
function Duel.RemoveOverlayCard(...) end

--- 向玩家发送提示。
--- C++: `scriptlib::duel_hint`
---@param hintType Hint
---@param player Player
---@param value integer|string
---@return nil
function Duel.Hint(hintType, player, value) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_last_select_hint`
---@param ... any
---@return any result
function Duel.GetLastSelectHint(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_hint_selection`
---@param ... any
---@return any result
function Duel.HintSelection(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_select_effect_yesno`
---@param ... any
---@return any result
function Duel.SelectEffectYesNo(...) end

--- 向玩家询问是否。
--- C++: `scriptlib::duel_select_yesno`
---@param player Player
---@param description integer|string
---@return boolean result
function Duel.SelectYesNo(player, description) end

--- 向玩家展示选项并返回选择索引。
--- C++: `scriptlib::duel_select_option`
---@param player Player
---@param ... integer|string
---@return integer optionIndex
function Duel.SelectOption(player, ...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_select_sequence`
---@param ... any
---@return any result
function Duel.SelectSequence(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_select_position`
---@param ... any
---@return any result
function Duel.SelectPosition(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_select_field`
---@param ... any
---@return any result
function Duel.SelectField(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_select_disable_field`
---@param ... any
---@return any result
function Duel.SelectDisableField(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_announce_race`
---@param ... any
---@return any result
function Duel.AnnounceRace(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_announce_attribute`
---@param ... any
---@return any result
function Duel.AnnounceAttribute(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_announce_level`
---@param ... any
---@return any result
function Duel.AnnounceLevel(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_announce_card`
---@param ... any
---@return any result
function Duel.AnnounceCard(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_announce_type`
---@param ... any
---@return any result
function Duel.AnnounceType(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_announce_number`
---@param ... any
---@return any result
function Duel.AnnounceNumber(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_announce_coin`
---@param ... any
---@return any result
function Duel.AnnounceCoin(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_toss_coin`
---@param ... any
---@return any result
function Duel.TossCoin(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_toss_dice`
---@param ... any
---@return any result
function Duel.TossDice(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_rock_paper_scissors`
---@param ... any
---@return any result
function Duel.RockPaperScissors(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_coin_result`
---@param ... any
---@return any result
function Duel.GetCoinResult(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_dice_result`
---@param ... any
---@return any result
function Duel.GetDiceResult(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_set_coin_result`
---@param ... any
---@return any result
function Duel.SetCoinResult(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_set_dice_result`
---@param ... any
---@return any result
function Duel.SetDiceResult(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_is_player_affected_by_effect`
---@param ... any
---@return any result
function Duel.IsPlayerAffectedByEffect(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_is_player_can_draw`
---@param ... any
---@return any result
function Duel.IsPlayerCanDraw(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_is_player_can_discard_deck`
---@param ... any
---@return any result
function Duel.IsPlayerCanDiscardDeck(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_is_player_can_discard_deck_as_cost`
---@param ... any
---@return any result
function Duel.IsPlayerCanDiscardDeckAsCost(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_is_player_can_summon`
---@param ... any
---@return any result
function Duel.IsPlayerCanSummon(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_is_player_can_mset`
---@param ... any
---@return any result
function Duel.IsPlayerCanMSet(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_is_player_can_sset`
---@param ... any
---@return any result
function Duel.IsPlayerCanSSet(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_is_player_can_spsummon`
---@param ... any
---@return any result
function Duel.IsPlayerCanSpecialSummon(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_is_player_can_flipsummon`
---@param ... any
---@return any result
function Duel.IsPlayerCanFlipSummon(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_is_player_can_spsummon_monster`
---@param ... any
---@return any result
function Duel.IsPlayerCanSpecialSummonMonster(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_is_player_can_spsummon_count`
---@param ... any
---@return integer count
function Duel.IsPlayerCanSpecialSummonCount(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_is_player_can_release`
---@param ... any
---@return any result
function Duel.IsPlayerCanRelease(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_is_player_can_remove`
---@param ... any
---@return any result
function Duel.IsPlayerCanRemove(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_is_player_can_send_to_hand`
---@param ... any
---@return any result
function Duel.IsPlayerCanSendtoHand(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_is_player_can_send_to_grave`
---@param ... any
---@return any result
function Duel.IsPlayerCanSendtoGrave(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_is_player_can_send_to_deck`
---@param ... any
---@return any result
function Duel.IsPlayerCanSendtoDeck(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_is_player_can_additional_summon`
---@param ... any
---@return any result
function Duel.IsPlayerCanAdditionalSummon(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_is_chain_solving`
---@param ... any
---@return any result
function Duel.IsChainSolving(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_is_chain_negatable`
---@param ... any
---@return any result
function Duel.IsChainNegatable(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_is_chain_disablable`
---@param ... any
---@return any result
function Duel.IsChainDisablable(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_is_chain_disabled`
---@param ... any
---@return any result
function Duel.IsChainDisabled(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_check_chain_target`
---@param ... any
---@return any result
function Duel.CheckChainTarget(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_check_chain_uniqueness`
---@param ... any
---@return any result
function Duel.CheckChainUniqueness(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_activity_count`
---@param ... any
---@return integer count
function Duel.GetActivityCount(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_check_phase_activity`
---@param ... any
---@return any result
function Duel.CheckPhaseActivity(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_add_custom_activity_counter`
---@param ... any
---@return any result
function Duel.AddCustomActivityCounter(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_custom_activity_count`
---@param ... any
---@return integer count
function Duel.GetCustomActivityCount(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_get_battled_count`
---@param ... any
---@return integer count
function Duel.GetBattledCount(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_is_able_to_enter_bp`
---@param ... any
---@return any result
function Duel.IsAbleToEnterBP(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_swap_deck_and_grave`
---@param ... any
---@return any result
function Duel.SwapDeckAndGrave(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::duel_majestic_copy`
---@param ... any
---@return any result
function Duel.MajesticCopy(...) end

-------------------------------------------------------------------------------
-- Debug API (11 functions)
-------------------------------------------------------------------------------

--- 输出调试消息。
--- C++: `scriptlib::debug_message`
---@param message string|integer
---@return nil
function Debug.Message(message) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::debug_add_card`
---@param ... any
---@return nil
function Debug.AddCard(...) end

--- 设置调试 duel 的玩家信息。
--- C++: `scriptlib::debug_set_player_info`
---@param player Player
---@param lp integer
---@param startCount integer
---@param drawCount integer
---@return nil
function Debug.SetPlayerInfo(player, lp, startCount, drawCount) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::debug_pre_summon`
---@param ... any
---@return nil
function Debug.PreSummon(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::debug_pre_equip`
---@param ... any
---@return nil
function Debug.PreEquip(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::debug_pre_set_target`
---@param ... any
---@return nil
function Debug.PreSetTarget(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::debug_pre_add_counter`
---@param ... any
---@return nil
function Debug.PreAddCounter(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::debug_reload_field_begin`
---@param ... any
---@return nil
function Debug.ReloadFieldBegin(...) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::debug_reload_field_end`
---@param ... any
---@return nil
function Debug.ReloadFieldEnd(...) end

--- 设置 AI 名称。
--- C++: `scriptlib::debug_set_ai_name`
---@param name string
---@return nil
function Debug.SetAIName(name) end

--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。
--- C++: `scriptlib::debug_show_hint`
---@param ... any
---@return nil
function Debug.ShowHint(...) end
