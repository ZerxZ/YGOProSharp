-- Effect 库全部 55 个 native Lua API 精确签名（对照 ygopro-core/libeffect.cpp）

function register(add, sig)
    add("Effect.CreateEffect", sig(
        "dot",
        "创建绑定到持有者卡片的效果对象（`effect_owner` 默认初始化为当前 `reason_player`）。",
        {"owner"},
        {"---@param owner Card 效果所属（创建者）卡片。"},
        {"---@return Effect effect 新建的效果对象。"}
    ))

    add("Effect.GlobalEffect", sig(
        "dot",
        "创建不绑定具体卡片的全局效果对象（`owner` 为内部 `temp_card`，`effect_owner` 为 `0`）。",
        {},
        {},
        {"---@return Effect effect 新建的全局效果对象。"}
    ))

    add("Effect.Clone", sig(
        "colon",
        "深拷贝当前效果对象（包含已注册的回调句柄与参数配置）。",
        {},
        {},
        {"---@return Effect effect 克隆出的新效果对象。"}
    ))

    add("Effect.Reset", sig(
        "colon",
        "将当前效果从其所属卡片或场地中移除并重置（未注册到有效 handler 时为空操作）。",
        {},
        {},
        {"---@return nil"}
    ))

    add("Effect.GetFieldID", sig(
        "colon",
        "取得效果在当前 duel 内部分配的唯一递增 ID。",
        {},
        {},
        {"---@return integer fieldId 效果内部 ID。"}
    ))

    add("Effect.SetDescription", sig(
        "colon",
        "设置效果的提示描述 ID（通常由 `aux.Stringid(code, idx)` 生成的整数，不接受字符串）。",
        {"description"},
        {"---@param description integer 描述文本 ID。"},
        {"---@return nil"}
    ))

    add("Effect.SetCode", sig(
        "colon",
        "设置效果代码（如 `EFFECT_UPDATE_ATTACK` 或触发事件 `EVENT_SUMMON_SUCCESS`）。",
        {"code"},
        {"---@param code EffectCode 效果代码或事件代码。"},
        {"---@return nil"}
    ))

    add("Effect.SetRange", sig(
        "colon",
        "设置效果生效或可发动时卡片自身必须处于的区域范围。",
        {"range"},
        {"---@param range Location 区域位掩码（`LOCATION_*`）。"},
        {"---@return nil"}
    ))

    add("Effect.SetTargetRange", sig(
        "colon",
        "设置效果影响双方玩家的区域/对象范围（并清除 `EFFECT_FLAG_ABSOLUTE_TARGET` 标志）。",
        {"selfRange", "opponentRange"},
        {
            "---@param selfRange Location|integer 控制者侧的目标范围（或玩家效果数值）。",
            "---@param opponentRange Location|integer 对手侧的目标范围（或玩家效果数值）。"
        },
        {"---@return nil"}
    ))

    add("Effect.SetAbsoluteRange", sig(
        "colon",
        "以固定玩家视角设置绝对目标范围（自动置位 `EFFECT_FLAG_ABSOLUTE_TARGET`）。",
        {"player", "selfRange", "opponentRange"},
        {
            "---@param player Player 基准玩家编号（`0` 时不互换，非 `0` 时交换双方范围）。",
            "---@param selfRange Location|integer `player` 侧的目标范围。",
            "---@param opponentRange Location|integer `1 - player` 侧的目标范围。"
        },
        {"---@return nil"}
    ))

    add("Effect.SetCountLimit", sig(
        "colon",
        "设置效果的次数限制；注意原生层仅接收 2 个参数，誓约/决斗一次等标志直接按位或到 `code` 中。",
        {"count", "code"},
        {
            "---@param count integer 允许次数（传入 `0` 时自动修正为 `1`）。",
            "---@param code? CountLimitCode 共享计数编码，可与 `EFFECT_COUNT_CODE_OATH`、`EFFECT_COUNT_CODE_DUEL`、`EFFECT_COUNT_CODE_SINGLE`、`EFFECT_COUNT_CODE_CHAIN` 按位或（默认 `0`）。"
        },
        {"---@return nil"},
        {"--- 注意：C++ 仅读取 2 个参数（不含 `self`），不存在第 3 个 `flags` 参数；`code == EFFECT_COUNT_CODE_CHAIN` 会自动附带 `EFFECT_COUNT_CODE_SINGLE`。"}
    ))

    add("Effect.SetReset", sig(
        "colon",
        "设置效果的重置时机与计数。",
        {"reset", "count"},
        {
            "---@param reset Reset 重置条件位掩码（含 `RESET_PHASE` 且未指定己方/对方回合时自动补全双方回合）。",
            "---@param count? integer 触发重置所需次数（省略或 `0` 时自动修正为 `1`）。"
        },
        {"---@return nil"}
    ))

    add("Effect.SetType", sig(
        "colon",
        "设置效果类型（会自动补齐关联标志，例如 `EFFECT_TYPE_ACTIVATE` 自动设置魔陷/场地/手牌范围，起动/诱发即时自动附加 `EFFECT_TYPE_FIELD`）。",
        {"type"},
        {"---@param type EffectType 效果类型位掩码（`EFFECT_TYPE_*`）。"},
        {"---@return nil"}
    ))

    add("Effect.SetProperty", sig(
        "colon",
        "设置效果属性位掩码（支持主标志 `flag1` 与扩展标志 `flag2`，保留内部标志位不被覆盖）。",
        {"flag1", "flag2"},
        {
            "---@param flag1 EffectProperty 主属性标志（`EFFECT_FLAG_*`）。",
            "---@param flag2? EffectProperty2 扩展属性标志（`EFFECT_FLAG2_*`，默认 `0`）。"
        },
        {"---@return nil"}
    ))

    add("Effect.SetLabel", sig(
        "colon",
        "清空并写入一个或多个整型自定义标签（label），后续由 `Effect:GetLabel()` 按序多值返回。",
        {"label", "..."},
        {
            "---@param label integer 首个标签值。",
            "---@param ... integer 额外标签值。"
        },
        {"---@return nil"}
    ))

    add("Effect.SetLabelObject", sig(
        "colon",
        "保存与效果关联的对象引用（支持 `Card`、`Effect`、`Group` 或 `nil` 清空，传入其他类型会抛 Lua 错误）。",
        {"object"},
        {"---@param object Card|Effect|Group|nil 关联对象，传 `nil` 清空。"},
        {"---@return nil"}
    ))

    add("Effect.SetCategory", sig(
        "colon",
        "设置效果分类位掩码（用于无效判定及处理类型标注）。",
        {"category"},
        {"---@param category EffectCategory 效果分类位掩码（`CATEGORY_*`）。"},
        {"---@return nil"}
    ))

    add("Effect.SetHintTiming", sig(
        "colon",
        "设置客户端询问发动的提示时点（`opponentTiming` 省略时默认等于 `selfTiming`）。",
        {"selfTiming", "opponentTiming"},
        {
            "---@param selfTiming Timing 己方回合提示时点（`TIMING_*`）。",
            "---@param opponentTiming? Timing 对方回合提示时点（默认同 `selfTiming`）。"
        },
        {"---@return nil"}
    ))

    add("Effect.SetCondition", sig(
        "colon",
        "设置效果发动或适用的条件回调函数。",
        {"condition"},
        {"---@param condition ConditionFunction 条件回调。"},
        {"---@return nil"}
    ))

    add("Effect.SetTarget", sig(
        "colon",
        "设置效果目标选择回调（启动/发动型为 `TargetFunction`，永续/适用型为 `EffectTargetFilter`）。",
        {"target"},
        {"---@param target TargetFunction|EffectTargetFilter 目标选择或适用对象过滤回调。"},
        {"---@return nil"}
    ))

    add("Effect.SetCost", sig(
        "colon",
        "设置效果发动代价（Cost）回调。",
        {"cost"},
        {"---@param cost CostFunction 代价支付回调。"},
        {"---@return nil"}
    ))

    add("Effect.SetValue", sig(
        "colon",
        "设置效果固定值（整型/布尔）或动态计算回调函数。",
        {"value"},
        {"---@param value integer|boolean|ValueFunction 固定数值、布尔值或动态计算函数。"},
        {"---@return nil"}
    ))

    add("Effect.SetOperation", sig(
        "colon",
        "设置效果处理回调；允许传入 `nil` 以清除已设置的处理函数。",
        {"operation"},
        {"---@param operation OperationFunction|nil 效果处理函数，传 `nil` 清空。"},
        {"---@return nil"}
    ))

    add("Effect.SetOwnerPlayer", sig(
        "colon",
        "设置效果的拥有玩家（仅接受 `0` 或 `1`，省略时按 `0` 处理）。",
        {"player"},
        {"---@param player? Player 拥有玩家编号（`0` 或 `1`，默认 `0`）。"},
        {"---@return nil"}
    ))

    add("Effect.GetDescription", sig("colon", "取得效果的描述文本 ID。", {}, {}, {"---@return integer description"}))
    add("Effect.GetCode", sig("colon", "取得效果代码（`EFFECT_*` / `EVENT_*`）。", {}, {}, {"---@return EffectCode code"}))
    add("Effect.GetType", sig("colon", "取得效果类型位掩码（`EFFECT_TYPE_*`）。", {}, {}, {"---@return EffectType type"}))

    add("Effect.GetProperty", sig(
        "colon",
        "取得效果的主属性标志与扩展属性标志（双返回值）。",
        {},
        {},
        {
            "---@return EffectProperty flag1 主属性标志（`EFFECT_FLAG_*`）。",
            "---@return EffectProperty2 flag2 扩展属性标志（`EFFECT_FLAG2_*`）。"
        }
    ))

    add("Effect.GetLabel", sig(
        "colon",
        "返回效果保存的全部整型标签（多返回值）；若尚未设置任何标签，返回单个 `0`。",
        {},
        {},
        {"---@return integer ... 全部标签整数值（未设置时为 `0`）。"}
    ))

    add("Effect.GetLabelObject", sig(
        "colon",
        "取得 `Effect:SetLabelObject` 保存的关联对象引用。",
        {},
        {},
        {"---@return Card|Effect|Group|nil object 关联对象；未设置或失效时返回 `nil`。"}
    ))

    add("Effect.GetCategory", sig("colon", "取得效果分类位掩码（`CATEGORY_*`）。", {}, {}, {"---@return EffectCategory category"}))
    add("Effect.GetRange", sig("colon", "取得效果生效/发动范围（`LOCATION_*`）。", {}, {}, {"---@return Location range"}))
    add("Effect.GetOwner", sig("colon", "取得效果的创建者卡片（`owner`）。", {}, {}, {"---@return Card|nil owner"}))
    add("Effect.GetHandler", sig("colon", "取得效果当前注册所附着的卡片（`handler`）。", {}, {}, {"---@return Card|nil handler"}))
    add("Effect.GetCondition", sig("colon", "取得已设置的条件回调函数。", {}, {}, {"---@return ConditionFunction|nil condition"}))
    add("Effect.GetTarget", sig("colon", "取得已设置的目标选择/适用过滤回调函数。", {}, {}, {"---@return TargetFunction|EffectTargetFilter|nil target"}))
    add("Effect.GetCost", sig("colon", "取得已设置的代价回调函数。", {}, {}, {"---@return CostFunction|nil cost"}))
    add("Effect.GetValue", sig("colon", "取得已设置的固定整型值或动态值回调函数。", {}, {}, {"---@return ValueFunction|integer value 若设置的是函数则返回函数，否则返回整型值。"}))
    add("Effect.GetOperation", sig("colon", "取得已设置的处理回调函数。", {}, {}, {"---@return OperationFunction|nil operation"}))
    add("Effect.GetActiveType", sig("colon", "取得效果发动时所视为的卡片种类位掩码。", {}, {}, {"---@return CardType activeType"}))

    add("Effect.IsActiveType", sig(
        "colon",
        "判定效果发动时所视为的卡片种类是否包含指定位掩码的任一位。",
        {"cardType"},
        {"---@param cardType CardType 卡片种类位掩码（`TYPE_*`）。"},
        {"---@return boolean matched"}
    ))

    add("Effect.GetOwnerPlayer", sig("colon", "取得效果的拥有玩家编号。", {}, {}, {"---@return Player player"}))
    add("Effect.GetHandlerPlayer", sig("colon", "取得效果当前附着卡片的控制者编号。", {}, {}, {"---@return Player player"}))

    add("Effect.IsHasProperty", sig(
        "colon",
        "检查效果是否具备指定的属性标志（主标志 `flag1` 与扩展标志 `flag2` 均按非零项检查）。",
        {"flag1", "flag2"},
        {
            "---@param flag1 EffectProperty 要检查的主属性位掩码（传 `0` 跳过主标志检查）。",
            "---@param flag2? EffectProperty2 要检查的扩展属性位掩码（默认 `0` 跳过）。"
        },
        {"---@return boolean matched"}
    ))

    add("Effect.IsHasCategory", sig(
        "colon",
        "检查效果分类是否包含指定位掩码的任一位。",
        {"category"},
        {"---@param category EffectCategory 效果分类位掩码（`CATEGORY_*`）。"},
        {"---@return boolean matched"}
    ))

    add("Effect.IsHasType", sig(
        "colon",
        "检查效果类型是否包含指定位掩码的任一位。",
        {"type"},
        {"---@param type EffectType 效果类型位掩码（`EFFECT_TYPE_*`）。"},
        {"---@return boolean matched"}
    ))

    add("Effect.IsHasRange", sig(
        "colon",
        "检查效果范围是否包含指定区域位掩码的任一位。",
        {"range"},
        {"---@param range Location 区域位掩码（`LOCATION_*`）。"},
        {"---@return boolean matched"}
    ))

    add("Effect.IsActivatable", sig(
        "colon",
        "判定指定玩家当前能否发动该效果（使用空事件检查）。",
        {"player", "neglectLoc", "neglectTarget"},
        {
            "---@param player Player 发动玩家编号。",
            "---@param neglectLoc? boolean 是否忽略位置限制（默认 `false`）。",
            "---@param neglectTarget? boolean 是否忽略目标限制（默认 `false`）。"
        },
        {"---@return boolean activatable"}
    ))

    add("Effect.IsActivated", sig(
        "colon",
        "判定该效果是否为入连锁的发动型效果（`EFFECT_TYPES_CHAIN_LINK`）。",
        {},
        {},
        {"---@return boolean activated"}
    ))

    add("Effect.IsCostChecked", sig(
        "colon",
        "返回该效果的 `cost_checked` 标记（常用于复制效果时跳过或检查 Cost）。",
        {},
        {},
        {"---@return boolean checked"}
    ))

    add("Effect.SetCostCheck", sig(
        "colon",
        "设置该效果的 `cost_checked` 标记。",
        {"costCheck"},
        {"---@param costCheck boolean 是否标记为需检查 Cost。"},
        {"---@return nil"}
    ))

    add("Effect.GetActivateLocation", sig("colon", "取得该效果上次发动时所在的区域。", {}, {}, {"---@return Location location"}))
    add("Effect.GetActivateSequence", sig("colon", "取得该效果上次发动时所在的区域序号。", {}, {}, {"---@return integer sequence"}))

    add("Effect.CheckCountLimit", sig(
        "colon",
        "检查指定玩家是否仍满足该效果的次数限制。",
        {"player"},
        {"---@param player Player 玩家编号。"},
        {"---@return boolean allowed"}
    ))

    add("Effect.UseCountLimit", sig(
        "colon",
        "手动消耗指定玩家对该效果的剩余次数限制。",
        {"player", "count", "oathOnly"},
        {
            "---@param player Player 玩家编号。",
            "---@param count? integer 消耗次数（默认 `1`）。",
            "---@param oathOnly? boolean 为 `true` 时仅在该效果带有 `EFFECT_COUNT_CODE_OATH` 时才扣减（默认 `false`）。"
        },
        {"---@return nil"}
    ))
end

return { register = register }
