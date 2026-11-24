-- Card 库全部 281 个 native Lua API 精确签名（对照 ygopro-core/libcard.cpp）

function register(add, sig)
    local function add_noarg_int(names, template)
        for _, item in ipairs(names) do
            add("Card." .. item[1], sig("colon", item[2], {}, {}, {"---@return " .. (item[3] or "integer") .. " value"}))
        end
    end

    local function add_noarg_bool(names)
        for _, item in ipairs(names) do
            add("Card." .. item[1], sig("colon", item[2], {}, {}, {"---@return boolean result"}))
        end
    end

    local function add_noarg_group(names)
        for _, item in ipairs(names) do
            add("Card." .. item[1], sig("colon", item[2], {}, {}, {"---@return Group group"}))
        end
    end

    local function add_noarg_card_opt(names)
        for _, item in ipairs(names) do
            add("Card." .. item[1], sig("colon", item[2], {}, {}, {"---@return Card|nil card"}))
        end
    end

    local function add_variadic_int_pred(names, param_name, param_type)
        for _, item in ipairs(names) do
            add("Card." .. item[1], sig(
                "colon",
                item[2],
                {param_name, "..."},
                {
                    "---@param " .. param_name .. " " .. param_type .. "|nil 目标值（`nil` 会被跳过）。",
                    "---@param ... " .. param_type .. "|nil 额外候选值（匹配任意一个即返回 `true`）。"
                },
                {"---@return boolean matched"}
            ))
        end
    end

    local function add_single_int_pred(names, param_name, param_type)
        for _, item in ipairs(names) do
            add("Card." .. item[1], sig(
                "colon",
                item[2],
                {param_name},
                {"---@param " .. param_name .. " " .. param_type .. " " .. (item[3] or "检查值。")},
                {"---@return boolean matched"}
            ))
        end
    end

    -- =========================================================================
    -- 卡号与字段
    -- =========================================================================
    add("Card.GetCode", sig(
        "colon",
        "取得当前卡号；若存在第二卡号（如规则同名卡）则额外返回第 2 个卡号。",
        {}, {},
        {"---@return CardCode code 当前主卡号。", "---@return CardCode? anotherCode 第二卡号（若存在）。"}
    ))
    add("Card.GetOriginalCode", sig("colon", "取得卡片的原本单卡号（`data.get_original_code()`）。", {}, {}, {"---@return CardCode code"}))
    add("Card.GetOriginalCodeRule", sig(
        "colon",
        "取得规则视角上的原本卡号；存在第二原本卡号时返回 2 个卡号。",
        {}, {},
        {"---@return CardCode code 规则原本卡号。", "---@return CardCode? secondCode 第二规则卡号（若存在）。"}
    ))
    add("Card.GetFusionCode", sig(
        "colon",
        "多值返回作为融合素材时的全部卡号（当前卡号、可选第二卡号及 `EFFECT_ADD_FUSION_CODE` 追加卡号）。",
        {}, {},
        {"---@return CardCode ... 融合卡号列表。"}
    ))
    add("Card.GetLinkCode", sig(
        "colon",
        "多值返回作为连接素材时的全部卡号（当前卡号、可选第二卡号及 `EFFECT_ADD_LINK_CODE` 追加卡号）。",
        {}, {},
        {"---@return CardCode ... 连接卡号列表。"}
    ))

    add_variadic_int_pred({
        {"IsCode", "判断当前卡号（含第二卡号）是否匹配任意给定卡号。"},
        {"IsOriginalCodeRule", "判断规则原本卡号是否匹配任意给定卡号。"},
        {"IsFusionCode", "判断作为融合素材时的卡号集合是否匹配任意给定卡号。"},
        {"IsLinkCode", "判断作为连接素材时的卡号集合是否匹配任意给定卡号。"}
    }, "code", "CardCode")

    add_variadic_int_pred({
        {"IsSetCard", "判断当前字段是否匹配任意给定字段编码。"},
        {"IsOriginalSetCard", "判断原本字段是否匹配任意给定字段编码。"},
        {"IsPreviousSetCard", "判断离场/区域变更前的字段是否匹配任意给定字段编码。"},
        {"IsFusionSetCard", "判断作为融合素材时的字段是否匹配任意给定字段编码。"},
        {"IsLinkSetCard", "判断作为连接素材时的字段是否匹配任意给定字段编码。"},
        {"IsSpecialSummonSetCard", "判断特殊召唤快照记录的字段是否匹配任意给定字段编码。"}
    }, "setcode", "SetCode")

    -- =========================================================================
    -- 种类、等级、阶级、连接、灵摆刻度、同列与连接区
    -- =========================================================================
    add_noarg_int({
        {"GetCardType", "取得基础卡片种类位掩码（受 `EFFECT_ADD_CARD_TYPE` 等基础种类效果影响）。", "CardType"},
        {"GetType", "取得当前完整卡片种类位掩码（`TYPE_*`）。", "CardType"},
        {"GetOriginalType", "取得卡库记录的原本卡片种类（`data.type`）。", "CardType"},
        {"GetFusionType", "取得作为融合素材时视为的卡片种类。", "CardType"},
        {"GetSynchroType", "取得作为同调素材时视为的卡片种类。", "CardType"},
        {"GetXyzType", "取得作为超量素材时视为的卡片种类。", "CardType"},
        {"GetLinkType", "取得作为连接素材时视为的卡片种类。", "CardType"},
        {"GetLevel", "取得当前等级（无等级时返回 `0`）。", "integer"},
        {"GetRank", "取得当前阶级（非超量时返回 `0`）。", "integer"},
        {"GetLink", "取得当前连接值（非连接时返回 `0`）。", "integer"},
        {"GetOriginalLevel", "取得原本等级（原本超量/连接或带 `STATUS_NO_LEVEL` 时返回 `0`）。", "integer"},
        {"GetOriginalRank", "取得原本阶级（原本非超量时返回 `0`）。", "integer"},
        {"GetLeftScale", "取得当前左灵摆刻度。", "integer"},
        {"GetOriginalLeftScale", "取得原本左灵摆刻度（`data.lscale`）。", "integer"},
        {"GetRightScale", "取得当前右灵摆刻度。", "integer"},
        {"GetOriginalRightScale", "取得原本右灵摆刻度（`data.rscale`）。", "integer"},
        {"GetCurrentScale", "取得当前生效的灵摆刻度（在左/右 P 区取左/右刻度，否则取 `data.lscale`）。", "integer"},
        {"GetLinkedGroupCount", "取得此卡连接端指向的卡片数量。", "integer"},
        {"GetMutualLinkedGroupCount", "取得与此卡互相连接的卡片数量。", "integer"},
        {"GetColumnGroupCount", "取得与此卡同纵列的卡片数量。", "integer"}
    })

    add("Card.GetSynchroLevel", sig(
        "colon",
        "取得作为同调怪兽 `sc` 素材时的同调等级（可能以高低 16 位编码两个可选等级）。",
        {"sc"},
        {"---@param sc Card 目标同调怪兽。"},
        {"---@return integer synchroLevel"}
    ))
    add("Card.GetRitualLevel", sig(
        "colon",
        "取得作为仪式怪兽 `rc` 祭品时的仪式等级（可能以高低 16 位编码两个可选等级）。",
        {"rc"},
        {"---@param rc Card 目标仪式怪兽。"},
        {"---@return integer ritualLevel"}
    ))
    add("Card.IsXyzLevel", sig(
        "colon",
        "判定此卡作为超量怪兽 `xyzCard` 素材时是否可视为等级 `lv`。",
        {"xyzCard", "lv"},
        {
            "---@param xyzCard Card 目标超量怪兽。",
            "---@param lv integer 目标等级。"
        },
        {"---@return boolean matched"}
    ))
    add("Card.IsLinkMarker", sig(
        "colon",
        "判定此卡是否具有指定的连接标记方向。",
        {"marker"},
        {"---@param marker LinkMarker 连接标记位掩码（`LINK_MARKER_*`）。"},
        {"---@return boolean matched"}
    ))

    add_noarg_group({
        {"GetLinkedGroup", "取得此卡连接端指向的卡片集合。"},
        {"GetMutualLinkedGroup", "取得与此卡互相连接的卡片集合。"},
        {"GetColumnGroup", "取得与此卡同纵列的卡片集合（注意：`ygopro-core` 中无参数，与 EDOPro 不同）。"}
    })

    add("Card.GetLinkedZone", sig(
        "colon",
        "取得此卡连接端所指的区域位掩码；`player` 为对方时自动互换高低 16 位。",
        {"player"},
        {"---@param player? Player 视角玩家（默认卡片当前控制者）。"},
        {"---@return Zone zone"}
    ))
    add("Card.GetMutualLinkedZone", sig(
        "colon",
        "取得与此卡互相连接的区域位掩码；`player` 为对方时自动互换高低 16 位。",
        {"player"},
        {"---@param player? Player 视角玩家（默认卡片当前控制者）。"},
        {"---@return Zone zone"}
    ))
    add("Card.GetColumnZone", sig(
        "colon",
        "取得与此卡同纵列且属于区域 `loc` 的格子位掩码；`player` 为对方时自动互换高低 16 位。",
        {"loc", "player"},
        {
            "---@param loc Location 区域筛选掩码（如 `LOCATION_MZONE`、`LOCATION_SZONE`）。",
            "---@param player? Player 视角玩家（默认卡片当前控制者）。"
        },
        {"---@return Zone zone"}
    ))

    add_noarg_bool({
        {"IsLinkState", "判定此卡是否处于连接状态。"},
        {"IsExtraLinkState", "判定此卡是否处于额外连接状态。"},
        {"IsAllColumn", "判定此卡所在纵列是否全格都有卡。"}
    })

    -- =========================================================================
    -- 属性、种族、攻击力、守备力与离场前场上快照
    -- =========================================================================
    add_noarg_int({
        {"GetAttribute", "取得当前属性位掩码。", "Attribute"},
        {"GetOriginalAttribute", "取得原本属性（带 `STATUS_NO_LEVEL` 时返回 `0`）。", "Attribute"},
        {"GetRace", "取得当前种族位掩码。", "Race"},
        {"GetOriginalRace", "取得原本种族（带 `STATUS_NO_LEVEL` 时返回 `0`）。", "Race"},
        {"GetAttack", "取得当前攻击力（负值钳位为 `0`）。", "integer"},
        {"GetBaseAttack", "取得原本攻击力（`get_base_attack()`，负值钳位为 `0`）。", "integer"},
        {"GetTextAttack", "取得卡面印刷攻击力（`?` 返回 `-2`，带 `STATUS_NO_LEVEL` 返回 `0`）。", "integer"},
        {"GetDefense", "取得当前守备力（负值钳位为 `0`）。", "integer"},
        {"GetBaseDefense", "取得原本守备力（`get_base_defense()`，负值钳位为 `0`）。", "integer"},
        {"GetTextDefense", "取得卡面印刷守备力（`?` 返回 `-2`，带 `STATUS_NO_LEVEL` 返回 `0`）。", "integer"},
        {"GetPreviousCardTypeOnField", "取得离场前在场上的基础种类位掩码。", "CardType"},
        {"GetPreviousTypeOnField", "取得离场前在场上的完整种类位掩码。", "CardType"},
        {"GetPreviousLevelOnField", "取得离场前在场上的等级。", "integer"},
        {"GetPreviousRankOnField", "取得离场前在场上的阶级。", "integer"},
        {"GetPreviousAttributeOnField", "取得离场前在场上的属性。", "Attribute"},
        {"GetPreviousRaceOnField", "取得离场前在场上的种族。", "Race"},
        {"GetPreviousAttackOnField", "取得离场前在场上的攻击力。", "integer"},
        {"GetPreviousDefenseOnField", "取得离场前在场上的守备力。", "integer"},
        {"GetPreviousOverlayCountOnField", "取得离场前在场上的超量素材数量。", "integer"}
    })

    add("Card.GetPreviousCodeOnField", sig(
        "colon",
        "取得离场前在场上的卡号；存在第二卡号时返回 2 个卡号。",
        {}, {},
        {"---@return CardCode code", "---@return CardCode? anotherCode"}
    ))

    local function add_opt_player_getter(name, desc, retType)
        add("Card." .. name, sig(
            "colon",
            desc,
            {"player"},
            {"---@param player? Player 视角玩家（默认当前 `reason_player`）。"},
            {"---@return " .. retType .. " value"}
        ))
    end
    add_opt_player_getter("GetFusionAttribute", "取得作为融合素材时对指定玩家视为的属性。", "Attribute")
    add_opt_player_getter("GetLinkAttribute", "取得作为连接素材时对指定玩家视为的属性。", "Attribute")
    add_opt_player_getter("GetAttributeInGrave", "取得在墓地对指定玩家视为的属性。", "Attribute")
    add_opt_player_getter("GetLinkRace", "取得作为连接素材时对指定玩家视为的种族。", "Race")
    add_opt_player_getter("GetRaceInGrave", "取得在墓地对指定玩家视为的种族。", "Race")

    -- =========================================================================
    -- 控制权、位置、原因、召唤信息与 ID
    -- =========================================================================
    add_noarg_int({
        {"GetOwner", "取得卡片持有者（原本卡组所属玩家）。", "Player"},
        {"GetControler", "取得卡片当前控制者。", "Player"},
        {"GetPreviousControler", "取得卡片上一个控制者。", "Player"},
        {"GetReason", "取得卡片当前移动/状态变更的原因位掩码。", "Reason"},
        {"GetReasonPlayer", "取得触发当前原因的玩家编号。", "Player"},
        {"GetPosition", "取得卡片当前表示形式。", "Position"},
        {"GetPreviousPosition", "取得卡片上一个表示形式。", "Position"},
        {"GetBattlePosition", "取得战斗期间记录的临时表示形式（`temp.position`）。", "Position"},
        {"GetLocation", "取得卡片当前所在区域（若被视为不在场则返回 `0`）。", "Location"},
        {"GetPreviousLocation", "取得卡片上一个所在区域。", "Location"},
        {"GetSequence", "取得卡片当前所在区域内的序号。", "integer"},
        {"GetPreviousSequence", "取得卡片上一个区域内的序号。", "integer"},
        {"GetSummonType", "取得卡片的召唤类型（`summon_info & DEFAULT_SUMMON_TYPE`）。", "SummonType"},
        {"GetSummonLocation", "取得卡片召唤前所在的区域。", "Location"},
        {"GetSummonPlayer", "取得执行召唤的玩家编号。", "Player"},
        {"GetDestination", "取得即将移动到的目标区域（`sendto_param.location`）。", "Location"},
        {"GetLeaveFieldDest", "取得因效果离场时的重定向目标区域。", "Location"},
        {"GetTurnID", "取得进入当前区域的回合编号。", "integer"},
        {"GetFieldID", "取得场上标识 ID（`fieldid`）。", "integer"},
        {"GetRealFieldID", "取得场上真实标识 ID（`fieldid_r`，用于判定是否离开过场）。", "integer"}
    })

    add("Card.SetReason", sig(
        "colon",
        "覆写卡片当前原因字段 `current.reason`。",
        {"reason"},
        {"---@param reason Reason 原因位掩码。"},
        {"---@return nil"}
    ))
    add("Card.GetReasonCard", sig("colon", "取得导致当前原因的关联卡片。", {}, {}, {"---@return Card|nil card"}))
    add("Card.GetReasonEffect", sig("colon", "取得导致当前原因的关联效果。", {}, {}, {"---@return Effect|nil effect"}))
    add("Card.GetSpecialSummonInfo", sig(
        "colon",
        "按传入的 0..N 个 `SUMMON_INFO_*` 标志依次返回对应特召快照值（`SUMMON_INFO_REASON_EFFECT` 返回 `Effect|nil`，其余为 `integer`，未知标志返回 `nil`）。",
        {"..."},
        {"---@param ... SummonInfo 要查询的特召快照标志（`SUMMON_INFO_*`）。"},
        {"---@return (integer|Effect|nil) ... 与输入标志一一对应的返回值列表。"}
    ))

    -- =========================================================================
    -- 类型/数值/种族/属性/原因/状态条件判定
    -- =========================================================================
    add_single_int_pred({
        {"IsCardType", "判定基础卡片种类是否包含指定位掩码的**任一位**（区别于 `IsType` 与 `IsAllCardTypes`）。", "基础种类位掩码（`TYPE_*`）。"},
        {"IsAllCardTypes", "判定基础卡片种类是否**同时包含**指定位掩码的全部位。", "基础种类位掩码（`TYPE_*`）。"},
        {"IsPreviousCardTypeOnField", "判定离场前在场上的基础种类是否包含指定位掩码的任一位。", "基础种类位掩码（`TYPE_*`）。"},
        {"IsType", "判定当前种类是否包含指定位掩码的**任一位**（多标志全匹配请用 `IsAllTypes`）。", "种类位掩码（`TYPE_*`）。"},
        {"IsAllTypes", "判定当前种类是否**同时包含**指定位掩码的全部位。", "种类位掩码（`TYPE_*`）。"},
        {"IsFusionType", "判定融合素材种类是否包含指定位掩码的任一位。", "种类位掩码（`TYPE_*`）。"},
        {"IsSynchroType", "判定同调素材种类是否包含指定位掩码的任一位。", "种类位掩码（`TYPE_*`）。"},
        {"IsXyzType", "判定超量素材种类是否包含指定位掩码的任一位。", "种类位掩码（`TYPE_*`）。"},
        {"IsLinkType", "判定连接素材种类是否包含指定位掩码的任一位。", "种类位掩码（`TYPE_*`）。"}
    }, "cardType", "CardType")

    add_variadic_int_pred({
        {"IsLevel", "判定当前等级是否等于任意给定值。"},
        {"IsRank", "判定当前阶级是否等于任意给定值。"},
        {"IsLink", "判定当前连接值是否等于任意给定值。"},
        {"IsAttack", "判定当前攻击力是否等于任意给定值（非怪兽且无 `EFFECT_PRE_MONSTER` 时恒返回 `false`）。"},
        {"IsDefense", "判定当前守备力是否等于任意给定值（原本连接怪兽或非怪兽且无 `EFFECT_PRE_MONSTER` 时恒返回 `false`）。"}
    }, "value", "integer")

    add_single_int_pred({
        {"IsRace", "判定当前种族是否包含指定位掩码的任一位。", "种族位掩码（`RACE_*`）。"}
    }, "race", "Race")
    add("Card.IsLinkRace", sig(
        "colon",
        "判定作为连接素材时对指定玩家视为的种族是否包含任一位。",
        {"race", "player"},
        {"---@param race Race 种族位掩码。", "---@param player? Player 视角玩家（默认 `reason_player`）。"},
        {"---@return boolean matched"}
    ))

    add_single_int_pred({
        {"IsAttribute", "判定当前属性是否包含指定位掩码的任一位。", "属性位掩码（`ATTRIBUTE_*`）。"},
        {"IsNonAttribute", "判定当前属性是否拥有除 `attribute` 外的任意有效属性（`ATTRIBUTE_ALL & ~attribute`）。", "要排除的属性位掩码。"}
    }, "attribute", "Attribute")
    add("Card.IsFusionAttribute", sig(
        "colon",
        "判定作为融合素材时对指定玩家视为的属性是否包含任一位。",
        {"attribute", "player"},
        {"---@param attribute Attribute 属性位掩码。", "---@param player? Player 视角玩家（默认 `reason_player`）。"},
        {"---@return boolean matched"}
    ))
    add("Card.IsLinkAttribute", sig(
        "colon",
        "判定作为连接素材时对指定玩家视为的属性是否包含任一位。",
        {"attribute", "player"},
        {"---@param attribute Attribute 属性位掩码。", "---@param player? Player 视角玩家（默认 `reason_player`）。"},
        {"---@return boolean matched"}
    ))

    add_noarg_bool({
        {"IsExtraDeckMonster", "判定此卡是否为额外卡组怪兽。"},
        {"IsDualState", "判定二重怪兽是否已处于再度召唤状态（`EFFECT_DUAL_STATUS`）。"}
    })

    add_single_int_pred({
        {"IsReason", "判定当前原因是否包含指定位掩码的**任一位**（全匹配请用 `IsAllReasons`）。", "原因位掩码（`REASON_*`）。"},
        {"IsAllReasons", "判定当前原因是否**同时包含**指定位掩码的全部位。", "原因位掩码（`REASON_*`）。"}
    }, "reason", "Reason")

    add_single_int_pred({
        {"IsSummonType", "判定召唤类型是否**全部包含**指定位掩码（注意：为全位精确匹配，非任一位匹配）。", "召唤类型位掩码（`SUMMON_TYPE_*`）。"}
    }, "summonType", "SummonType")
    add_single_int_pred({
        {"IsSummonLocation", "判定召唤前所在区域是否包含指定位掩码的任一位。", "区域位掩码（`LOCATION_*`）。"}
    }, "location", "Location")
    add_single_int_pred({
        {"IsSummonPlayer", "判定召唤玩家是否等于 `player`。", "玩家编号。"}
    }, "player", "Player")
    add_single_int_pred({
        {"IsStatus", "判定卡片状态是否包含指定 `STATUS_*` 位掩码的任一位。", "状态位掩码（`STATUS_*`）。"}
    }, "status", "Status")

    add("Card.IsNotTuner", sig(
        "colon",
        "判定作为同调怪兽 `sc` 素材时能否被视为非调整怪兽。",
        {"sc"},
        {"---@param sc Card 目标同调怪兽。"},
        {"---@return boolean result"}
    ))
    add("Card.IsTuner", sig(
        "colon",
        "判定作为同调怪兽 `sc` 素材时能否被视为调整怪兽。",
        {"sc"},
        {"---@param sc Card 目标同调怪兽。"},
        {"---@return boolean result"}
    ))
    add("Card.IsOriginalEffectProperty", sig(
        "colon",
        "判定卡片原本效果属性是否满足过滤函数 `filter`。",
        {"filter"},
        {"---@param filter EffectFilterFunction 效果过滤函数。"},
        {"---@return boolean matched"}
    ))
    add("Card.IsEffectProperty", sig(
        "colon",
        "判定卡片当前效果属性是否满足过滤函数 `filter`。",
        {"filter"},
        {"---@param filter EffectFilterFunction 效果过滤函数。"},
        {"---@return boolean matched"}
    ))

    add("Card.SetStatus", sig(
        "colon",
        "设置或清除卡片状态位（处于 `STATUS_COPYING_EFFECT` 时忽略）。",
        {"status", "enable"},
        {"---@param status Status 状态位掩码（`STATUS_*`）。", "---@param enable boolean `true` 置位，`false` 清除。"},
        {"---@return nil"}
    ))
    add("Card.EnableDualState", sig("colon", "赋予卡片二重再度召唤状态（`EFFECT_DUAL_STATUS`）。", {}, {}, {"---@return nil"}))
    add("Card.SetTurnCounter", sig("colon", "设置卡片上显示的回合计数。", {"count"}, {"---@param count integer 回合计数值。"}, {"---@return nil"}))
    add("Card.GetTurnCounter", sig("colon", "取得卡片当前显示的回合计数。", {}, {}, {"---@return integer count"}))

    -- =========================================================================
    -- 素材、装备、同盟、超量素材、战斗与持续取对象
    -- =========================================================================
    add("Card.SetMaterial", sig(
        "colon",
        "设置该卡的召唤素材集合；传 `nil` 清空素材。",
        {"materials"},
        {"---@param materials Group|nil 素材集合，传 `nil` 清空。"},
        {"---@return nil"}
    ))
    add_noarg_group({
        {"GetMaterial", "取得该卡的召唤素材卡片集合。"},
        {"GetEquipGroup", "取得当前装备到此卡上的装备卡集合。"},
        {"GetOverlayGroup", "取得该卡持有的超量素材集合。"},
        {"GetAttackedGroup", "取得本回合被该卡攻击过的怪兽集合（不含直接攻击）。"},
        {"GetBattledGroup", "取得本回合与该卡战斗过的怪兽集合。"},
        {"GetCardTarget", "取得该卡持续取对象的所有卡片集合。"},
        {"GetOwnerTarget", "取得正在持续取该卡为对象的所有卡片集合。"}
    })
    add_noarg_int({
        {"GetMaterialCount", "取得该卡的召唤素材数量。", "integer"},
        {"GetEquipCount", "取得装备到此卡上的装备卡数量。", "integer"},
        {"GetOverlayCount", "取得该卡持有的超量素材数量。", "integer"},
        {"GetAttackedGroupCount", "取得本回合攻击记录数（含直接攻击）。", "integer"},
        {"GetAttackedCount", "取得本回合已完成的攻击次数。", "integer"},
        {"GetBattledGroupCount", "取得本回合战斗过的记录数。", "integer"},
        {"GetAttackAnnouncedCount", "取得本回合攻击宣言次数。", "integer"},
        {"GetCardTargetCount", "取得该卡持续取对象的卡片数量。", "integer"},
        {"GetOwnerTargetCount", "取得正在持续取该卡为对象的卡片数量。", "integer"}
    })
    add_noarg_card_opt({
        {"GetEquipTarget", "取得该装备卡当前装备的目标怪兽。"},
        {"GetPreviousEquipTarget", "取得该装备卡之前装备的目标怪兽。"},
        {"GetOverlayTarget", "取得持有该超量素材的超量怪兽。"},
        {"GetFirstCardTarget", "取得该卡持续取对象的第一张卡片。"},
        {"GetBattleTarget", "取得当前战斗的对手怪兽（攻击者返回防御者，反之亦然；非战斗中返回 `nil`）。"}
    })

    add("Card.CheckEquipTarget", sig(
        "colon",
        "检查 `target` 是否符合该装备卡的装备限制及同盟互斥限制。",
        {"target"},
        {"---@param target Card 候选装备目标怪兽。"},
        {"---@return boolean valid"}
    ))
    add("Card.CheckUnionTarget", sig(
        "colon",
        "检查 `target` 是否符合该同盟卡的装备限制及新旧同盟互斥限制。",
        {"target"},
        {"---@param target Card 候选同盟装备目标怪兽。"},
        {"---@return boolean valid"}
    ))
    add("Card.GetUnionCount", sig(
        "colon",
        "返回装备在该卡上的新同盟与旧同盟数量（双返回值）。",
        {}, {},
        {"---@return integer newUnionCount 新规则同盟怪兽数量。", "---@return integer oldUnionCount 旧规则同盟怪兽数量。"}
    ))
    add("Card.CheckRemoveOverlayCard", sig(
        "colon",
        "检查玩家能否因 `reason` 从该卡取除至少 `count` 个超量素材。",
        {"player", "count", "reason"},
        {
            "---@param player Player 玩家编号（0 或 1）。",
            "---@param count integer 需要取除的数量。",
            "---@param reason Reason 原因位掩码。"
        },
        {"---@return boolean|nil canRemove 能否取除（`player` 非 0/1 无返回值）。"}
    ))
    add("Card.RemoveOverlayCard", sig(
        "colon",
        "[协程挂起] 让玩家从该卡取除 `min..max` 个超量素材并返回实际取除数量。",
        {"player", "min", "max", "reason"},
        {
            "---@param player Player 玩家编号（0 或 1）。",
            "---@param min integer 最少取除数。",
            "---@param max integer 最多取除数。",
            "---@param reason Reason 原因位掩码。"
        },
        {"---@return integer|nil removed 实际取除数量（`player` 非 0/1 无返回值）。"}
    ))
    add("Card.IsDirectAttacked", sig("colon", "判定此卡本回合是否进行过直接攻击。", {}, {}, {"---@return boolean directAttacked"}))
    add("Card.SetCardTarget", sig("colon", "建立此卡对 `target` 的持续取对象关联。", {"target"}, {"---@param target Card 目标卡片。"}, {"---@return nil"}))
    add("Card.IsHasCardTarget", sig("colon", "判定此卡是否正在持续取 `target` 为对象。", {"target"}, {"---@param target Card 目标卡片。"}, {"---@return boolean targeted"}))
    add("Card.CancelCardTarget", sig("colon", "解除此卡对 `target` 的持续取对象关联。", {"target"}, {"---@param target Card 目标卡片。"}, {"---@return nil"}))

    -- =========================================================================
    -- 效果查询、注册、标记效果与关联
    -- =========================================================================
    add("Card.GetActivateEffect", sig(
        "colon",
        "多值返回该卡上的全部 `EFFECT_TYPE_ACTIVATE` 效果（无则返回 0 个值）。",
        {}, {},
        {"---@return Effect ... 卡片发动效果列表。"}
    ))
    add("Card.CheckActivateEffect", sig(
        "colon",
        "查找首个可发动的 `EFFECT_TYPE_ACTIVATE` 效果；`copyInfo=false` 或自由时点返回单个 `Effect`，否则返回 7 个值含触发事件信息。",
        {"neglectCon", "neglectCost", "copyInfo"},
        {
            "---@param neglectCon boolean 是否忽略发动条件。",
            "---@param neglectCost boolean 是否忽略发动代价。",
            "---@param copyInfo boolean 为 `true` 且非 `EVENT_FREE_CHAIN` 时额外返回触发事件 6 元组。"
        },
        {
            "---@return Effect|nil effect 首个可发动的发动效果；无则返回 `nil`。",
            "---@return Group|nil eg 事件卡组（仅 `copyInfo=true` 且非自由时点）。",
            "---@return Player? ep 事件玩家。",
            "---@return integer? ev 事件值。",
            "---@return Effect|nil re 触发效果。",
            "---@return Reason? r 触发原因。",
            "---@return Player? rp 触发玩家。"
        }
    ))
    add("Card.GetTunerLimit", sig(
        "colon",
        "查询此卡上的 `EFFECT_TUNER_MATERIAL_LIMIT` 限制信息（存在时返回 5 个值，否则无返回值）。",
        {}, {},
        {
            "---@return Effect|nil effect 限制效果。",
            "---@return function|nil targetFilter 额外过滤函数。",
            "---@return Location|nil location 区域限制值。",
            "---@return integer? min 最少张数（默认 `0`）。",
            "---@return integer? max 最多张数（默认 `99`）。"
        }
    ))
    add("Card.GetHandSynchro", sig(
        "colon",
        "查询怪兽区此卡的手牌同调效果信息（存在时返回 4 个值，否则无返回值）。",
        {}, {},
        {
            "---@return Effect|nil effect 效果对象。",
            "---@return function|nil targetFilter 额外过滤函数。",
            "---@return integer? min 最少张数（默认 `0`）。",
            "---@return integer? max 最多张数（默认 `99`）。"
        }
    ))
    add("Card.RegisterEffect", sig(
        "colon",
        "将效果 `effect` 注册到当前卡片（注意：仅 2 个参数，不存在第 3 个 `player` 参数）。",
        {"effect", "forced"},
        {
            "---@param effect Effect 要注册的效果对象。",
            "---@param forced? boolean 是否强制注册（忽略卡片对该效果的免疫，默认 `false`）。"
        },
        {"---@return integer|nil effectId 注册成功返回效果 ID；效果已有 handler 返回 `-1`；卡片免疫且非强制或 owner 为 temp_card 时无返回值（`nil`）。"}
    ))
    add("Card.IsHasEffect", sig(
        "colon",
        "多值返回该卡上所有生效且匹配 `code` 的效果（可按玩家过滤次数限制）；无匹配时返回 `nil`。",
        {"code", "checkPlayer"},
        {
            "---@param code EffectCode 要查询的效果代码。",
            "---@param checkPlayer? Player 传 `0` 或 `1` 时仅返回对该玩家仍满足次数限制的效果（默认不检查）。"
        },
        {"---@return Effect ... 匹配的全部效果对象；无匹配时返回 `nil`。"}
    ))
    add("Card.ResetEffect", sig(
        "colon",
        "按重置类型 `resetType`（如 `RESET_CODE`、`RESET_EVENT`）重置该卡的效果。",
        {"resetCode", "resetType"},
        {
            "---@param resetCode integer 重置编码或事件掩码。",
            "---@param resetType Reset 重置类型（`RESET_*`）。"
        },
        {"---@return nil"}
    ))
    add("Card.GetEffectCount", sig(
        "colon",
        "返回该卡上匹配 `code` 的生效效果数量。",
        {"code"},
        {"---@param code EffectCode 效果代码。"},
        {"---@return integer count"}
    ))
    add("Card.RegisterFlagEffect", sig(
        "colon",
        "为该卡注册不可无效的标记效果（`FlagEffect`）并返回新效果对象。",
        {"code", "reset", "property", "count", "label", "description"},
        {
            "---@param code integer 标记代码（低位掩码 `MAX_CARD_ID`）。",
            "---@param reset Reset 重置条件（含 `RESET_PHASE` 且未指定回合归属时自动补全双方回合）。",
            "---@param property EffectProperty 属性标志（自动附加 `EFFECT_FLAG_CANNOT_DISABLE`）。",
            "---@param count integer 重置计数（`0` 自动修正为 `1`）。",
            "---@param label? integer 初始整型标签（默认 `0`）。",
            "---@param description? integer 客户端显示提示描述 ID（默认 `0`）。"
        },
        {"---@return Effect flagEffect 注册好的标记效果。"}
    ))
    add("Card.GetFlagEffect", sig("colon", "返回该卡上指定标记效果的数量。", {"code"}, {"---@param code integer 标记代码。"}, {"---@return integer count"}))
    add("Card.ResetFlagEffect", sig("colon", "移除该卡上所有指定 `code` 的标记效果。", {"code"}, {"---@param code integer 标记代码。"}, {"---@return nil"}))
    add("Card.SetFlagEffectLabel", sig(
        "colon",
        "修改该卡上找到的第一个指定 `code` 标记效果的整型标签。",
        {"code", "label"},
        {"---@param code integer 标记代码。", "---@param label integer 新的标签值。"},
        {"---@return boolean updated 找到并修改返回 `true`，未找到返回 `false`。"}
    ))
    add("Card.GetFlagEffectLabel", sig(
        "colon",
        "多值返回该卡上所有指定 `code` 标记效果的首个标签（无标签项返回 `0`）；无该标记效果时返回 `nil`。",
        {"code"},
        {"---@param code integer 标记代码。"},
        {"---@return integer ... 标签值列表；不存在该标记效果时返回 `nil`。"}
    ))

    add("Card.CreateRelation", sig("colon", "建立此卡指向 `rcard` 的卡片关联。", {"rcard", "reset"}, {"---@param rcard Card 目标卡片。", "---@param reset Reset 解除关联的重置标志。"}, {"---@return nil"}))
    add("Card.ReleaseRelation", sig("colon", "解除此卡指向 `rcard` 的卡片关联。", {"rcard"}, {"---@param rcard Card 目标卡片。"}, {"---@return nil"}))
    add("Card.CreateEffectRelation", sig("colon", "建立此卡与效果 `effect` 的关联。", {"effect"}, {"---@param effect Effect 效果对象。"}, {"---@return nil"}))
    add("Card.ReleaseEffectRelation", sig("colon", "解除此卡与效果 `effect` 的关联。", {"effect"}, {"---@param effect Effect 效果对象。"}, {"---@return nil"}))
    add("Card.ClearEffectRelation", sig("colon", "清空此卡的所有效果关联。", {}, {}, {"---@return nil"}))
    add("Card.IsRelateToEffect", sig("colon", "判定此卡是否仍与效果 `effect` 保持关联。", {"effect"}, {"---@param effect Effect 效果对象。"}, {"---@return boolean related"}))
    add("Card.IsRelateToChain", sig(
        "colon",
        "判定此卡是否仍与指定连锁项保持关联（省略或 `0` 取当前连锁栈顶）。",
        {"chainIndex"},
        {"---@param chainIndex? integer 连锁序号（1-based；省略、`0` 或越界取当前连锁栈顶）。"},
        {"---@return boolean related"}
    ))
    add("Card.IsRelateToCard", sig("colon", "判定此卡是否与 `rcard` 存在卡片关联。", {"rcard"}, {"---@param rcard Card 目标卡片。"}, {"---@return boolean related"}))
    add("Card.IsRelateToBattle", sig("colon", "判定此卡是否仍与当前战斗关联（比对 `fieldid_r`）。", {}, {}, {"---@return boolean related"}))

    add("Card.CopyEffect", sig(
        "colon",
        "复制卡号 `code` 的脚本效果到此卡，返回复制记录 ID。",
        {"code", "reset", "count"},
        {
            "---@param code CardCode 要复制效果的源卡号。",
            "---@param reset Reset 重置条件。",
            "---@param count? integer 重置计数（默认 `1`）。"
        },
        {"---@return integer copyId 复制 ID（可用于 `ResetEffect(copyId, RESET_COPY)`）。"}
    ))
    add("Card.ReplaceEffect", sig(
        "colon",
        "用卡号 `code` 的脚本效果替换此卡原有效果，返回替换记录 ID。",
        {"code", "reset", "count"},
        {
            "---@param code CardCode 替换源卡号。",
            "---@param reset Reset 重置条件。",
            "---@param count? integer 重置计数（默认 `1`）。"
        },
        {"---@return integer replaceId"}
    ))
    add("Card.EnableReviveLimit", sig("colon", "为卡片附加不可无效、不可复制的苏生限制（`EFFECT_REVIVE_LIMIT`）。", {}, {}, {"---@return nil"}))
    add("Card.CompleteProcedure", sig("colon", "标记卡片已完成正规召唤手续（置位 `STATUS_PROC_COMPLETE`）。", {}, {}, {"---@return nil"}))

    -- =========================================================================
    -- 破坏、召唤、移动、解放、丢弃、攻击与控制权能力判定
    -- =========================================================================
    add_noarg_bool({
        {"IsDisabled", "判定卡片是否带有 `STATUS_DISABLED` 无效状态。"},
        {"IsSummonableCard", "判定卡片自身属性是否为**可通常召唤的怪兽卡**（注意：检查当前能否执行通召请用 `Card:IsSummonable`）。"},
        {"IsSpecialSummonableCard", "判定卡片自身属性是否为**可特殊召唤的卡片**（区别于 `Card:IsSpecialSummonable`）。"},
        {"IsAbleToGrave", "判定当前 `reason_player` 能否将此卡送去墓地。"},
        {"IsAbleToDeck", "判定当前 `reason_player` 能否将此卡送回卡组。"},
        {"IsAbleToExtra", "判定当前 `reason_player` 能否将此卡送回额外卡组。"},
        {"IsAbleToHandAsCost", "判定能否作为 Cost 返回手牌。"},
        {"IsAbleToGraveAsCost", "判定能否作为 Cost 送去墓地。"},
        {"IsAbleToDeckAsCost", "判定能否作为 Cost 返回卡组。"},
        {"IsAbleToExtraAsCost", "判定能否作为 Cost 返回额外卡组。"},
        {"IsAbleToDeckOrExtraAsCost", "判定能否作为 Cost 返回卡组或额外卡组（按卡片种类自动分派）。"},
        {"IsReleasableByEffect", "判定能否被当前 `reason_effect` 因效果解放。"},
        {"IsAttackable", "判定此卡当前是否具备攻击能力。"},
        {"IsFaceup", "判定当前是否为表侧表示（`POS_FACEUP`）。"},
        {"IsFaceupEx", "判定是否为表侧表示，或位于手牌/墓地/卡组任一区域。"},
        {"IsAttackPos", "判定当前是否为攻击表示（`POS_ATTACK`）。"},
        {"IsFacedown", "判定当前是否为里侧表示（`POS_FACEDOWN`）。"},
        {"IsDefensePos", "判定当前是否为守备表示（`POS_DEFENSE`）。"},
        {"IsOnField", "判定是否位于场上（且未被视为不在场）。"},
        {"IsHasLevel", "判定是否为拥有等级的怪兽（非超量、非连接且无 `STATUS_NO_LEVEL`）。"},
        {"IsHasDefense", "判定是否为拥有守备力的怪兽（非连接怪兽）。"},
        {"IsPublic", "判定是否公开（底层检查 `POS_FACEUP`，常用于手牌公开判断）。"},
        {"IsForbidden", "判定是否带有 `STATUS_FORBIDDEN` 状态。"},
        {"IsAbleToChangeControler", "仅检查卡片自身是否允许转移控制权（**不检查**对方怪兽区空位；含空位检查请用 `Card:IsControlerCanBeChanged`）。"},
        {"IsCanChangePosition", "判定能否因效果改变表示形式。"},
        {"IsCanTurnSet", "判定能否变为里侧表示。"}
    })

    add("Card.IsDestructable", sig(
        "colon",
        "判定卡片是否可被破坏；传入 `effect` 时按该效果检查（若传参则不得显式传 `nil`）。",
        {"effect"},
        {"---@param effect? Effect 可选的效果对象（省略时检查一般可破坏性）。"},
        {"---@return boolean destructable"}
    ))
    add("Card.IsFusionSummonableCard", sig(
        "colon",
        "判定卡片本身是否为可按指定方式融合召唤的融合怪兽卡。",
        {"summonType"},
        {"---@param summonType? SummonType 召唤类型（默认 `0`）。"},
        {"---@return boolean result"}
    ))
    add("Card.IsMSetable", sig(
        "colon",
        "判定当前 `reason_player` 能否将此卡通常盖放到怪兽区（伤害步骤直接返回 `false`）。",
        {"ignoreCount", "effect", "minTribute", "zone"},
        {
            "---@param ignoreCount boolean 是否忽略通常召唤次数限制。",
            "---@param effect Effect|nil 赋予额外通常盖放的效果（必填，无则显式传 `nil`）。",
            "---@param minTribute? integer 最少解放数（默认 `0`）。",
            "---@param zone? Zone 允许放置的怪兽区位掩码（默认 `0x1f`）。"
        },
        {"---@return boolean setable"}
    ))
    add("Card.IsSSetable", sig(
        "colon",
        "判定当前 `reason_player` 能否将此卡盖放到魔陷区。",
        {"ignoreField"},
        {"---@param ignoreField? boolean 是否忽略魔陷区空位限制（默认 `false`）。"},
        {"---@return boolean setable"}
    ))
    add("Card.IsSpecialSummonable", sig(
        "colon",
        "判定当前 `reason_player` 能否按规则手续特殊召唤此卡（如同调/超量/连接/规则特召）。",
        {"summonType"},
        {"---@param summonType? SummonType 召唤类型限制（默认 `0`）。"},
        {"---@return boolean summonable"}
    ))
    add("Card.IsSynchroSummonable", sig(
        "colon",
        "判定能否在限定调整/素材组/素材数范围内同调召唤此卡（非同调怪兽或伤害步骤返回 `false`）。",
        {"tuner", "materials", "minc", "maxc"},
        {
            "---@param tuner Card|nil 必选调整怪兽（必填，不限定则传 `nil`）。",
            "---@param materials? Group|nil 限定候选素材集合（默认 `nil`）。",
            "---@param minc? integer 最少素材数（默认 `0`）。",
            "---@param maxc? integer 最多素材数（默认 `0`）。"
        },
        {"---@return boolean summonable"}
    ))
    add("Card.IsXyzSummonable", sig(
        "colon",
        "判定能否在限定素材组/素材数范围内超量召唤此卡（非超量怪兽或伤害步骤返回 `false`）。",
        {"materials", "minc", "maxc"},
        {
            "---@param materials Group|nil 限定候选素材集合（必填，不限定则传 `nil`）。",
            "---@param minc? integer 最少素材数（默认 `0`）。",
            "---@param maxc? integer 最多素材数（默认 `0`）。"
        },
        {"---@return boolean summonable"}
    ))
    add("Card.IsLinkSummonable", sig(
        "colon",
        "判定能否在限定素材组/必选卡/素材数范围内连接召唤此卡（非连接怪兽或伤害步骤返回 `false`）。",
        {"materials", "mustCard", "minc", "maxc"},
        {
            "---@param materials Group|nil 限定候选素材集合（必填，不限定则传 `nil`）。",
            "---@param mustCard? Card|nil 必选素材卡（默认 `nil`）。",
            "---@param minc? integer 最少素材数（默认 `0`）。",
            "---@param maxc? integer 最多素材数（默认 `0`）。"
        },
        {"---@return boolean summonable"}
    ))
    add("Card.IsSummonable", sig(
        "colon",
        "判定当前 `reason_player` 能否立即通常召唤此卡（伤害步骤返回 `false`；注意与 `Card:IsSummonableCard` 区分）。",
        {"ignoreCount", "effect", "minTribute", "zone"},
        {
            "---@param ignoreCount boolean 是否忽略通常召唤次数限制。",
            "---@param effect Effect|nil 赋予额外通常召唤的效果（必填，无则显式传 `nil`）。",
            "---@param minTribute? integer 最少解放数（默认 `0`）。",
            "---@param zone? Zone 允许召唤到的怪兽区位掩码（默认 `0x1f`）。"
        },
        {"---@return boolean summonable"}
    ))
    add("Card.IsCanBeSpecialSummoned", sig(
        "colon",
        "判定此卡能否由效果 `effect` 按指定参数特殊召唤。",
        {"effect", "summonType", "sumPlayer", "nocheck", "nolimit", "position", "toPlayer", "zone"},
        {
            "---@param effect Effect 发动特殊召唤的效果。",
            "---@param summonType SummonType 召唤类型（如 `0`、`SUMMON_TYPE_FUSION`）。",
            "---@param sumPlayer Player 执行特殊召唤的玩家。",
            "---@param nocheck boolean 是否无视召唤条件。",
            "---@param nolimit boolean 是否无视苏生限制。",
            "---@param position? Position 表示形式（默认 `POS_FACEUP`）。",
            "---@param toPlayer? Player 召唤到哪方场上（默认 `sumPlayer`）。",
            "---@param zone? Zone 允许召唤到的格子掩码（默认 `0xff`）。"
        },
        {"---@return boolean summonable"}
    ))
    add("Card.IsCanBePlacedOnField", sig(
        "colon",
        "判定此卡能否直接放置到场上（检查非 `STATUS_FORBIDDEN` 且不违反唯一限制）。",
        {"toPlayer", "toLocation"},
        {
            "---@param toPlayer? Player 目标玩家（默认 `reason_player`）。",
            "---@param toLocation? Location 目标区域（默认 `LOCATION_SZONE`）。"
        },
        {"---@return boolean placeable"}
    ))
    add("Card.IsAbleToHand", sig(
        "colon",
        "判定能否将此卡加入手牌。",
        {"player"},
        {"---@param player? Player 操作玩家（默认 `reason_player`）。"},
        {"---@return boolean able"}
    ))
    add("Card.IsAbleToRemove", sig(
        "colon",
        "判定能否以指定表示形式与原因除外此卡。",
        {"player", "position", "reason"},
        {
            "---@param player? Player 操作玩家（默认 `reason_player`）。",
            "---@param position? Position 除外表示形式（默认 `POS_FACEUP`）。",
            "---@param reason? Reason 除外原因（默认 `REASON_EFFECT`）。"
        },
        {"---@return boolean able"}
    ))
    add("Card.IsAbleToRemoveAsCost", sig(
        "colon",
        "判定能否作为 Cost 以指定表示形式除外此卡。",
        {"position"},
        {"---@param position? Position 除外表示形式（默认 `POS_FACEUP`）。"},
        {"---@return boolean able"}
    ))
    add("Card.IsReleasable", sig(
        "colon",
        "判定能否因非召唤原因解放此卡。",
        {"reason"},
        {"---@param reason? Reason 解放原因（默认 `REASON_COST`）。"},
        {"---@return boolean releasable"}
    ))
    add("Card.IsDiscardable", sig(
        "colon",
        "判定手牌中的此卡能否被丢弃（`REASON_COST` 时额外检查 `EFFECT_CANNOT_USE_AS_COST`）。",
        {"reason"},
        {"---@param reason? Reason 丢弃原因（默认 `REASON_COST`）。"},
        {"---@return boolean discardable"}
    ))
    add("Card.IsChainAttackable", sig(
        "colon",
        "判定当前战斗攻击者（`core.attacker`）能否继续连续攻击。",
        {"ac", "monsterOnly"},
        {
            "---@param ac? integer 允许攻击次数阈值（默认 `2`；`0` 表示不限次数）。",
            "---@param monsterOnly? boolean 是否限定仅对怪兽连击（默认 `false`）。"
        },
        {"---@return boolean chainAttackable"}
    ))
    add("Card.IsControlerCanBeChanged", sig(
        "colon",
        "检查此卡的控制权能否实际转移给对方（含对方怪兽区空位与区域掩码检查）。",
        {"ignoreMZone", "zone"},
        {
            "---@param ignoreMZone? boolean 是否忽略怪兽区空位检查（默认 `false`）。",
            "---@param zone? Zone 目标怪兽区位掩码（默认 `0xff`）。"
        },
        {"---@return boolean changeable"}
    ))

    -- =========================================================================
    -- 位置/表示/控制者/数值上下限判定
    -- =========================================================================
    add_single_int_pred({
        {"IsPosition", "判定当前表示形式是否匹配指定位掩码。", "表示形式位掩码（`POS_*`）。"},
        {"IsPreviousPosition", "判定上一个表示形式是否匹配指定位掩码。", "表示形式位掩码（`POS_*`）。"}
    }, "position", "Position")
    add_single_int_pred({
        {"IsControler", "判定当前控制者是否等于 `player`。", "玩家编号。"},
        {"IsPreviousControler", "判定上一个控制者是否等于 `player`。", "玩家编号。"}
    }, "player", "Player")
    add_single_int_pred({
        {"IsLocation", "判定当前位置是否匹配 `location`（被视为不在场时恒返回 `false`；支持区分场地/灵摆区）。", "区域位掩码（`LOCATION_*`）。"},
        {"IsPreviousLocation", "判定上一个位置是否匹配 `location`。", "区域位掩码（`LOCATION_*`）。"}
    }, "location", "Location")
    add_single_int_pred({
        {"IsLevelBelow", "判定当前等级 `> 0` 且 `<= level`。", "等级上限。"},
        {"IsLevelAbove", "判定当前等级 `> 0` 且 `>= level`。", "等级下限。"},
        {"IsRankBelow", "判定当前阶级 `> 0` 且 `<= rank`。", "阶级上限。"},
        {"IsRankAbove", "判定当前阶级 `> 0` 且 `>= rank`。", "阶级下限。"},
        {"IsLinkBelow", "判定当前连接值 `> 0` 且 `<= link`。", "连接值上限。"},
        {"IsLinkAbove", "判定当前连接值 `> 0` 且 `>= link`。", "连接值下限。"},
        {"IsAttackBelow", "判定为怪兽且当前攻击力 `>= 0` 且 `<= atk`（`?` 攻 `-2` 返回 `false`）。", "攻击力上限。"},
        {"IsAttackAbove", "判定为怪兽且当前攻击力 `>= atk`。", "攻击力下限。"},
        {"IsDefenseBelow", "判定为非连接怪兽且当前守备力 `>= 0` 且 `<= def`。", "守备力上限。"},
        {"IsDefenseAbove", "判定为非连接怪兽且当前守备力 `>= def`。", "守备力下限。"}
    }, "value", "integer")

    -- =========================================================================
    -- 指示物、召唤素材资格、免疫/取对象、陷阱怪兽、唯一性与其他工具
    -- =========================================================================
    add("Card.AddCounter", sig(
        "colon",
        "向此卡放置指定数量指示物（若卡片不受当前 `reason_effect` 影响则直接返回 `false`）。",
        {"counterType", "count", "singly"},
        {
            "---@param counterType CounterType 指示物类型编码。",
            "---@param count integer 放置数量。",
            "---@param singly? boolean 是否允许按上限逐个放置至满额（默认 `false`）。"
        },
        {"---@return boolean added"}
    ))
    add("Card.RemoveCounter", sig(
        "colon",
        "[可能协程挂起] 从此卡取除指示物；`counterType == 0` 时同步清空该卡所有指示物，否则挂起处理后返回是否成功。",
        {"player", "counterType", "count", "reason"},
        {
            "---@param player Player 执行取除的玩家编号。",
            "---@param counterType CounterType 指示物类型（传 `0` 清空所有指示物）。",
            "---@param count integer 取除数量。",
            "---@param reason Reason 原因位掩码。"
        },
        {"---@return boolean removed"}
    ))
    add("Card.GetCounter", sig(
        "colon",
        "取得该卡上的指示物数量；注意传 `0` 时返回的是指示物的**种类数**而非总数。",
        {"counterType"},
        {"---@param counterType CounterType 指示物类型（传 `0` 返回不同指示物的种类数）。"},
        {"---@return integer count"}
    ))
    add("Card.EnableCounterPermit", sig(
        "colon",
        "允许此卡放置指定类型指示物（注册 `EFFECT_COUNTER_PERMIT | counterType`）。",
        {"counterType", "range", "condition"},
        {
            "---@param counterType CounterType 指示物类型编码。",
            "---@param range? Location 生效区域（默认：怪兽为 `LOCATION_MZONE`，非怪兽为 `LOCATION_SZONE | LOCATION_FZONE`）。",
            "---@param condition? ConditionFunction 可选条件回调。"
        },
        {"---@return nil"}
    ))
    add("Card.SetCounterLimit", sig(
        "colon",
        "为该卡设置某类指示物的持有数量上限。",
        {"counterType", "limit"},
        {"---@param counterType CounterType 指示物类型编码。", "---@param limit integer 上限数量。"},
        {"---@return nil"}
    ))
    add("Card.IsCanAddCounter", sig(
        "colon",
        "判定此卡能否放置指定种类和数量的指示物。",
        {"counterType", "count", "singly", "location"},
        {
            "---@param counterType CounterType 指示物类型编码。",
            "---@param count integer 数量。",
            "---@param singly? boolean 是否允许按上限逐个放置（默认 `false`）。",
            "---@param location? Location 预检的目标区域（默认 `0` 使用当前区域）。"
        },
        {"---@return boolean canAdd"}
    ))
    add("Card.IsCanRemoveCounter", sig(
        "colon",
        "判定玩家能否从此卡取除指定种类与数量的指示物。",
        {"player", "counterType", "count", "reason"},
        {
            "---@param player Player 玩家编号（0 或 1）。",
            "---@param counterType CounterType 指示物类型编码。",
            "---@param count integer 取除数量。",
            "---@param reason Reason 原因位掩码。"
        },
        {"---@return boolean|nil canRemove `player` 非 0/1 时无返回值。"}
    ))
    add("Card.IsCanHaveCounter", sig(
        "colon",
        "判定此卡是否允许持有指定种类的指示物。",
        {"counterType"},
        {"---@param counterType CounterType 指示物类型编码。"},
        {"---@return boolean allowed"}
    ))
    add("Card.IsCanOverlay", sig(
        "colon",
        "判定此卡能否作为超量素材重叠到超量怪兽下方。",
        {"player"},
        {"---@param player? Player 操作玩家（默认 `reason_player`）。"},
        {"---@return boolean allowed"}
    ))
    add("Card.IsCanBeFusionMaterial", sig(
        "colon",
        "判定此卡能否作为融合怪兽 `fusionCard` 的融合素材。",
        {"fusionCard", "summonType"},
        {
            "---@param fusionCard? Card|nil 目标融合怪兽（默认 `nil`）。",
            "---@param summonType? SummonType 召唤类型（默认 `SUMMON_TYPE_FUSION`）。"
        },
        {"---@return boolean allowed"}
    ))
    add("Card.IsCanBeSynchroMaterial", sig(
        "colon",
        "判定此卡能否作为同调怪兽 `synchroCard`（及指定调整 `tuner`）的同调素材；注意传入参数时不可显式传 `nil`。",
        {"synchroCard", "tuner"},
        {
            "---@param synchroCard? Card 目标同调怪兽（可选；传参时必须为 `Card`）。",
            "---@param tuner? Card 搭配的调整怪兽（可选；传参时必须为 `Card`）。"
        },
        {"---@return boolean allowed"}
    ))
    add("Card.IsCanBeRitualMaterial", sig(
        "colon",
        "判定此卡能否作为仪式怪兽 `ritualCard` 的仪式素材（参数必传，不限定时传 `nil`）。",
        {"ritualCard"},
        {"---@param ritualCard Card|nil 目标仪式怪兽（必填，不限定则显式传 `nil`）。"},
        {"---@return boolean allowed"}
    ))
    add("Card.IsCanBeXyzMaterial", sig(
        "colon",
        "判定此卡能否作为超量怪兽 `xyzCard` 的超量素材（参数必传，不限定时传 `nil`）。",
        {"xyzCard"},
        {"---@param xyzCard Card|nil 目标超量怪兽（必填，不限定则显式传 `nil`）。"},
        {"---@return boolean allowed"}
    ))
    add("Card.IsCanBeLinkMaterial", sig(
        "colon",
        "判定此卡能否作为连接怪兽 `linkCard` 的连接素材（参数必传，不限定时传 `nil`）。",
        {"linkCard"},
        {"---@param linkCard Card|nil 目标连接怪兽（必填，不限定则显式传 `nil`）。"},
        {"---@return boolean allowed"}
    ))
    add("Card.CheckFusionMaterial", sig(
        "colon",
        "检查给定素材集合中是否满足该融合怪兽的融合素材要求。",
        {"materials", "mustCard", "chkf", "notMaterial"},
        {
            "---@param materials? Group|nil 候选素材集合（默认 `nil`）。",
            "---@param mustCard? Card|nil 必选素材卡（默认 `nil`）。",
            "---@param chkf? Player 检查玩家标志（默认 `PLAYER_NONE`）。",
            "---@param notMaterial? boolean 是否按非融合素材规则检查（默认 `false`）。"
        },
        {"---@return boolean valid"}
    ))
    add("Card.CheckFusionSubstitute", sig(
        "colon",
        "判定此卡能否作为 `fusionCard` 的融合素材代替怪兽。",
        {"fusionCard"},
        {"---@param fusionCard Card 目标融合怪兽。"},
        {"---@return boolean substitute"}
    ))
    add("Card.IsImmuneToEffect", sig(
        "colon",
        "判定此卡是否不受效果 `effect` 影响。",
        {"effect"},
        {"---@param effect Effect 效果对象。"},
        {"---@return boolean immune"}
    ))
    add("Card.IsCanBeDisabledByEffect", sig(
        "colon",
        "判定此卡能否被效果 `effect` 无效化。",
        {"effect", "isMonsterEffect"},
        {
            "---@param effect Effect 无效来源效果。",
            "---@param isMonsterEffect? boolean 是否按怪兽效果无效判定（默认 `true`）。"
        },
        {"---@return boolean disablable"}
    ))
    add("Card.IsCanBeEffectTarget", sig(
        "colon",
        "判定此卡能否成为效果 `effect` 的取对象目标；传参时必须为 `Effect`（不可显式传 `nil`）。",
        {"effect"},
        {"---@param effect? Effect 效果对象（省略时取当前 `reason_effect`）。"},
        {"---@return boolean targetable"}
    ))
    add("Card.IsCanBeBattleTarget", sig(
        "colon",
        "判定此卡能否成为攻击怪兽 `attacker` 的攻击对象。",
        {"attacker"},
        {"---@param attacker Card 攻击宣言怪兽。"},
        {"---@return boolean targetable"}
    ))
    add("Card.AddMonsterAttribute", sig(
        "colon",
        "陷阱怪兽辅助方法：在当前连锁期间赋予 `EFFECT_PRE_MONSTER` 及非零的种类/属性/种族/等级/攻守。",
        {"cardType", "attribute", "race", "level", "atk", "def"},
        {
            "---@param cardType CardType 怪兽种类位掩码。",
            "---@param attribute? Attribute 属性（默认 `0` 不设置）。",
            "---@param race? Race 种族（默认 `0` 不设置）。",
            "---@param level? integer 等级（默认 `0` 不设置）。",
            "---@param atk? integer 攻击力（默认 `0` 不设置）。",
            "---@param def? integer 守备力（默认 `0` 不设置）。"
        },
        {"---@return nil"}
    ))
    add("Card.CancelToGrave", sig(
        "colon",
        "控制魔陷卡发动结算后是否取消自动送墓（`true` 留在场上，`false` 强制标记送墓）。",
        {"cancel"},
        {"---@param cancel? boolean 是否取消送墓（默认 `true`）。"},
        {"---@return nil"}
    ))
    add("Card.GetTributeRequirement", sig(
        "colon",
        "返回通常召唤此卡所需的最小与最大解放数（双返回值）。",
        {}, {},
        {"---@return integer minTribute 最少祭品数。", "---@return integer maxTribute 最多祭品数。"}
    ))
    add("Card.GetAttackableTarget", sig(
        "colon",
        "返回此卡当前可攻击的对象怪兽集合，以及是否可以直接攻击（双返回值）。",
        {}, {},
        {"---@return Group targets 可攻击的对象怪兽集合。", "---@return boolean canDirectAttack 是否允许直接攻击。"}
    ))
    add("Card.SetHint", sig(
        "colon",
        "向客户端发送单卡提示（`MSG_CARD_HINT`；`hintType >= CHINT_DESC_ADD` 时会被忽略）。",
        {"hintType", "value"},
        {
            "---@param hintType CardHint 卡片提示类型（`CHINT_*`）。",
            "---@param value integer 提示值。"
        },
        {"---@return nil"}
    ))
    add("Card.ReverseInDeck", sig(
        "colon",
        "将卡组中的此卡翻转为表侧守备表示（在卡组顶时同步通知客户端 `MSG_DECK_TOP`）。",
        {}, {},
        {"---@return nil"}
    ))
    add("Card.SetUniqueOnField", sig(
        "colon",
        "设置此卡在场上只能表侧存在 1 张的唯一性限制。",
        {"selfFlag", "opponentFlag", "codeOrFunc", "location"},
        {
            "---@param selfFlag integer 己方场上是否受限（`0` 或 `1`）。",
            "---@param opponentFlag integer 对方场上是否受限（`0` 或 `1`）。",
            "---@param codeOrFunc integer|FilterFunction 唯一标识码或冲突判定函数。",
            "---@param location? Location 限制生效的场上区域（默认 `LOCATION_ONFIELD`）。"
        },
        {"---@return nil"}
    ))
    add("Card.CheckUniqueOnField", sig(
        "colon",
        "检查此卡放置在指定玩家指定区域时是否**不发生**唯一性冲突（无冲突返回 `true`）。",
        {"checkPlayer", "checkLocation", "ignoreCard"},
        {
            "---@param checkPlayer Player 检查玩家编号。",
            "---@param checkLocation? Location 目标区域（默认 `LOCATION_ONFIELD`）。",
            "---@param ignoreCard? Card|nil 忽略检查的一张卡（默认 `nil`）。"
        },
        {"---@return boolean noConflict 无冲突返回 `true`，发生冲突返回 `false`。"}
    ))
    add("Card.ResetNegateEffect", sig(
        "colon",
        "按传入的 0..N 个卡号逐个重置该卡上关联的无效效果（`RESET_CARD`）。",
        {"..."},
        {"---@param ... CardCode 要重置的卡号列表。"},
        {"---@return nil"}
    ))
    add("Card.AssumeProperty", sig(
        "colon",
        "临时设定卡片的假设属性（`ASSUME_*`），会在顶层脚本调用结束后自动恢复。",
        {"assumeType", "assumeValue"},
        {
            "---@param assumeType AssumeType 假设属性类型（`ASSUME_*`）。",
            "---@param assumeValue integer 假设属性值。"
        },
        {"---@return nil"}
    ))
    add("Card.SetSPSummonOnce", sig(
        "colon",
        "为卡片注册“1 回合只能特殊召唤 1 次”限制并启用 `GLOBALFLAG_SPSUMMON_ONCE`（复制效果期间忽略）。",
        {"code"},
        {"---@param code CardCode 共享特召次数限制的卡号。"},
        {"---@return nil"}
    ))
end

return { register = register }
