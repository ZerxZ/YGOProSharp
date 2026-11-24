-- Duel 库全部 229 个 native Lua API 精确签名（对照 ygopro-core/libduel.cpp）

function register(add, sig)
    local function add_player_can_card_overload(name, desc1, desc2)
        add("Duel." .. name, sig(
            "dot",
            desc1 .. "（1 参查玩家整体权限；2 参查对指定卡片的权限）。",
            {"player", "card"},
            {
                "---@param player Player 玩家编号（0 或 1）。",
                "---@param card? Card 可选：指定要检查的卡片。"
            },
            {"---@return boolean allowed"},
            nil,
            {
                "---@overload fun(player: Player): boolean",
                "---@overload fun(player: Player, card: Card): boolean"
            }
        ))
    end

    local function add_player_can_card_reason_overload(name, desc, defaultReason)
        add("Duel." .. name, sig(
            "dot",
            desc .. "（1 参查玩家整体权限；2~3 参查对指定卡片的权限）。",
            {"player", "card", "reason"},
            {
                "---@param player Player 玩家编号（0 或 1）。",
                "---@param card? Card 可选：指定卡片。",
                "---@param reason? Reason 可选：原因位掩码（默认 `" .. defaultReason .. "`）。"
            },
            {"---@return boolean allowed"},
            nil,
            {
                "---@overload fun(player: Player): boolean",
                "---@overload fun(player: Player, card: Card, reason?: Reason): boolean"
            }
        ))
    end

    -- =========================================================================
    -- 全局标志、LP、回合、抽卡数、效果注册与标记效果
    -- =========================================================================
    add("Duel.EnableGlobalFlag", sig("dot", "按位或启用全局规则标志（`GLOBALFLAG_*`）。", {"flag"}, {"---@param flag GlobalFlag 全局标志位掩码。"}, {"---@return nil"}))
    add("Duel.GetLP", sig("dot", "取得玩家当前 LP（`player` 非 0/1 无返回值）。", {"player"}, {"---@param player Player 玩家编号（0 或 1）。"}, {"---@return integer|nil lp"}))
    add("Duel.SetLP", sig("dot", "直接设置玩家 LP（负值钳位为 `0`）并向客户端广播 `MSG_LPUPDATE`。", {"player", "lp"}, {"---@param player Player 玩家编号。", "---@param lp integer 目标 LP。"}, {"---@return nil"}))
    add("Duel.IsTurnPlayer", sig("dot", "判定 `player` 是否为当前回合玩家。", {"player"}, {"---@param player Player 玩家编号。"}, {"---@return boolean isTurnPlayer"}))
    add("Duel.GetTurnPlayer", sig("dot", "取得当前回合玩家编号（`0` 或 `1`）。", {}, {}, {"---@return Player player"}))
    add("Duel.GetTurnCount", sig(
        "dot",
        "取得回合数：无参返回对局总回合数；传入 `player` 返回该玩家已进行的回合数。",
        {"player"},
        {"---@param player? Player 可选：玩家编号（0 或 1）。"},
        {"---@return integer|nil turnCount"}
    ))
    add("Duel.GetDrawCount", sig("dot", "取得指定玩家抽卡阶段的通常抽卡张数。", {"player"}, {"---@param player Player 玩家编号（0 或 1）。"}, {"---@return integer|nil drawCount"}))
    add("Duel.RegisterEffect", sig("dot", "将效果注册为作用于指定玩家的场地/玩家效果。", {"effect", "player"}, {"---@param effect Effect 效果对象。", "---@param player Player 目标玩家编号（0 或 1）。"}, {"---@return nil"}))
    add("Duel.RegisterFlagEffect", sig(
        "dot",
        "为指定玩家注册标记效果（`FlagEffect`）并返回该效果对象。",
        {"player", "code", "reset", "property", "count", "label"},
        {
            "---@param player Player 玩家编号（0 或 1）。",
            "---@param code integer 标记代码。",
            "---@param reset Reset 重置条件（含 `RESET_PHASE` 且未指定回合归属时自动补全双方回合）。",
            "---@param property EffectProperty 属性标志。",
            "---@param count integer 重置计数（`0` 自动修正为 `1`）。",
            "---@param label? integer 初始标签值（默认 `0`）。"
        },
        {"---@return Effect|nil flagEffect `player` 非 0/1 无返回值。"}
    ))
    add("Duel.GetFlagEffect", sig("dot", "取得玩家身上匹配 `code` 的标记效果数量。", {"player", "code"}, {"---@param player Player 玩家编号。", "---@param code integer 标记代码。"}, {"---@return integer|nil count"}))
    add("Duel.ResetFlagEffect", sig("dot", "移除玩家身上所有匹配 `code` 的标记效果。", {"player", "code"}, {"---@param player Player 玩家编号。", "---@param code integer 标记代码。"}, {"---@return nil"}))
    add("Duel.SetFlagEffectLabel", sig("dot", "修改玩家首个匹配 `code` 的标记效果的整型标签。", {"player", "code", "label"}, {"---@param player Player 玩家编号。", "---@param code integer 标记代码。", "---@param label integer 新的标签值。"}, {"---@return boolean|nil updated"}))
    add("Duel.GetFlagEffectLabel", sig(
        "dot",
        "多值返回玩家身上所有匹配 `code` 的标记效果的首个标签；无匹配返回 `nil`。",
        {"player", "code"},
        {"---@param player Player 玩家编号。", "---@param code integer 标记代码。"},
        {"---@return integer ... 标签值列表；无匹配返回 `nil`。"}
    ))

    -- =========================================================================
    -- 移动、破坏、除外、召唤、盖放与特召
    -- =========================================================================
    add("Duel.Destroy", sig(
        "dot",
        "[协程挂起] 破坏单卡或集合，可选指定破坏后的去向与原因玩家，返回实际破坏张数。",
        {"targets", "reason", "dest", "reasonPlayer"},
        {
            "---@param targets Card|Group 要破坏的单卡或集合。",
            "---@param reason Reason 破坏原因（如 `REASON_EFFECT`）。",
            "---@param dest? Location 破坏后移动到的区域（默认 `LOCATION_GRAVE`）。",
            "---@param reasonPlayer? Player 原因玩家（默认当前 `reason_player`）。"
        },
        {"---@return integer count 实际破坏张数。"}
    ))
    add("Duel.Remove", sig(
        "dot",
        "[协程挂起] 以指定表示形式除外单卡或集合，返回实际除外张数。",
        {"targets", "position", "reason", "reasonPlayer"},
        {
            "---@param targets Card|Group 要除外的单卡或集合。",
            "---@param position Position 除外表示形式（`POS_FACEUP` 或 `POS_FACEDOWN`）。",
            "---@param reason Reason 除外原因。",
            "---@param reasonPlayer? Player 原因玩家（默认当前 `reason_player`）。"
        },
        {"---@return integer count 实际除外张数。"}
    ))
    add("Duel.SendtoGrave", sig(
        "dot",
        "[协程挂起] 将单卡或集合表侧送去墓地，返回实际送墓张数。",
        {"targets", "reason", "reasonPlayer"},
        {
            "---@param targets Card|Group 要送墓的单卡或集合。",
            "---@param reason Reason 送墓原因。",
            "---@param reasonPlayer? Player 原因玩家（默认当前 `reason_player`）。"
        },
        {"---@return integer count 实际送墓张数。"}
    ))
    add("Duel.Summon", sig(
        "dot",
        "[协程挂起] 让玩家通常召唤怪兽 `card`（伤害步骤直接忽略；连锁中会暂存至连锁后处理）。",
        {"player", "card", "ignoreCount", "effect", "minTribute", "zone"},
        {
            "---@param player Player 执行通常召唤的玩家。",
            "---@param card Card 要通常召唤的怪兽。",
            "---@param ignoreCount boolean 是否忽略通常召唤次数限制。",
            "---@param effect Effect|nil 赋予额外通常召唤的效果（必填，无则传 `nil`）。",
            "---@param minTribute? integer 最少解放数（默认 `0`）。",
            "---@param zone? Zone 允许召唤到的怪兽区位掩码（默认 `0x1f`）。"
        },
        {"---@return nil"}
    ))
    add("Duel.SpecialSummonRule", sig(
        "dot",
        "[协程挂起] 让玩家按规则手续特殊召唤 `card`（伤害步骤忽略）。",
        {"player", "card", "summonType"},
        {
            "---@param player Player 执行特殊召唤的玩家。",
            "---@param card Card 要特殊召唤的怪兽。",
            "---@param summonType? SummonType 召唤类型限制（默认 `0`）。"
        },
        {"---@return nil"}
    ))
    add("Duel.SynchroSummon", sig(
        "dot",
        "[协程挂起] 让玩家同调召唤 `card`，可限定必选调整、候选素材组及素材数范围。",
        {"player", "card", "tuner", "materials", "minc", "maxc"},
        {
            "---@param player Player 执行召唤的玩家。",
            "---@param card Card 目标同调怪兽。",
            "---@param tuner Card|nil 必选调整怪兽（必填，不限定则传 `nil`）。",
            "---@param materials? Group|nil 候选素材集合（默认 `nil`）。",
            "---@param minc? integer 最少素材数（默认 `0`）。",
            "---@param maxc? integer 最多素材数（默认 `0`）。"
        },
        {"---@return nil"}
    ))
    add("Duel.XyzSummon", sig(
        "dot",
        "[协程挂起] 让玩家超量召唤 `card`，可限定候选素材组及素材数范围。",
        {"player", "card", "materials", "minc", "maxc"},
        {
            "---@param player Player 执行召唤的玩家。",
            "---@param card Card 目标超量怪兽。",
            "---@param materials Group|nil 候选素材集合（必填，不限定则传 `nil`）。",
            "---@param minc? integer 最少素材数（默认 `0`）。",
            "---@param maxc? integer 最多素材数（默认 `0`）。"
        },
        {"---@return nil"}
    ))
    add("Duel.LinkSummon", sig(
        "dot",
        "[协程挂起] 让玩家连接召唤 `card`，可限定候选素材组、必选卡及素材数范围。",
        {"player", "card", "materials", "mustCard", "minc", "maxc"},
        {
            "---@param player Player 执行召唤的玩家。",
            "---@param card Card 目标连接怪兽。",
            "---@param materials Group|nil 候选素材集合（必填，不限定则传 `nil`）。",
            "---@param mustCard? Card|nil 必选素材卡（默认 `nil`）。",
            "---@param minc? integer 最少素材数（默认 `0`）。",
            "---@param maxc? integer 最多素材数（默认 `0`）。"
        },
        {"---@return nil"}
    ))
    add("Duel.MSet", sig(
        "dot",
        "[协程挂起] 让玩家通常盖放怪兽 `card`（参数与 `Duel.Summon` 相同，伤害步骤忽略）。",
        {"player", "card", "ignoreCount", "effect", "minTribute", "zone"},
        {
            "---@param player Player 执行通常盖放的玩家。",
            "---@param card Card 要盖放的怪兽。",
            "---@param ignoreCount boolean 是否忽略通常召唤次数限制。",
            "---@param effect Effect|nil 赋予额外通常盖放的效果（必填，无则传 `nil`）。",
            "---@param minTribute? integer 最少解放数（默认 `0`）。",
            "---@param zone? Zone 允许放置的怪兽区位掩码（默认 `0x1f`）。"
        },
        {"---@return nil"}
    ))
    add("Duel.SSet", sig(
        "dot",
        "[协程挂起] 将魔陷卡或集合盖放到 `toPlayer` 场上，返回实际成功盖放张数。",
        {"player", "targets", "toPlayer", "confirm"},
        {
            "---@param player Player 执行盖放的玩家。",
            "---@param targets Card|Group 要盖放的单卡或集合。",
            "---@param toPlayer? Player 盖放到哪方场上（默认 `player`）。",
            "---@param confirm? boolean 是否向对手展示确认（默认 `true`）。"
        },
        {"---@return integer count 实际盖放张数。"}
    ))
    add("Duel.CreateToken", sig(
        "dot",
        "为玩家创建一个指定卡号的衍生物对象（`player` 非 0/1 时返回 `false`）。",
        {"player", "code"},
        {"---@param player Player 衍生物持有者（0 或 1）。", "---@param code CardCode 衍生物卡号。"},
        {"---@return Card|boolean token 成功返回 `Card`，玩家非法返回 `false`。"}
    ))
    add("Duel.SpecialSummon", sig(
        "dot",
        "[协程挂起] 一步特殊召唤单卡或集合到 `targetPlayer` 场上，返回实际成功特召张数。",
        {"targets", "summonType", "sumPlayer", "targetPlayer", "nocheck", "nolimit", "position", "zone"},
        {
            "---@param targets Card|Group 要特殊召唤的单卡或集合。",
            "---@param summonType SummonType 召唤类型（如 `0`、`SUMMON_TYPE_FUSION`）。",
            "---@param sumPlayer Player 执行特殊召唤的玩家。",
            "---@param targetPlayer Player 特殊召唤到哪方场上。",
            "---@param nocheck boolean 是否无视召唤条件。",
            "---@param nolimit boolean 是否无视苏生限制。",
            "---@param position Position 特殊召唤表示形式。",
            "---@param zone? Zone 允许召唤到的格子位掩码（默认 `0xff`）。"
        },
        {"---@return integer count 实际特殊召唤张数。"}
    ))
    add("Duel.SpecialSummonStep", sig(
        "dot",
        "[协程挂起] 分步特殊召唤单张怪兽（须配对调用 `Duel.SpecialSummonComplete()` 完成结算），返回是否成功。",
        {"card", "summonType", "sumPlayer", "targetPlayer", "nocheck", "nolimit", "position", "zone"},
        {
            "---@param card Card 要特殊召唤的单张怪兽。",
            "---@param summonType SummonType 召唤类型。",
            "---@param sumPlayer Player 执行特殊召唤的玩家。",
            "---@param targetPlayer Player 特殊召唤到哪方场上。",
            "---@param nocheck boolean 是否无视召唤条件。",
            "---@param nolimit boolean 是否无视苏生限制。",
            "---@param position Position 表示形式。",
            "---@param zone? Zone 允许召唤到的格子位掩码（默认 `0xff`）。"
        },
        {"---@return boolean success"}
    ))
    add("Duel.SpecialSummonComplete", sig("dot", "[协程挂起] 统一结算所有通过 `Duel.SpecialSummonStep` 暂存的特殊召唤，返回实际特召张数。", {}, {}, {"---@return integer count"}))
    add("Duel.SendtoHand", sig(
        "dot",
        "[协程挂起] 将单卡或集合加入手牌；`player` 为 `nil` 时各自回到持有者手牌。",
        {"targets", "player", "reason", "reasonPlayer"},
        {
            "---@param targets Card|Group 目标单卡或集合。",
            "---@param player Player|nil 目标玩家（必填；传 `nil` 则各自回持有者手牌）。",
            "---@param reason Reason 移动原因。",
            "---@param reasonPlayer? Player 原因玩家（默认当前 `reason_player`）。"
        },
        {"---@return integer count 实际加入手牌张数。"}
    ))
    add("Duel.SendtoDeck", sig(
        "dot",
        "[协程挂起] 将单卡或集合送回卡组（`sequence`: `SEQ_DECKTOP`/`SEQ_DECKBOTTOM`/`SEQ_DECKSHUFFLE`）。",
        {"targets", "player", "sequence", "reason", "reasonPlayer", "sendActivating"},
        {
            "---@param targets Card|Group 目标单卡或集合。",
            "---@param player Player|nil 目标玩家（必填；传 `nil` 则各自回持有者卡组）。",
            "---@param sequence integer 放回位置（`SEQ_DECKTOP=0`、`SEQ_DECKBOTTOM=1`、`SEQ_DECKSHUFFLE=2`）。",
            "---@param reason Reason 移动原因。",
            "---@param reasonPlayer? Player 原因玩家（默认当前 `reason_player`）。",
            "---@param sendActivating? boolean 是否允许送回正在发动中的魔陷（默认 `false`）。"
        },
        {"---@return integer count 实际回到卡组张数。"}
    ))
    add("Duel.SendtoExtraP", sig(
        "dot",
        "[协程挂起] 将灵摆怪兽等单卡或集合表侧加入额外卡组（`player` 为 `nil` 时各自回持有者额外卡组）。",
        {"targets", "player", "reason"},
        {
            "---@param targets Card|Group 目标单卡或集合。",
            "---@param player Player|nil 目标玩家（必填；传 `nil` 各自回持有者额外）。",
            "---@param reason Reason 移动原因。"
        },
        {"---@return integer count 实际加入额外卡组张数。"}
    ))
    add("Duel.GetOperatedGroup", sig("dot", "取得上一次移动/破坏/抽卡等操作实际影响到的卡片集合副本。", {}, {}, {"---@return Group group"}))

    -- =========================================================================
    -- 指示物、表示形式、解放、移动格子、连锁限制与确认展示
    -- =========================================================================
    add("Duel.IsCanAddCounter", sig(
        "dot",
        "检查玩家能否放置指示物（1 参查全局放置权限；4 参查能否给指定卡放 `count` 个 `counterType`）。",
        {"player", "counterType", "count", "card"},
        {
            "---@param player Player 玩家编号（0 或 1）。",
            "---@param counterType? CounterType 可选：指示物类型编码。",
            "---@param count? integer 可选：放置数量。",
            "---@param card? Card 可选：目标卡片。"
        },
        {"---@return boolean canAdd"},
        nil,
        {
            "---@overload fun(player: Player): boolean",
            "---@overload fun(player: Player, counterType: CounterType, count: integer, card: Card): boolean"
        }
    ))
    add("Duel.RemoveCounter", sig(
        "dot",
        "[协程挂起] 从玩家视角己方 `s` / 对方 `o` 区域的卡片上取除指定种类和数量的指示物。",
        {"player", "selfLocation", "opponentLocation", "counterType", "count", "reason"},
        {
            "---@param player Player 执行取除的玩家（0 或 1）。",
            "---@param selfLocation Location 己方区域掩码。",
            "---@param opponentLocation Location 对方区域掩码。",
            "---@param counterType CounterType 指示物类型编码。",
            "---@param count integer 取除数量。",
            "---@param reason Reason 原因位掩码。"
        },
        {"---@return boolean|nil removed `player` 非 0/1 无返回值。"}
    ))
    add("Duel.IsCanRemoveCounter", sig(
        "dot",
        "检查玩家能否从己方 `s` / 对方 `o` 区域取除指定种类与数量的指示物。",
        {"player", "selfLocation", "opponentLocation", "counterType", "count", "reason"},
        {
            "---@param player Player 玩家编号（0 或 1）。",
            "---@param selfLocation Location 己方区域掩码。",
            "---@param opponentLocation Location 对方区域掩码。",
            "---@param counterType CounterType 指示物类型编码。",
            "---@param count integer 取除数量。",
            "---@param reason Reason 原因位掩码。"
        },
        {"---@return boolean|nil canRemove"}
    ))
    add("Duel.GetCounter", sig(
        "dot",
        "统计玩家视角己方 `s` / 对方 `o` 区域上指定类型指示物的总数。",
        {"player", "selfLocation", "opponentLocation", "counterType"},
        {
            "---@param player Player 玩家编号（0 或 1）。",
            "---@param selfLocation Location 己方区域掩码。",
            "---@param opponentLocation Location 对方区域掩码。",
            "---@param counterType CounterType 指示物类型编码。"
        },
        {"---@return integer|nil count"}
    ))
    add("Duel.ChangePosition", sig(
        "dot",
        "[协程挂起] 改变单卡或集合的表示形式；可按原表示（表攻/里攻/表守/里守）分别指定目标表示。",
        {"targets", "au", "ad", "du", "dd", "noFlip"},
        {
            "---@param targets Card|Group 目标单卡或集合。",
            "---@param au Position 原表侧攻击表示的目标表示（同时作为 `ad`/`du`/`dd` 省略时的默认值）。",
            "---@param ad? Position 原里侧攻击表示的目标表示（默认同 `au`）。",
            "---@param du? Position 原表侧守备表示的目标表示（默认同 `au`）。",
            "---@param dd? Position 原里侧守备表示的目标表示（默认同 `au`）。",
            "---@param noFlip? boolean 为 `true` 时不触发反转效果（默认 `false`）。"
        },
        {"---@return integer count 实际改变表示形式的张数。"}
    ))
    add("Duel.Release", sig(
        "dot",
        "[协程挂起] 解放单卡或集合，返回实际解放张数。",
        {"targets", "reason", "reasonPlayer"},
        {
            "---@param targets Card|Group 目标单卡或集合。",
            "---@param reason Reason 解放原因。",
            "---@param reasonPlayer? Player 原因玩家（默认当前 `reason_player`）。"
        },
        {"---@return integer count 实际解放张数。"}
    ))
    add("Duel.MoveToField", sig(
        "dot",
        "[协程挂起] 将卡片直接移动到目标玩家场上指定区域（`LOCATION_FZONE`/`LOCATION_PZONE` 自动映射）。",
        {"card", "movePlayer", "targetPlayer", "destination", "position", "enable", "zone"},
        {
            "---@param card Card 要放置的卡片。",
            "---@param movePlayer Player 执行放置的玩家。",
            "---@param targetPlayer Player 放置到哪方场上（0 或 1）。",
            "---@param destination Location 目标区域（如 `LOCATION_MZONE`、`LOCATION_SZONE`、`LOCATION_FZONE`、`LOCATION_PZONE`）。",
            "---@param position Position 放置后的表示形式。",
            "---@param enable boolean 是否启用场上效果。",
            "---@param zone? Zone 允许放置的格子掩码（默认 `0xff`）。"
        },
        {"---@return boolean|nil moved"}
    ))
    add("Duel.ReturnToField", sig(
        "dot",
        "[协程挂起] 将因 `REASON_TEMPORARY` 暂时离场的卡片返回原控制者原区域。",
        {"card", "position", "zone"},
        {
            "---@param card Card 带有暂时离场标记的卡片。",
            "---@param position? Position 返回时的表示形式（默认 `card:GetPreviousPosition()`）。",
            "---@param zone? Zone 允许返回的格子掩码（默认 `0xff`）。"
        },
        {"---@return boolean|nil returned 无暂时离场标记时无返回值。"}
    ))
    add("Duel.MoveSequence", sig("dot", "将卡片移动到当前所在区域的新序号 `sequence` 并触发 `EVENT_MOVE`。", {"card", "sequence"}, {"---@param card Card 目标卡片。", "---@param sequence integer 目标序号。"}, {"---@return nil"}))
    add("Duel.SwapSequence", sig("dot", "交换同一控制者怪兽区中两张怪兽的位置并触发 `EVENT_MOVE`。", {"card1", "card2"}, {"---@param card1 Card 第一张怪兽。", "---@param card2 Card 第二张怪兽。"}, {"---@return nil"}))
    add("Duel.Activate", sig("dot", "将效果 `effect` 压入引擎的立即发动处理队列（不挂起）。", {"effect"}, {"---@param effect Effect 要发动的效果。"}, {"---@return nil"}))
    add("Duel.SetChainLimit", sig("dot", "为当前连锁项注册连锁限制回调 `f(e, rp, tp) -> boolean`。", {"limitFunc"}, {"---@param limitFunc ChainLimitFunction 连锁限制函数。"}, {"---@return nil"}))
    add("Duel.SetChainLimitTillChainEnd", sig("dot", "为当前连锁串后续全部连锁窗口注册临时连锁限制回调。", {"limitFunc"}, {"---@param limitFunc ChainLimitFunction 连锁限制函数。"}, {"---@return nil"}))
    add("Duel.GetChainMaterial", sig("dot", "取得作用于指定玩家的首个 `EFFECT_CHAIN_MATERIAL` 效果。", {"player"}, {"---@param player Player 玩家编号。"}, {"---@return Effect|nil effect"}))
    add("Duel.ConfirmDecktop", sig("dot", "[协程挂起] 双方确认指定玩家主卡组顶最多 `count` 张卡。", {"player", "count"}, {"---@param player Player 玩家编号。", "---@param count integer 确认张数。"}, {"---@return nil"}))
    add("Duel.ConfirmExtratop", sig("dot", "[协程挂起] 确认指定玩家额外卡组里侧顶（跳过表侧灵摆）最多 `count` 张卡。", {"player", "count"}, {"---@param player Player 玩家编号。", "---@param count integer 确认张数。"}, {"---@return nil"}))
    add("Duel.ConfirmCards", sig(
        "dot",
        "[协程挂起] 向指定玩家展示单卡或集合（空集合直接跳过）。",
        {"player", "targets", "skipPanel"},
        {
            "---@param player Player 观看展示的玩家编号。",
            "---@param targets Card|Group 要展示的单卡或集合。",
            "---@param skipPanel? boolean 为 `true` 时不弹出客户端确认面板（默认 `false`）。"
        },
        {"---@return nil"}
    ))
    add("Duel.SortDecktop", sig("dot", "[协程挂起] 让 `sortPlayer` 排列 `targetPlayer` 主卡组顶 `count` 张卡的顺序。", {"sortPlayer", "targetPlayer", "count"}, {"---@param sortPlayer Player 执行排序的玩家。", "---@param targetPlayer Player 卡组所属玩家。", "---@param count integer 排序张数。"}, {"---@return nil"}))

    -- =========================================================================
    -- 事件、环境、胜负、抽卡、伤害/回复、装备、控制权、支付 LP、洗牌
    -- =========================================================================
    add("Duel.CheckEvent", sig(
        "dot",
        "检查当前是否正在处理事件 `event`；`getInfo=true` 且命中时返回 7 个值 `(true, eg, ep, ev, re, r, rp)`。",
        {"event", "getInfo"},
        {
            "---@param event EffectCode 事件代码（`EVENT_*`）。",
            "---@param getInfo? boolean 是否在命中时一并返回事件 6 元组（默认 `false`）。"
        },
        {
            "---@return boolean matched 是否命中事件。",
            "---@return Group|nil eg 事件卡组（仅 `getInfo=true` 且命中时）。",
            "---@return Player? ep 事件玩家。",
            "---@return integer? ev 事件值。",
            "---@return Effect|nil re 触发效果。",
            "---@return Reason? r 触发原因。",
            "---@return Player? rp 触发玩家。"
        },
        nil,
        {
            "---@overload fun(event: EffectCode): boolean",
            "---@overload fun(event: EffectCode, getInfo: true): boolean, Group|nil, Player?, integer?, Effect|nil, Reason?, Player?"
        }
    ))
    add("Duel.RaiseEvent", sig(
        "dot",
        "[协程挂起] 手动触发一个全局事件并立即推进瞬时事件处理。",
        {"eg", "code", "re", "r", "rp", "ep", "ev"},
        {
            "---@param eg Card|Group 事件相关卡片或集合。",
            "---@param code EffectCode 事件代码（`EVENT_*`）。",
            "---@param re Effect|nil 触发原因效果。",
            "---@param r Reason 触发原因位掩码。",
            "---@param rp Player 触发原因玩家。",
            "---@param ep Player 事件对象玩家。",
            "---@param ev integer 事件数值。"
        },
        {"---@return nil"}
    ))
    add("Duel.RaiseSingleEvent", sig(
        "dot",
        "[协程挂起] 对单张卡片触发单体事件并立即推进瞬时事件处理。",
        {"card", "code", "re", "r", "rp", "ep", "ev"},
        {
            "---@param card Card 目标卡片。",
            "---@param code EffectCode 单体事件代码（`EVENT_*`）。",
            "---@param re Effect|nil 触发原因效果。",
            "---@param r Reason 触发原因位掩码。",
            "---@param rp Player 触发原因玩家。",
            "---@param ep Player 事件对象玩家。",
            "---@param ev integer 事件数值。"
        },
        {"---@return nil"}
    ))
    add("Duel.CheckTiming", sig("dot", "检查当前任意玩家的提示时点是否命中 `timing` 位掩码。", {"timing"}, {"---@param timing Timing 时点位掩码（`TIMING_*`）。"}, {"---@return boolean matched"}))
    add("Duel.IsEnvironment", sig(
        "dot",
        "判定场上/墓地是否存在表侧的指定卡号场地/环境（无表侧场地魔法时同步检查 `EFFECT_CHANGE_ENVIRONMENT`）。",
        {"code", "player", "location"},
        {
            "---@param code CardCode 环境卡号（如海 `22702055`）。",
            "---@param player? Player 检查哪方（默认 `PLAYER_ALL` 双方）。",
            "---@param location? Location 检查区域（默认 `LOCATION_ONFIELD`）。"
        },
        {"---@return boolean|nil active"}
    ))
    add("Duel.Win", sig("dot", "立即判定玩家胜出（`player` 可为 `0`、`1` 或 `PLAYER_NONE=2` 平局）。", {"player", "reason"}, {"---@param player Player 胜出玩家（0、1 或 2 平局）。", "---@param reason integer 特殊胜利代码（`WIN_REASON_*`）。"}, {"---@return nil"}))
    add("Duel.Draw", sig("dot", "[协程挂起] 让玩家抽 `count` 张卡，返回实际抽卡张数。", {"player", "count", "reason"}, {"---@param player Player 玩家编号（0 或 1）。", "---@param count integer 抽卡张数。", "---@param reason Reason 抽卡原因。"}, {"---@return integer|nil drawn"}))
    add("Duel.Damage", sig(
        "dot",
        "[协程挂起] 给予玩家效果伤害，返回实际造成的伤害值；`isStep=true` 时暂存等待 `Duel.RDComplete()` 统一结算。",
        {"player", "amount", "reason", "isStep"},
        {
            "---@param player Player 受伤玩家（0 或 1）。",
            "---@param amount integer 伤害值（负值归 0）。",
            "---@param reason Reason 伤害原因。",
            "---@param isStep? boolean 是否分步结算（默认 `false`）。"
        },
        {"---@return integer|nil damage"}
    ))
    add("Duel.Recover", sig(
        "dot",
        "[协程挂起] 回复玩家 LP，返回实际回复值；`isStep=true` 时暂存等待 `Duel.RDComplete()` 统一结算。",
        {"player", "amount", "reason", "isStep"},
        {
            "---@param player Player 回复玩家（0 或 1）。",
            "---@param amount integer 回复量（负值归 0）。",
            "---@param reason Reason 回复原因。",
            "---@param isStep? boolean 是否分步结算（默认 `false`）。"
        },
        {"---@return integer|nil recovered"}
    ))
    add("Duel.RDComplete", sig("dot", "[协程挂起] 统一结算所有通过 `isStep=true` 暂存的效果伤害与 LP 回复。", {}, {}, {"---@return nil"}))
    add("Duel.Equip", sig(
        "dot",
        "[协程挂起] 将 `equipCard` 装备给 `target`（`step=true` 时需配对调用 `Duel.EquipComplete()`）。",
        {"player", "equipCard", "target", "faceup", "step"},
        {
            "---@param player Player 执行装备的玩家（0 或 1）。",
            "---@param equipCard Card 装备卡。",
            "---@param target Card 被装备怪兽。",
            "---@param faceup? boolean 是否表侧表示装备（默认 `true`）。",
            "---@param step? boolean 是否分步装备（默认 `false`）。"
        },
        {"---@return boolean|nil equipped"}
    ))
    add("Duel.EquipComplete", sig("dot", "[协程挂起] 启用所有分步装备卡的效果并触发 `EVENT_EQUIP`。", {}, {}, {"---@return nil"}))
    add("Duel.GetControl", sig(
        "dot",
        "[协程挂起] 将单卡或集合的控制权转移给 `player`，返回实际转移张数。",
        {"targets", "player", "resetPhase", "resetCount", "zone"},
        {
            "---@param targets Card|Group 要转移控制权的单卡或集合。",
            "---@param player Player 接收控制权的玩家（0 或 1）。",
            "---@param resetPhase? Phase 归还控制权的阶段（默认 `0` 永久）。",
            "---@param resetCount? integer 经过多少次该阶段后归还（默认 `0`）。",
            "---@param zone? Zone 允许放置的怪兽区位掩码（默认 `0xff`）。"
        },
        {"---@return integer|nil moved"}
    ))
    add("Duel.SwapControl", sig(
        "dot",
        "[协程挂起] 交换两张卡或两个等量卡片集合的控制权（参数 1 与参数 2 必须同为 `Card` 或同为 `Group`）。",
        {"targets1", "targets2", "resetPhase", "resetCount"},
        {
            "---@param targets1 Card|Group 第一方卡片或集合。",
            "---@param targets2 Card|Group 第二方卡片或集合（须与 `targets1` 同类型）。",
            "---@param resetPhase? Phase 归还阶段（默认 `0` 永久）。",
            "---@param resetCount? integer 归还回合计数（默认 `0`）。"
        },
        {"---@return boolean swapped"},
        nil,
        {
            "---@overload fun(card1: Card, card2: Card, resetPhase?: Phase, resetCount?: integer): boolean",
            "---@overload fun(group1: Group, group2: Group, resetPhase?: Phase, resetCount?: integer): boolean"
        }
    ))
    add("Duel.CheckLPCost", sig(
        "dot",
        "检查玩家能否支付指定数值的 LP 代价（`cost <= 0` 返回 `false`）。",
        {"player", "cost", "mustPay"},
        {
            "---@param player Player 玩家编号（0 或 1）。",
            "---@param cost integer 需支付的 LP 数量。",
            "---@param mustPay? boolean 是否为强制支付（不计入免支付替代效果，默认 `false`）。"
        },
        {"---@return boolean|nil payable"}
    ))
    add("Duel.PayLPCost", sig(
        "dot",
        "[协程挂起] 让玩家支付指定数值的 LP 代价。",
        {"player", "cost", "mustPay"},
        {
            "---@param player Player 玩家编号（0 或 1）。",
            "---@param cost integer 支付的 LP 数量。",
            "---@param mustPay? boolean 是否为强制支付（默认 `false`）。"
        },
        {"---@return nil"}
    ))
    add("Duel.DiscardDeck", sig("dot", "[协程挂起] 将玩家主卡组顶 `count` 张卡送去墓地，返回实际送墓张数。", {"player", "count", "reason"}, {"---@param player Player 玩家编号。", "---@param count integer 张数。", "---@param reason Reason 原因。"}, {"---@return integer count"}))
    add("Duel.DiscardHand", sig(
        "dot",
        "[协程挂起] 按过滤条件让玩家从手牌选择 `min..max` 张卡丢弃（送墓），返回实际丢弃张数。",
        {"player", "filter", "min", "max", "reason", "excluded", "..."},
        {
            "---@param player Player 玩家编号（0 或 1）。",
            "---@param filter FilterFunction|nil 手牌过滤函数（`nil` 表示任意手牌）。",
            "---@param min integer 最少丢弃张数。",
            "---@param max integer 最多丢弃张数。",
            "---@param reason Reason 丢弃原因（通常含 `REASON_DISCARD`）。",
            "---@param excluded? Card|Group|nil 排除的单卡或集合（默认 `nil`）。",
            "---@param ... any 透传给 `filter` 的附加参数。"
        },
        {"---@return integer|nil discarded"}
    ))
    add("Duel.DisableShuffleCheck", sig("dot", "开启或关闭引擎的自动卡组洗切检查。", {"disable"}, {"---@param disable? boolean 是否禁用自动洗切检查（默认 `true`）。"}, {"---@return nil"}))
    add("Duel.DisableSelfDestroyCheck", sig("dot", "开启或关闭引擎的自毁状态检查。", {"disable"}, {"---@param disable? boolean 是否禁用自毁检查（默认 `true`）。"}, {"---@return nil"}))
    add("Duel.RevealSelectDeckSequence", sig("dot", "开启或关闭从卡组选卡时是否向玩家公开卡组序号。", {"reveal"}, {"---@param reveal? boolean 是否公开序号（默认 `true`）。"}, {"---@return nil"}))
    add("Duel.ShuffleDeck", sig("dot", "洗切指定玩家的主卡组。", {"player"}, {"---@param player Player 玩家编号。"}, {"---@return nil"}))
    add("Duel.ShuffleExtra", sig("dot", "洗切指定玩家的额外卡组。", {"player"}, {"---@param player Player 玩家编号。"}, {"---@return nil"}))
    add("Duel.ShuffleHand", sig("dot", "洗切指定玩家的手牌。", {"player"}, {"---@param player Player 玩家编号。"}, {"---@return nil"}))
    add("Duel.ShuffleSetCard", sig("dot", "将同控制者同区域（主怪兽区/主魔陷区 0~4）的多张里侧卡随机洗牌换位并触发 `EVENT_MOVE`。", {"group"}, {"---@param group Group 同区域里侧卡片集合。"}, {"---@return nil"}))

    -- =========================================================================
    -- 战斗、连锁修改/无效、通召计数、格子查询、阶段与场面调整
    -- =========================================================================
    add("Duel.ChangeAttacker", sig("dot", "将当前战斗的攻击怪兽替换为 `attacker`。", {"attacker", "ignoreCount"}, {"---@param attacker Card 新的攻击怪兽。", "---@param ignoreCount? boolean 是否忽略攻击次数记录（默认 `false`）。"}, {"---@return nil"}))
    add("Duel.ChangeAttackTarget", sig("dot", "更改当前战斗的攻击对象（传 `nil` 改为直接攻击），返回是否成功。", {"target"}, {"---@param target Card|nil 新攻击对象怪兽，传 `nil` 为直接攻击。"}, {"---@return boolean changed"}))
    add("Duel.CalculateDamage", sig("dot", "[协程挂起] 强制进入伤害步骤进行一次战斗伤害计算（`target=nil` 表示直接攻击）。", {"attacker", "target", "newAttack"}, {"---@param attacker Card 攻击怪兽。", "---@param target Card|nil 被攻击怪兽（`nil` 为直接攻击）。", "---@param newAttack? boolean 是否视为新攻击（默认 `false`）。"}, {"---@return nil"}))
    add("Duel.GetBattleDamage", sig("dot", "取得指定玩家本次战斗将受到的战斗伤害值。", {"player"}, {"---@param player Player 玩家编号（0 或 1）。"}, {"---@return integer|nil damage"}))
    add("Duel.ChangeBattleDamage", sig("dot", "修改指定玩家本次战斗伤害值（`check=true` 且原伤害为 `0` 时不修改）。", {"player", "damage", "check"}, {"---@param player Player 玩家编号。", "---@param damage integer 新战斗伤害值。", "---@param check? boolean 原伤害为 `0` 时是否跳过修改（默认 `true`）。"}, {"---@return nil"}))
    add("Duel.ChangeTargetCard", sig("dot", "替换指定连锁项的目标卡片集合。", {"chainIndex", "targets"}, {"---@param chainIndex integer 连锁序号（1-based）。", "---@param targets Group 新的目标集合。"}, {"---@return nil"}))
    add("Duel.ChangeTargetPlayer", sig("dot", "替换指定连锁项的目标玩家。", {"chainIndex", "player"}, {"---@param chainIndex integer 连锁序号。", "---@param player Player 新的目标玩家。"}, {"---@return nil"}))
    add("Duel.ChangeTargetParam", sig("dot", "替换指定连锁项的目标参数值。", {"chainIndex", "param"}, {"---@param chainIndex integer 连锁序号。", "---@param param integer 新的目标参数。"}, {"---@return nil"}))
    add("Duel.BreakEffect", sig("dot", "[协程挂起] 插入效果处理断点（区分“那之后”与同时处理）并触发 `EVENT_BREAK_EFFECT`。", {}, {}, {"---@return nil"}))
    add("Duel.ChangeChainOperation", sig("dot", "替换指定连锁项的处理函数（`operation`）。", {"chainIndex", "operation"}, {"---@param chainIndex integer 连锁序号。", "---@param operation OperationFunction 新的效果处理函数。"}, {"---@return nil"}))
    add("Duel.NegateActivation", sig("dot", "无效指定连锁项的效果发动，返回是否成功。", {"chainIndex"}, {"---@param chainIndex integer 连锁序号。"}, {"---@return boolean negated"}))
    add("Duel.NegateEffect", sig("dot", "无效指定连锁项的效果处理，返回是否成功。", {"chainIndex", "forced"}, {"---@param chainIndex integer 连锁序号。", "---@param forced? boolean 是否强制无效（默认 `false`）。"}, {"---@return boolean negated"}))
    add("Duel.NegateRelatedChain", sig("dot", "给 `card` 附加 `EFFECT_DISABLE_CHAIN`，无效当前连锁串中仍与该卡关联的该卡效果。", {"card", "reset"}, {"---@param card Card 目标卡片。", "---@param reset Reset 重置条件。"}, {"---@return nil"}))
    add("Duel.NegateSummon", sig("dot", "无效当前正在进行的召唤/反转召唤/特殊召唤并触发对应的 `EVENT_*_NEGATED`。", {"targets"}, {"---@param targets Card|Group 被无效召唤的单卡或集合。"}, {"---@return nil"}))
    add("Duel.IncreaseSummonedCount", sig("dot", "增加当前 `reason_player` 的通常召唤次数（传入 `card` 且存在额外通召权时优先消耗额外通召权）。", {"card"}, {"---@param card? Card 可选：正在召唤的怪兽。"}, {"---@return nil"}))
    add("Duel.CheckSummonedCount", sig("dot", "检查当前 `reason_player` 是否仍保有通常召唤次数（或对 `card` 的额外通召权）。", {"card"}, {"---@param card? Card 可选：要召唤的怪兽。"}, {"---@return boolean available"}))

    add("Duel.GetLocationCount", sig(
        "dot",
        "查询玩家主怪兽区/魔陷区的可用空位数（可为负）与可用格子位掩码（双返回值）。",
        {"player", "location", "uplayer", "reason", "zone"},
        {
            "---@param player Player 场地所属玩家（0 或 1）。",
            "---@param location Location `LOCATION_MZONE` 或 `LOCATION_SZONE`。",
            "---@param uplayer? Player|nil 使用格子的玩家（默认当前 `reason_player`）。",
            "---@param reason? integer 占用原因（默认 `LOCATION_REASON_TOFIELD`）。",
            "---@param zone? Zone 限定的格子位掩码（默认 `0xff`）。"
        },
        {"---@return integer|nil count 可用空位数（`player` 非 0/1 无返回值）。", "---@return Zone|nil usableZone 可用格子位掩码。"}
    ))
    add("Duel.GetMZoneCount", sig(
        "dot",
        "模拟 `targets` 离场后，查询玩家主怪兽区的可用空位数与可用格子位掩码（双返回值）。",
        {"player", "targets", "uplayer", "reason", "zone"},
        {
            "---@param player Player 场地所属玩家（0 或 1）。",
            "---@param targets? Card|Group|nil 模拟即将离场的卡片或集合（默认 `nil`）。",
            "---@param uplayer? Player 使用格子的玩家（省略时默认 `reason_player`；注意 C++ 未判 `nil`，显式传 `nil` 会被转为 `0`！）。",
            "---@param reason? integer 占用原因（默认 `LOCATION_REASON_TOFIELD`）。",
            "---@param zone? Zone 限定的格子位掩码（默认 `0xff`）。"
        },
        {"---@return integer|nil count 可用空位数。", "---@return Zone|nil usableZone 可用格子位掩码。"}
    ))
    add("Duel.GetSZoneCount", sig(
        "dot",
        "模拟 `targets` 离场后，查询玩家魔陷区的可用空位数与可用格子位掩码（双返回值）。",
        {"player", "targets", "uplayer", "reason", "zone"},
        {
            "---@param player Player 场地所属玩家（0 或 1）。",
            "---@param targets? Card|Group|nil 模拟即将离场的卡片或集合（默认 `nil`）。",
            "---@param uplayer? Player 使用格子的玩家（省略时默认 `reason_player`；注意显式传 `nil` 会被转为 `0`！）。",
            "---@param reason? integer 占用原因（默认 `LOCATION_REASON_TOFIELD`）。",
            "---@param zone? Zone 限定的格子位掩码（默认 `0xff`）。"
        },
        {"---@return integer|nil count 可用空位数。", "---@return Zone|nil usableZone 可用格子位掩码。"}
    ))
    add("Duel.GetLocationCountFromEx", sig(
        "dot",
        "模拟 `targets` 离场后，计算从额外卡组特招 `sc` 到 `player` 场上的可用空位数与可用格子位掩码（双返回值）。",
        {"player", "uplayer", "targets", "sc", "zone"},
        {
            "---@param player Player 场地所属玩家（0 或 1）。",
            "---@param uplayer? Player|nil 执行特召的玩家（默认当前 `reason_player`，支持传 `nil`）。",
            "---@param targets? Card|Group|nil 模拟即将离场的素材卡或集合（默认 `nil`）。",
            "---@param sc? Card|CardType|nil 要从额外特招的具体怪兽 `Card` 或种类掩码（如 `TYPE_LINK`/`TYPE_PENDULUM`）。",
            "---@param zone? Zone 限定的格子位掩码（默认 `0xff`）。"
        },
        {"---@return integer|nil count 可用空位数。", "---@return Zone|nil usableZone 可用格子位掩码。"}
    ))
    add("Duel.GetUsableMZoneCount", sig("dot", "取得玩家主怪兽区与额外怪兽区合计可用的总格数。", {"player", "uplayer"}, {"---@param player Player 场地所属玩家。", "---@param uplayer? Player 使用者玩家（默认 `reason_player`）。"}, {"---@return integer|nil count"}))
    add("Duel.GetLinkedGroup", sig("dot", "取得指定玩家视角己方 `s` / 对方 `o` 区域中处于连接状态的全部卡片集合。", {"player", "selfLocation", "opponentLocation"}, {"---@param player Player 视角玩家。", "---@param selfLocation Location 己方区域。", "---@param opponentLocation Location 对方区域。"}, {"---@return Group|nil group"}))
    add("Duel.GetLinkedGroupCount", sig("dot", "取得指定玩家视角己方 `s` / 对方 `o` 区域中处于连接状态的卡片数量。", {"player", "selfLocation", "opponentLocation"}, {"---@param player Player 视角玩家。", "---@param selfLocation Location 己方区域。", "---@param opponentLocation Location 对方区域。"}, {"---@return integer|nil count"}))
    add("Duel.GetLinkedZone", sig("dot", "取得指定玩家视角的全局连接端格子位掩码。", {"player"}, {"---@param player Player 视角玩家。"}, {"---@return Zone|nil zone"}))
    add("Duel.GetFieldCard", sig("dot", "取得指定玩家、区域与序号位置的单张卡片（无卡或正在召唤中返回 `nil`）。", {"player", "location", "sequence"}, {"---@param player Player 玩家编号。", "---@param location Location 区域（如 `LOCATION_MZONE`、`LOCATION_SZONE`、`LOCATION_PZONE`）。", "---@param sequence integer 格子序号。"}, {"---@return Card|nil card"}))
    add("Duel.CheckLocation", sig("dot", "检查指定玩家的指定区域格子是否空闲且可用。", {"player", "location", "sequence"}, {"---@param player Player 玩家编号。", "---@param location Location 区域。", "---@param sequence integer 格子序号。"}, {"---@return boolean|nil usable"}))

    add("Duel.GetCurrentChain", sig("dot", "取得当前连锁堆叠长度（`0` 表示不在连锁中）。", {}, {}, {"---@return integer chainCount"}))
    add("Duel.GetReadyChain", sig("dot", "取得当前已完成取对象的就绪连锁数量。", {}, {}, {"---@return integer readyChainCount"}))
    add("Duel.GetChainInfo", sig(
        "dot",
        "按 1..N 个 `CHAININFO_*` 标志查询连锁 `chainIndex`（`0` 表示当前正在处理的连锁），按序多值返回对应结果。",
        {"chainIndex", "..."},
        {
            "---@param chainIndex integer 连锁序号（`0` 为当前处理连锁，正数为 1-based 序号）。",
            "---@param ... ChainInfo 要查询的连锁信息标志（`CHAININFO_*`）。"
        },
        {"---@return any ... 与输入标志一一对应的返回值（`integer`/`Effect`/`Group`/`nil`）。"}
    ))
    add("Duel.GetChainEvent", sig(
        "dot",
        "返回指定连锁项发动时捕获的 6 元组事件信息（连锁不存在时无返回值）。",
        {"chainIndex"},
        {"---@param chainIndex integer 连锁序号。"},
        {
            "---@return Group|nil eg 事件卡组。",
            "---@return Player? ep 事件玩家。",
            "---@return integer? ev 事件数值。",
            "---@return Effect|nil re 触发效果。",
            "---@return Reason? r 触发原因。",
            "---@return Player? rp 触发玩家。"
        }
    ))
    add("Duel.GetFirstTarget", sig("dot", "将当前处理连锁的全部对象卡片依次作为多返回值返回（无对象时返回 `nil`）。", {}, {}, {"---@return Card ... 当前连锁的所有对象卡片。"}))
    add("Duel.GetTargetsRelateToChain", sig("dot", "取得当前处理连锁中仍然保持关联的对象卡片集合。", {}, {}, {"---@return Group targets"}))
    add("Duel.IsPhase", sig("dot", "判定当前阶段是否严格等于 `phase`。", {"phase"}, {"---@param phase Phase 阶段常量（`PHASE_*`）。"}, {"---@return boolean matched"}))
    add("Duel.IsMainPhase", sig("dot", "判定当前是否处于主要阶段 1 或主要阶段 2。", {}, {}, {"---@return boolean isMainPhase"}))
    add("Duel.IsBattlePhase", sig("dot", "判定当前是否处于战斗阶段（`PHASE_BATTLE_START`～`PHASE_BATTLE`）。", {}, {}, {"---@return boolean isBattlePhase"}))
    add("Duel.GetCurrentPhase", sig("dot", "取得当前阶段常量（`PHASE_*`）。", {}, {}, {"---@return Phase phase"}))
    add("Duel.SkipPhase", sig(
        "dot",
        "跳过指定玩家的某个阶段（DP/SP/M1/BP/M2/EP）。",
        {"player", "phase", "reset", "resetCount", "value"},
        {
            "---@param player Player 目标玩家。",
            "---@param phase Phase 要跳过的阶段。",
            "---@param reset Reset 重置条件。",
            "---@param resetCount integer 重置计数（`<= 0` 自动修正为 `1`）。",
            "---@param value? integer 附加给跳过效果的整型 value（默认 `0`）。"
        },
        {"---@return nil"}
    ))
    add("Duel.IsDamageCalculated", sig("dot", "判定本次战斗是否已完成伤害计算。", {}, {}, {"---@return boolean calculated"}))
    add("Duel.GetAttacker", sig("dot", "取得当前战斗的攻击怪兽。", {}, {}, {"---@return Card|nil attacker"}))
    add("Duel.GetAttackTarget", sig("dot", "取得当前战斗的被攻击怪兽（直接攻击时返回 `nil`）。", {}, {}, {"---@return Card|nil target"}))
    add("Duel.GetBattleMonster", sig(
        "dot",
        "按 `(己方战斗怪兽, 对方战斗怪兽)` 双值返回当前参与战斗的双方怪兽。",
        {"player"},
        {"---@param player Player 基准视角玩家（强校验整数）。"},
        {"---@return Card|nil selfMonster", "---@return Card|nil opponentMonster"}
    ))
    add("Duel.NegateAttack", sig("dot", "[协程挂起] 无效当前攻击，返回是否成功。", {}, {}, {"---@return boolean negated"}))
    add("Duel.ChainAttack", sig("dot", "赋予当前攻击怪兽一次额外连续攻击机会，可选限定只能攻击 `target`。", {"target"}, {"---@param target? Card 可选：限定连击目标怪兽。"}, {"---@return nil"}))
    add("Duel.Readjust", sig("dot", "[可能协程挂起] 请求引擎重新调整场面状态（含死循环保护）。", {}, {}, {"---@return nil"}))
    add("Duel.AdjustInstantly", sig("dot", "立即重算场上持续效果（传入 `card` 时先刷新其无效关联卡）。", {"card"}, {"---@param card? Card 可选：先刷新关联的卡片。"}, {"---@return nil"}))
    add("Duel.AdjustAll", sig("dot", "[协程挂起] 执行完整的场面状态调整与规则/自毁检查。", {}, {}, {"---@return nil"}))

    -- =========================================================================
    -- 区域查询、筛选与选卡 / 解放 / 祭品 / 取对象
    -- =========================================================================
    add("Duel.GetFieldGroup", sig("dot", "取得玩家视角己方 `selfLocation` 与对方 `opponentLocation` 的全部卡片集合。", {"player", "selfLocation", "opponentLocation"}, {"---@param player Player 视角玩家。", "---@param selfLocation Location 己方区域掩码。", "---@param opponentLocation Location 对方区域掩码。"}, {"---@return Group group"}))
    add("Duel.GetFieldGroupCount", sig("dot", "取得玩家视角己方 `selfLocation` 与对方 `opponentLocation` 的卡片总数。", {"player", "selfLocation", "opponentLocation"}, {"---@param player Player 视角玩家。", "---@param selfLocation Location 己方区域掩码。", "---@param opponentLocation Location 对方区域掩码。"}, {"---@return integer count"}))
    add("Duel.GetDecktopGroup", sig("dot", "取得玩家主卡组顶最多 `count` 张卡的集合。", {"player", "count"}, {"---@param player Player 玩家编号。", "---@param count integer 张数。"}, {"---@return Group group"}))
    add("Duel.GetExtraTopGroup", sig("dot", "取得玩家额外卡组里侧顶（跳过表侧灵摆）最多 `count` 张卡的集合。", {"player", "count"}, {"---@param player Player 玩家编号。", "---@param count integer 张数。"}, {"---@return Group group"}))

    add("Duel.GetMatchingGroup", sig(
        "dot",
        "筛选玩家视角指定区域中满足过滤条件的全部卡片集合。",
        {"filter", "player", "selfLocation", "opponentLocation", "excluded", "..."},
        {
            "---@param filter FilterFunction|nil 过滤函数（`nil` 表示不过滤）。",
            "---@param player Player 区域视角玩家。",
            "---@param selfLocation Location 己方区域掩码。",
            "---@param opponentLocation Location 对方区域掩码。",
            "---@param excluded Card|Group|nil 排除的单卡或集合（必填，无则传 `nil`）。",
            "---@param ... any 透传给 `filter` 的附加参数。"
        },
        {"---@return Group group"}
    ))
    add("Duel.GetMatchingGroupCount", sig(
        "dot",
        "统计玩家视角指定区域中满足过滤条件的卡片数量。",
        {"filter", "player", "selfLocation", "opponentLocation", "excluded", "..."},
        {
            "---@param filter FilterFunction|nil 过滤函数（`nil` 表示不过滤）。",
            "---@param player Player 区域视角玩家。",
            "---@param selfLocation Location 己方区域掩码。",
            "---@param opponentLocation Location 对方区域掩码。",
            "---@param excluded Card|Group|nil 排除的单卡或集合（必填，无则传 `nil`）。",
            "---@param ... any 透传给 `filter` 的附加参数。"
        },
        {"---@return integer count"}
    ))
    add("Duel.GetFirstMatchingCard", sig(
        "dot",
        "查找并返回指定区域中第一张满足过滤条件的卡片；未找到返回 `nil`。",
        {"filter", "player", "selfLocation", "opponentLocation", "excluded", "..."},
        {
            "---@param filter FilterFunction|nil 过滤函数。",
            "---@param player Player 区域视角玩家。",
            "---@param selfLocation Location 己方区域掩码。",
            "---@param opponentLocation Location 对方区域掩码。",
            "---@param excluded Card|Group|nil 排除的单卡或集合（必填，无则传 `nil`）。",
            "---@param ... any 透传给 `filter` 的附加参数。"
        },
        {"---@return Card|nil card"}
    ))
    add("Duel.IsExistingMatchingCard", sig(
        "dot",
        "检查指定区域中满足过滤条件的卡片是否至少达到 `count` 张。",
        {"filter", "player", "selfLocation", "opponentLocation", "count", "excluded", "..."},
        {
            "---@param filter FilterFunction|nil 过滤函数。",
            "---@param player Player 区域视角玩家。",
            "---@param selfLocation Location 己方区域掩码。",
            "---@param opponentLocation Location 对方区域掩码。",
            "---@param count integer 最少所需张数。",
            "---@param excluded Card|Group|nil 排除的单卡或集合（必填，无则传 `nil`）。",
            "---@param ... any 透传给 `filter` 的附加参数。"
        },
        {"---@return boolean exists"}
    ))
    add("Duel.SelectMatchingCard", sig(
        "dot",
        "[协程挂起] 让 `player` 从 `selectPlayer` 视角指定区域中选择 `min..max` 张满足条件的卡片。",
        {"player", "filter", "selectPlayer", "selfLocation", "opponentLocation", "min", "max", "excluded", "..."},
        {
            "---@param player Player 执行选择的玩家（0 或 1）。",
            "---@param filter FilterFunction|nil 过滤函数。",
            "---@param selectPlayer Player 区域视角玩家。",
            "---@param selfLocation Location 视角方区域掩码。",
            "---@param opponentLocation Location 视角对手方区域掩码。",
            "---@param min integer 最少选择张数。",
            "---@param max integer 最多选择张数。",
            "---@param excluded Card|Group|nil 排除的单卡或集合（必填，无则传 `nil`）。",
            "---@param ... any 透传给 `filter` 的附加参数。"
        },
        {"---@return Group|nil selected"}
    ))

    add("Duel.GetReleaseGroup", sig("dot", "取得玩家可用于解放的卡片集合。", {"player", "useHand", "reason"}, {"---@param player Player 玩家编号。", "---@param useHand? boolean 是否包含手牌（默认 `false`）。", "---@param reason? Reason 解放原因（默认 `REASON_COST`）。"}, {"---@return Group|nil group"}))
    add("Duel.GetReleaseGroupCount", sig("dot", "取得玩家可用于解放的卡片数量。", {"player", "useHand", "reason"}, {"---@param player Player 玩家编号。", "---@param useHand? boolean 是否包含手牌（默认 `false`）。", "---@param reason? Reason 解放原因（默认 `REASON_COST`）。"}, {"---@return integer|nil count"}))
    add("Duel.CheckReleaseGroup", sig(
        "dot",
        "检查玩家场上是否存在至少 `count` 张符合条件的 `REASON_COST` 可解放卡（并清空 `must_select_cards`）。",
        {"player", "filter", "count", "excluded", "..."},
        {
            "---@param player Player 玩家编号。",
            "---@param filter FilterFunction|nil 过滤函数。",
            "---@param count integer 最少张数。",
            "---@param excluded Card|Group|nil 排除项（必填，无则传 `nil`）。",
            "---@param ... any 透传给 `filter` 的附加参数。"
        },
        {"---@return boolean|nil exists"}
    ))
    add("Duel.SelectReleaseGroup", sig(
        "dot",
        "[协程挂起] 让玩家从场上选择 `min..max` 张符合条件的 `REASON_COST` 可解放卡。",
        {"player", "filter", "min", "max", "excluded", "..."},
        {
            "---@param player Player 玩家编号。",
            "---@param filter FilterFunction|nil 过滤函数。",
            "---@param min integer 最少张数。",
            "---@param max integer 最多张数。",
            "---@param excluded Card|Group|nil 排除项（必填，无则传 `nil`）。",
            "---@param ... any 透传给 `filter` 的附加参数。"
        },
        {"---@return Group|nil selected"}
    ))
    add("Duel.CheckReleaseGroupEx", sig(
        "dot",
        "`CheckReleaseGroup` 扩展版：可自定义 `reason` 与是否包含手牌 `useHand`（并清空 `must_select_cards`）。",
        {"player", "filter", "count", "reason", "useHand", "excluded", "..."},
        {
            "---@param player Player 玩家编号。",
            "---@param filter FilterFunction|nil 过滤函数。",
            "---@param count integer 最少张数。",
            "---@param reason Reason 解放原因。",
            "---@param useHand boolean 是否包含手牌。",
            "---@param excluded Card|Group|nil 排除项（必填，无则传 `nil`）。",
            "---@param ... any 透传给 `filter` 的附加参数。"
        },
        {"---@return boolean|nil exists"}
    ))
    add("Duel.SelectReleaseGroupEx", sig(
        "dot",
        "[协程挂起] `SelectReleaseGroup` 扩展版：可自定义 `reason` 与是否包含手牌 `useHand`。",
        {"player", "filter", "min", "max", "reason", "useHand", "excluded", "..."},
        {
            "---@param player Player 玩家编号。",
            "---@param filter FilterFunction|nil 过滤函数。",
            "---@param min integer 最少张数。",
            "---@param max integer 最多张数。",
            "---@param reason Reason 解放原因。",
            "---@param useHand boolean 是否包含手牌。",
            "---@param excluded Card|Group|nil 排除项（必填，无则传 `nil`）。",
            "---@param ... any 透传给 `filter` 的附加参数。"
        },
        {"---@return Group|nil selected"}
    ))
    add("Duel.GetTributeGroup", sig("dot", "取得通常召唤 `target` 时可作为祭品的卡片集合。", {"target"}, {"---@param target Card 要上位召唤的怪兽。"}, {"---@return Group group"}))
    add("Duel.GetTributeCount", sig("dot", "统计通常召唤 `target` 可提供的祭品当量（计入双祭品怪兽）。", {"target", "mg", "ex"}, {"---@param target Card 目标怪兽。", "---@param mg? Group|nil 限定祭品候选集合（默认 `nil`）。", "---@param ex? boolean 是否允许解放对方怪兽（默认 `false`）。"}, {"---@return integer count"}))
    add("Duel.CheckTribute", sig(
        "dot",
        "检查是否能为 `target` 凑齐 `min..max` 个上位召唤祭品并腾出 `zone`。",
        {"target", "min", "max", "mg", "toPlayer", "zone"},
        {
            "---@param target Card 要上位召唤的怪兽。",
            "---@param min integer 最少祭品数。",
            "---@param max? integer|nil 最多祭品数（默认等于 `min`）。",
            "---@param mg? Group|nil 限定祭品候选集合（默认 `nil`）。",
            "---@param toPlayer? Player|nil 召唤到哪方场上（默认 `target:GetControler()`）。",
            "---@param zone? Zone|nil 允许召唤到的格子掩码（默认 `0x1f`）。"
        },
        {"---@return boolean valid"}
    ))
    add("Duel.SelectTribute", sig(
        "dot",
        "[协程挂起] 让玩家选择 `min..max` 个用于上位召唤 `target` 的祭品。",
        {"player", "target", "min", "max", "mg", "toPlayer"},
        {
            "---@param player Player 执行选择的玩家（0 或 1）。",
            "---@param target Card 要上位召唤的怪兽。",
            "---@param min integer 最少祭品数。",
            "---@param max integer 最多祭品数。",
            "---@param mg? Group|nil 限定祭品候选集合（默认 `nil`）。",
            "---@param toPlayer? Player 召唤到哪方场上（默认 `player`）。"
        },
        {"---@return Group|nil selected"}
    ))
    add("Duel.GetTargetCount", sig(
        "dot",
        "统计指定区域中满足过滤条件且可作为当前效果取对象目标的卡片数量（返回 `integer`）。",
        {"filter", "player", "selfLocation", "opponentLocation", "excluded", "..."},
        {
            "---@param filter FilterFunction|nil 过滤函数。",
            "---@param player Player 区域视角玩家。",
            "---@param selfLocation Location 己方区域掩码。",
            "---@param opponentLocation Location 对方区域掩码。",
            "---@param excluded Card|Group|nil 排除项（必填，无则传 `nil`）。",
            "---@param ... any 透传给 `filter` 的附加参数。"
        },
        {"---@return integer count"}
    ))
    add("Duel.IsExistingTarget", sig(
        "dot",
        "检查指定区域中满足过滤条件且可作为当前效果对象的卡片是否至少达到 `count` 张。",
        {"filter", "player", "selfLocation", "opponentLocation", "count", "excluded", "..."},
        {
            "---@param filter FilterFunction|nil 过滤函数。",
            "---@param player Player 区域视角玩家。",
            "---@param selfLocation Location 己方区域掩码。",
            "---@param opponentLocation Location 对方区域掩码。",
            "---@param count integer 最少所需张数。",
            "---@param excluded Card|Group|nil 排除项（必填，无则传 `nil`）。",
            "---@param ... any 透传给 `filter` 的附加参数。"
        },
        {"---@return boolean exists"}
    ))
    add("Duel.SelectTarget", sig(
        "dot",
        "[协程挂起] 让玩家选择 `min..max` 张效果对象，自动登记到当前连锁 `target_cards` 并建立关联。",
        {"player", "filter", "selectPlayer", "selfLocation", "opponentLocation", "min", "max", "excluded", "..."},
        {
            "---@param player Player 执行选择的玩家（0 或 1）。",
            "---@param filter FilterFunction|nil 过滤函数。",
            "---@param selectPlayer Player 区域视角玩家。",
            "---@param selfLocation Location 视角方区域掩码。",
            "---@param opponentLocation Location 视角对手方区域掩码。",
            "---@param min integer 最少选择张数。",
            "---@param max integer 最多选择张数。",
            "---@param excluded Card|Group|nil 排除项（必填，无则传 `nil`）。",
            "---@param ... any 透传给 `filter` 的附加参数。"
        },
        {"---@return Group|nil selected"}
    ))

    -- =========================================================================
    -- 融合 / 同调 / 仪式 / 超量素材与连锁信息
    -- =========================================================================
    add("Duel.GetMustMaterial", sig("dot", "取得玩家受 `EFFECT_MUST_BE_*MATERIAL` 约束必须使用的素材卡片集合。", {"player", "limit"}, {"---@param player Player 玩家编号。", "---@param limit EffectCode 强制素材效果代码（如 `EFFECT_MUST_BE_SMATERIAL`）。"}, {"---@return Group|nil group"}))
    add("Duel.CheckMustMaterial", sig("dot", "检查候选素材 `materials` 是否包含全部必选素材。", {"player", "materials", "limit"}, {"---@param player Player 玩家编号。", "---@param materials Card|Group|nil 候选素材。", "---@param limit EffectCode 强制素材效果代码。"}, {"---@return boolean|nil valid"}))
    add("Duel.SelectFusionMaterial", sig(
        "dot",
        "[协程挂起] 让玩家从候选组 `mg` 中选择融合怪兽 `fc` 的融合素材（传入 `gc` 时自动并入结果）。",
        {"player", "fc", "mg", "gc", "chkf", "notMaterial"},
        {
            "---@param player Player 执行选择的玩家。",
            "---@param fc Card 目标融合怪兽。",
            "---@param mg Group 候选融合素材集合。",
            "---@param gc? Card|nil 必选素材卡（默认 `nil`）。",
            "---@param chkf? Player 检查玩家标志（默认 `PLAYER_NONE`）。",
            "---@param notMaterial? boolean 是否按非素材模式选择（默认 `false`）。"
        },
        {"---@return Group|nil selected"}
    ))
    add("Duel.SetFusionMaterial", sig("dot", "直接设置当前融合召唤流程使用的素材集合 `core.fusion_materials`。", {"group"}, {"---@param group Group 融合素材集合。"}, {"---@return nil"}))
    add("Duel.SetSynchroMaterial", sig("dot", "直接设置当前同调召唤流程使用的素材集合 `core.synchro_materials`。", {"group"}, {"---@param group Group 同调素材集合。"}, {"---@return nil"}))
    add("Duel.GetSynchroMaterial", sig("dot", "取得玩家可用于同调召唤的素材集合。", {"player", "facedown"}, {"---@param player Player 玩家编号。", "---@param facedown? boolean 是否包含怪兽区里侧卡（默认 `false`）。"}, {"---@return Group|nil group"}))
    add("Duel.SelectSynchroMaterial", sig(
        "dot",
        "[协程挂起] 让玩家选择同调召唤 `sc` 所需的素材集合（玩家取消召唤返回 `nil`；传入 `mg` 时会忽略 `smat`）。",
        {"player", "sc", "f1", "f2", "min", "max", "smat", "mg"},
        {
            "---@param player Player 执行选择的玩家。",
            "---@param sc Card 目标同调怪兽。",
            "---@param f1 FilterFunction|nil 调整过滤函数。",
            "---@param f2 FilterFunction|nil 非调整过滤函数。",
            "---@param min integer 非调整最少张数。",
            "---@param max integer 非调整最多张数。",
            "---@param smat? Card|nil 必选素材卡（传入 `mg` 时被忽略）。",
            "---@param mg? Group|nil 限定候选素材集合。"
        },
        {"---@return Group|nil selected"}
    ))
    add("Duel.CheckSynchroMaterial", sig(
        "dot",
        "检查是否存在满足同调怪兽 `sc` 条件的合法同调素材组合。",
        {"sc", "f1", "f2", "min", "max", "smat", "mg"},
        {
            "---@param sc Card 目标同调怪兽。",
            "---@param f1 FilterFunction|nil 调整过滤函数。",
            "---@param f2 FilterFunction|nil 非调整过滤函数。",
            "---@param min integer 非调整最少张数。",
            "---@param max integer 非调整最多张数。",
            "---@param smat? Card|nil 必选素材卡。",
            "---@param mg? Group|nil 限定候选素材集合。"
        },
        {"---@return boolean valid"}
    ))
    add("Duel.SelectTunerMaterial", sig(
        "dot",
        "[协程挂起] 固定指定 `tuner` 作为调整，让玩家选择其余同调素材（取消返回 `nil`）。",
        {"player", "sc", "tuner", "f1", "f2", "min", "max", "mg"},
        {
            "---@param player Player 执行选择的玩家。",
            "---@param sc Card 目标同调怪兽。",
            "---@param tuner Card 固定使用的调整怪兽。",
            "---@param f1 FilterFunction|nil 调整过滤函数。",
            "---@param f2 FilterFunction|nil 非调整过滤函数。",
            "---@param min integer 非调整最少张数。",
            "---@param max integer 非调整最多张数。",
            "---@param mg? Group|nil 限定候选素材集合。"
        },
        {"---@return Group|nil selected"}
    ))
    add("Duel.CheckTunerMaterial", sig(
        "dot",
        "检查固定以 `tuner` 为调整时能否凑齐同调怪兽 `sc` 所需素材。",
        {"sc", "tuner", "f1", "f2", "min", "max", "mg"},
        {
            "---@param sc Card 目标同调怪兽。",
            "---@param tuner Card 固定调整怪兽。",
            "---@param f1 FilterFunction|nil 调整过滤函数。",
            "---@param f2 FilterFunction|nil 非调整过滤函数。",
            "---@param min integer 非调整最少张数。",
            "---@param max integer 非调整最多张数。",
            "---@param mg? Group|nil 限定候选素材集合。"
        },
        {"---@return boolean valid"}
    ))
    add("Duel.GetRitualMaterial", sig("dot", "取得玩家用于仪式召唤（按等级）的解放素材集合。", {"player"}, {"---@param player Player 玩家编号。"}, {"---@return Group|nil group"}))
    add("Duel.GetRitualMaterialEx", sig("dot", "取得玩家用于非等级仪式召唤（如按攻击力仪式）的解放素材集合。", {"player"}, {"---@param player Player 玩家编号。"}, {"---@return Group|nil group"}))
    add("Duel.ReleaseRitualMaterial", sig("dot", "[协程挂起] 按所在区域结算仪式召唤素材（墓地除外、超量素材/额外送墓、其余解放）。", {"materials"}, {"---@param materials Group 仪式素材集合。"}, {"---@return nil"}))
    add("Duel.GetFusionMaterial", sig(
        "dot",
        "返回玩家可用于融合召唤的全部素材集合，以及仅来自基础区域的素材集合（双返回值）。",
        {"player", "location"},
        {
            "---@param player Player 玩家编号（0 或 1）。",
            "---@param location? Location 基础融合区域（默认 `LOCATION_HAND + LOCATION_MZONE`）。"
        },
        {
            "---@return Group|nil allMaterials 全部可用融合素材（含 `EFFECT_EXTRA_FUSION_MATERIAL`）。",
            "---@return Group|nil baseMaterials 仅基础区域的融合素材。"
        }
    ))
    add("Duel.IsSummonCancelable", sig("dot", "检查当前召唤流程是否允许玩家取消。", {}, {}, {"---@return boolean cancelable"}))
    add("Duel.SetSelectedCard", sig("dot", "设置 `core.must_select_cards` 必选卡片列表（供 `CheckWithSum*`/`CheckReleaseGroup` 等使用）。", {"cards"}, {"---@param cards Card|Group 必选单卡或集合。"}, {"---@return nil"}))
    add("Duel.GrabSelectedCard", sig("dot", "取出并清空 `core.must_select_cards` 返回为新集合。", {}, {}, {"---@return Group group"}))

    add("Duel.SetTargetCard", sig("dot", "将卡片或集合设为当前连锁的对象卡片并建立连锁关联。", {"targets"}, {"---@param targets Card|Group 对象单卡或集合。"}, {"---@return nil"}))
    add("Duel.ClearTargetCard", sig("dot", "清空当前连锁记录的全部对象卡片。", {}, {}, {"---@return nil"}))
    add("Duel.SetTargetPlayer", sig("dot", "设置当前连锁的目标玩家（`CHAININFO_TARGET_PLAYER`）。", {"player"}, {"---@param player Player 目标玩家编号。"}, {"---@return nil"}))
    add("Duel.SetTargetParam", sig("dot", "设置当前连锁的目标参数（`CHAININFO_TARGET_PARAM`）。", {"param"}, {"---@param param integer 整型参数值。"}, {"---@return nil"}))
    add("Duel.SetOperationInfo", sig(
        "dot",
        "为指定连锁项登记处理预报信息（用于灰流丽/星尘龙等判定效果分类与预期目标）。",
        {"chainIndex", "category", "targets", "count", "targetPlayer", "targetParam"},
        {
            "---@param chainIndex integer 连锁序号（`0` 为当前构造中的连锁）。",
            "---@param category EffectCategory 效果分类位掩码（`CATEGORY_*`）。",
            "---@param targets Card|Group|nil 预期目标单卡或集合（不确定时传 `nil`）。",
            "---@param count integer 预期数量。",
            "---@param targetPlayer Player 预期目标玩家。",
            "---@param targetParam integer 预期参数（如区域或伤害值）。"
        },
        {"---@return nil"}
    ))
    add("Duel.GetOperationInfo", sig(
        "dot",
        "查询指定连锁项是否登记了某 `category` 的处理预报信息；命中返回 5 个值，未命中返回单个 `false`。",
        {"chainIndex", "category"},
        {"---@param chainIndex integer 连锁序号。", "---@param category EffectCategory 要查询的效果分类。"},
        {
            "---@return boolean|nil found 是否存在该分类记录（连锁不存在无返回值）。",
            "---@return Group|nil targets 登记的目标集合。",
            "---@return integer? count 登记的数量。",
            "---@return Player? targetPlayer 登记的目标玩家。",
            "---@return integer? targetParam 登记的目标参数。"
        }
    ))
    add("Duel.GetOperationCount", sig("dot", "取得指定连锁项已登记的 OperationInfo 条目数。", {"chainIndex"}, {"---@param chainIndex integer 连锁序号。"}, {"---@return integer|nil count"}))
    add("Duel.ClearOperationInfo", sig("dot", "清空并释放指定连锁项的全部 OperationInfo。", {"chainIndex"}, {"---@param chainIndex integer 连锁序号。"}, {"---@return nil"}))

    add("Duel.CheckXyzMaterial", sig(
        "dot",
        "检查是否存在满足超量怪兽 `sc` 条件的合法超量素材（注意第 6 参 `mg` 必传，不限定时显式传 `nil`）。",
        {"sc", "filter", "lv", "minc", "maxc", "mg"},
        {
            "---@param sc Card 目标超量怪兽。",
            "---@param filter FilterFunction|nil 素材过滤函数。",
            "---@param lv integer 所需超量素材等级。",
            "---@param minc integer 最少素材数。",
            "---@param maxc integer 最多素材数。",
            "---@param mg Group|nil 限定候选素材集合（必填，不限定则显式传 `nil`）。"
        },
        {"---@return boolean valid"}
    ))
    add("Duel.SelectXyzMaterial", sig(
        "dot",
        "[协程挂起] 让玩家选择超量怪兽 `sc` 的素材（玩家取消返回 `nil`；第 7 参 `mg` 可省略）。",
        {"player", "sc", "filter", "lv", "minc", "maxc", "mg"},
        {
            "---@param player Player 执行选择的玩家。",
            "---@param sc Card 目标超量怪兽。",
            "---@param filter FilterFunction|nil 素材过滤函数。",
            "---@param lv integer 所需等级。",
            "---@param minc integer 最少素材数。",
            "---@param maxc integer 最多素材数。",
            "---@param mg? Group|nil 限定候选素材集合（默认 `nil`）。"
        },
        {"---@return Group|nil selected"}
    ))
    add("Duel.Overlay", sig(
        "dot",
        "[协程挂起] 将卡片或集合重叠到超量怪兽 `target` 下方作为超量素材。",
        {"target", "materials", "adjust"},
        {
            "---@param target Card 目标超量怪兽。",
            "---@param materials Card|Group 要叠放的单卡或集合。",
            "---@param adjust? boolean 叠放后是否触发 `adjust_all`（默认 `true`）。"
        },
        {"---@return nil"}
    ))
    add("Duel.GetOverlayGroup", sig("dot", "取得指定区域上的全部超量素材集合。", {"player", "selfLocation", "opponentLocation"}, {"---@param player Player 视角玩家。", "---@param selfLocation Location 己方区域。", "---@param opponentLocation Location 对方区域。"}, {"---@return Group|nil group"}))
    add("Duel.GetOverlayCount", sig("dot", "取得指定区域上的超量素材总数。", {"player", "selfLocation", "opponentLocation"}, {"---@param player Player 视角玩家。", "---@param selfLocation Location 己方区域。", "---@param opponentLocation Location 对方区域。"}, {"---@return integer|nil count"}))
    add("Duel.CheckRemoveOverlayCard", sig("dot", "检查玩家能否从指定区域取除至少 `count` 个超量素材。", {"player", "selfLocation", "opponentLocation", "count", "reason"}, {"---@param player Player 玩家编号。", "---@param selfLocation Location 己方区域。", "---@param opponentLocation Location 对方区域。", "---@param count integer 最少张数。", "---@param reason Reason 原因。"}, {"---@return boolean|nil canRemove"}))
    add("Duel.RemoveOverlayCard", sig("dot", "[协程挂起] 让玩家从指定区域取除 `min..max` 个超量素材，返回实际取除数。", {"player", "selfLocation", "opponentLocation", "min", "max", "reason"}, {"---@param player Player 玩家编号。", "---@param selfLocation Location 己方区域。", "---@param opponentLocation Location 对方区域。", "---@param min integer 最少取除数。", "---@param max integer 最多取除数。", "---@param reason Reason 原因。"}, {"---@return integer|nil removed"}))

    -- =========================================================================
    -- 提示、交互选择、宣言、硬币/骰子/猜拳
    -- =========================================================================
    add("Duel.Hint", sig(
        "dot",
        "向客户端发送 `MSG_HINT` 提示消息（`description` 必须为整型 ID，如 `HINTMSG_*` 或 `aux.Stringid(...)`）。",
        {"hintType", "player", "description"},
        {
            "---@param hintType Hint 提示类型（`HINT_*`）。",
            "---@param player Player 玩家编号（`HINT_OPSELECTED` 会自动转为通知对手 `1 - player`）。",
            "---@param description integer 提示文本或卡号整数值。"
        },
        {"---@return nil"}
    ))
    add("Duel.GetLastSelectHint", sig("dot", "读取玩家最近一次收到的 `HINT_SELECTMSG` 提示值。", {"player"}, {"---@param player? Player 玩家编号（默认当前 `reason_player`）。"}, {"---@return integer|nil hint"}))
    add("Duel.HintSelection", sig("dot", "向客户端发送 `MSG_BECOME_TARGET` 高亮提示所选非取对象卡片集合。", {"group"}, {"---@param group Group 要高亮提示的卡片集合。"}, {"---@return nil"}))
    add("Duel.SelectEffectYesNo", sig("dot", "[协程挂起] 询问玩家是否适用/发动卡片 `card` 的效果。", {"player", "card", "description"}, {"---@param player Player 玩家编号。", "---@param card Card 关联卡片。", "---@param description? integer 提示文本 ID（默认 `95`）。"}, {"---@return boolean|nil yes"}))
    add("Duel.SelectYesNo", sig("dot", "[协程挂起] 向玩家弹出是否确认框（`description` 为整型描述 ID）。", {"player", "description"}, {"---@param player Player 玩家编号。", "---@param description integer 提示文本 ID。"}, {"---@return boolean|nil yes"}))
    add("Duel.SelectOption", sig(
        "dot",
        "[协程挂起] 向玩家展示 1..N 个文本描述选项并返回选中的 0-based 索引（自动向对手广播 `HINT_OPSELECTED`）。",
        {"player", "..."},
        {"---@param player Player 玩家编号。", "---@param ... integer 选项描述文本 ID 列表。"},
        {"---@return integer|nil optionIndex 0-based 选中索引。"}
    ))
    add("Duel.SelectSequence", sig("dot", "保留空占位函数（仅检查操作权限，无参数无返回值）。", {}, {}, {"---@return nil"}))
    add("Duel.SelectPosition", sig("dot", "[协程挂起] 让玩家为卡片 `card` 从候选表示形式掩码中选择一种表示形式。", {"player", "card", "positions"}, {"---@param player Player 玩家编号。", "---@param card Card 目标卡片。", "---@param positions Position 可选表示形式位掩码。"}, {"---@return Position position"}))
    add("Duel.SelectDisableField", sig("dot", "[协程挂起] 让玩家从当前可用空格中选择 `count` 个格子并返回格子位掩码。", {"player", "count", "selfLocation", "opponentLocation", "filter"}, {"---@param player Player 玩家编号。", "---@param count integer 需选择的格子数。", "---@param selfLocation Location 己方区域。", "---@param opponentLocation Location 对方区域。", "---@param filter Zone 排除/禁止选择的格子位掩码。"}, {"---@return Zone|nil zone"}))
    add("Duel.SelectField", sig(
        "dot",
        "[协程挂起] 按玩家视角任选 `count` 个格子（不要求空闲）；传入 `hintCode` 时切换为 `PROCESSOR_SELECT_PLACE`。",
        {"player", "count", "selfLocation", "opponentLocation", "filter", "hintCode"},
        {
            "---@param player Player 玩家编号。",
            "---@param count integer 格子数。",
            "---@param selfLocation Location 己方区域。",
            "---@param opponentLocation Location 对方区域。",
            "---@param filter Zone 排除的格子掩码。",
            "---@param hintCode? integer 可选：发往 `HINT_SELECTMSG` 的提示卡号/ID。"
        },
        {"---@return Zone|nil zone"}
    ))

    add("Duel.AnnounceRace", sig("dot", "[协程挂起] 让玩家从 `available` 掩码中宣言 `count` 个种族。", {"player", "count", "available"}, {"---@param player Player 玩家编号。", "---@param count integer 宣言数量。", "---@param available Race 可选种族位掩码。"}, {"---@return Race race"}))
    add("Duel.AnnounceAttribute", sig("dot", "[协程挂起] 让玩家从 `available` 掩码中宣言 `count` 个属性。", {"player", "count", "available"}, {"---@param player Player 玩家编号。", "---@param count integer 宣言数量。", "---@param available Attribute 可选属性位掩码。"}, {"---@return Attribute attribute"}))
    add("Duel.AnnounceLevel", sig(
        "dot",
        "[协程挂起] 让玩家在 `[min, max]` 区间内宣言一个等级，返回 `(等级值, 0-based 索引)` 双值。",
        {"player", "min", "max", "..."},
        {
            "---@param player Player 玩家编号。",
            "---@param min? integer|nil 最小等级（默认 `1`）。",
            "---@param max? integer|nil 最大等级（默认 `12`）。",
            "---@param ... integer|nil 额外排除的等级值（`nil` 忽略）。"
        },
        {"---@return integer|nil level 宣言的等级值。", "---@return integer|nil index 选项索引。"}
    ))
    add("Duel.AnnounceCard", sig(
        "dot",
        "[协程挂起] 按可选后缀表达式（`OPCODE_*`）过滤后让玩家宣言一个卡名，返回所宣言卡号。",
        {"player", "..."},
        {"---@param player Player 玩家编号。", "---@param ... Opcode|integer 可选后缀表达式过滤指令流（省略默认允许全部卡）。"},
        {"---@return CardCode code"}
    ))
    add("Duel.AnnounceType", sig("dot", "[协程挂起] 让玩家宣言卡片种类（`0`=怪兽、`1`=魔法、`2`=陷阱）。", {"player"}, {"---@param player Player 玩家编号。"}, {"---@return integer cardKind 0=怪兽，1=魔法，2=陷阱。"}))
    add("Duel.AnnounceNumber", sig(
        "dot",
        "[协程挂起] 让玩家从候选整数列表中宣言一个数字，返回 `(选中数值, 0-based 索引)` 双值。",
        {"player", "num", "..."},
        {"---@param player Player 玩家编号。", "---@param num integer 首个候选数字。", "---@param ... integer 其余候选数字。"},
        {"---@return integer value 选中的数字。", "---@return integer index 选中的 0-based 索引。"}
    ))
    add("Duel.AnnounceCoin", sig("dot", "[协程挂起] 让玩家宣言硬币正反面（`0`=正面/表、`1`=反面/里）。", {"player"}, {"---@param player Player 玩家编号。"}, {"---@return integer side 0=正面，1=反面。"}))
    add("Duel.TossCoin", sig("dot", "[协程挂起] 掷 `count` 枚硬币（`-1` 为特殊标记），多值返回每枚硬币结果（`1`=正、`0`=反）。", {"player", "count"}, {"---@param player Player 玩家编号。", "---@param count integer 硬币数量。"}, {"---@return integer ... 硬币结果列表。"}))
    add("Duel.TossDice", sig("dot", "[协程挂起] 掷骰子（己方 `count1` 个、对方 `count2` 个，合计上限 5），多值返回各骰子点数。", {"player", "count1", "count2"}, {"---@param player Player 玩家编号。", "---@param count1 integer 己方骰子数。", "---@param count2? integer 对方骰子数（默认 `0`）。"}, {"---@return integer ... 骰子点数列表。"}))
    add("Duel.RockPaperScissors", sig("dot", "[协程挂起] 让双方猜拳，返回获胜玩家编号。", {"repeatOnDraw"}, {"---@param repeatOnDraw? boolean 平局是否重来（默认 `true`）。"}, {"---@return Player winner"}))
    add("Duel.GetCoinResult", sig("dot", "多值返回最近一次掷硬币的全部结果。", {}, {}, {"---@return integer ... 硬币结果列表。"}))
    add("Duel.GetDiceResult", sig("dot", "固定返回最近一次掷骰子的 5 个槽位点数。", {}, {}, {"---@return integer d1", "---@return integer d2", "---@return integer d3", "---@return integer d4", "---@return integer d5"}))
    add("Duel.SetCoinResult", sig("dot", "覆写最近一次掷硬币结果（非 0/1 归一化为 `0`）。", {"..."}, {"---@param ... integer 硬币结果（最多读取 `MAX_COIN_COUNT` 个）。"}, {"---@return nil"}))
    add("Duel.SetDiceResult", sig("dot", "覆写最近一次掷骰子结果（最多 5 个，超出 `1..255` 归一化为 `1`）。", {"..."}, {"---@param ... integer 骰子点数（最多 5 个）。"}, {"---@return nil"}))

    -- =========================================================================
    -- 玩家效果 / 行为权限判定 / 连锁状态 / 行动计数 / 特殊工具
    -- =========================================================================
    add("Duel.IsPlayerAffectedByEffect", sig(
        "dot",
        "多值返回作用于指定玩家且满足次数限制的全部匹配 `code` 效果；无匹配返回 `nil`。",
        {"player", "code"},
        {"---@param player Player 玩家编号（0 或 1）。", "---@param code EffectCode 效果代码。"},
        {"---@return Effect ... 匹配的效果列表；无匹配返回 `nil`。"}
    ))
    add("Duel.IsPlayerCanDraw", sig("dot", "检查玩家能否抽卡（`count > 0` 时同时检查主卡组张数 `>= count`）。", {"player", "count"}, {"---@param player Player 玩家编号。", "---@param count? integer 抽卡张数（默认 `0` 仅查禁止抽卡效果）。"}, {"---@return boolean allowed"}))
    add("Duel.IsPlayerCanDiscardDeck", sig("dot", "检查玩家能否将主卡组顶 `count` 张卡送去墓地。", {"player", "count"}, {"---@param player Player 玩家编号。", "---@param count integer 张数。"}, {"---@return boolean allowed"}))
    add("Duel.IsPlayerCanDiscardDeckAsCost", sig("dot", "检查玩家能否作为代价将主卡组顶 `count` 张卡送去墓地。", {"player", "count"}, {"---@param player Player 玩家编号。", "---@param count integer 张数。"}, {"---@return boolean allowed"}))

    add("Duel.IsPlayerCanSummon", sig(
        "dot",
        "检查玩家能否通常召唤（1 参查整体 `EFFECT_CANNOT_SUMMON`；3 参查能否以 `summonType` 通常召唤 `card`）。",
        {"player", "summonType", "card"},
        {"---@param player Player 玩家编号。", "---@param summonType? SummonType 可选：召唤类型。", "---@param card? Card 可选：目标怪兽（传第 2 参时必传）。"},
        {"---@return boolean allowed"},
        nil,
        {
            "---@overload fun(player: Player): boolean",
            "---@overload fun(player: Player, summonType: SummonType, card: Card): boolean"
        }
    ))
    add("Duel.IsPlayerCanMSet", sig(
        "dot",
        "检查玩家能否通常盖放怪兽（1 参查整体 `EFFECT_CANNOT_MSET`；3 参查指定怪兽）。",
        {"player", "summonType", "card"},
        {"---@param player Player 玩家编号。", "---@param summonType? SummonType 可选：召唤类型。", "---@param card? Card 可选：目标怪兽（传第 2 参时必传）。"},
        {"---@return boolean allowed"},
        nil,
        {
            "---@overload fun(player: Player): boolean",
            "---@overload fun(player: Player, summonType: SummonType, card: Card): boolean"
        }
    ))
    add_player_can_card_overload("IsPlayerCanSSet", "检查玩家能否盖放魔陷卡")
    add("Duel.IsPlayerCanSpecialSummon", sig(
        "dot",
        "检查玩家能否特殊召唤（1 参查整体权限；5 参查能否按指定参数特召 `card`）。",
        {"player", "summonType", "position", "toPlayer", "card"},
        {
            "---@param player Player 玩家编号。",
            "---@param summonType? SummonType 可选：召唤类型。",
            "---@param position? Position 可选：表示形式。",
            "---@param toPlayer? Player 可选：召唤目标玩家。",
            "---@param card? Card 可选：目标怪兽（传 >=2 参时必须提供全部 5 参）。"
        },
        {"---@return boolean allowed"},
        nil,
        {
            "---@overload fun(player: Player): boolean",
            "---@overload fun(player: Player, summonType: SummonType, position: Position, toPlayer: Player, card: Card): boolean"
        }
    ))
    add_player_can_card_overload("IsPlayerCanFlipSummon", "检查玩家能否反转召唤")
    add("Duel.IsPlayerCanSpecialSummonMonster", sig(
        "dot",
        "检查玩家能否将虚拟属性（衍生物/陷阱怪兽等）按指定参数特殊召唤（3~9 参为 `nil` 时沿用 `code` 的卡库默认值）。",
        {"player", "code", "setcode", "cardType", "atk", "def", "level", "race", "attribute", "position", "toPlayer", "summonType"},
        {
            "---@param player Player 执行召唤的玩家。",
            "---@param code CardCode 卡号（用于读取卡库初始值）。",
            "---@param setcode? SetCode|nil 覆写字段编码。",
            "---@param cardType? CardType|nil 覆写种类。",
            "---@param atk? integer|nil 覆写攻击力。",
            "---@param def? integer|nil 覆写守备力。",
            "---@param level? integer|nil 覆写等级。",
            "---@param race? Race|nil 覆写种族。",
            "---@param attribute? Attribute|nil 覆写属性。",
            "---@param position? Position 表示形式（默认 `POS_FACEUP`）。",
            "---@param toPlayer? Player 召唤到哪方场上（默认 `player`）。",
            "---@param summonType? SummonType 召唤类型（默认 `0`）。"
        },
        {"---@return boolean allowed"}
    ))
    add("Duel.IsPlayerCanSpecialSummonCount", sig("dot", "检查玩家本回合剩余特召次数上限是否允许再特召 `count` 次。", {"player", "count"}, {"---@param player Player 玩家编号。", "---@param count integer 需要的特召次数。"}, {"---@return boolean allowed"}))
    add_player_can_card_reason_overload("IsPlayerCanRelease", "检查玩家能否解放", "REASON_COST")
    add_player_can_card_reason_overload("IsPlayerCanRemove", "检查玩家能否除外", "REASON_EFFECT")
    add_player_can_card_overload("IsPlayerCanSendtoHand", "检查玩家能否将卡片加入手牌")
    add_player_can_card_overload("IsPlayerCanSendtoGrave", "检查玩家能否将卡片送去墓地")
    add_player_can_card_overload("IsPlayerCanSendtoDeck", "检查玩家能否将卡片送回卡组")
    add("Duel.IsPlayerCanAdditionalSummon", sig("dot", "检查玩家本回合是否尚未使用额外通常召唤标记。", {"player"}, {"---@param player Player 玩家编号。"}, {"---@return boolean allowed"}))

    add("Duel.IsChainSolving", sig("dot", "检查当前是否正处于连锁逆序结算阶段。", {}, {}, {"---@return boolean solving"}))
    add("Duel.IsChainNegatable", sig("dot", "历史兼容接口：检查连锁能否无效发动（要求 1 参，原生层恒返回 `true`）。", {"chainIndex"}, {"---@param chainIndex integer 连锁序号（未使用）。"}, {"---@return boolean negatable"}))
    add("Duel.IsChainDisablable", sig("dot", "在连锁结算期间检查指定连锁项的效果能否被无效（非结算期恒返回 `true`）。", {"chainIndex"}, {"---@param chainIndex integer 连锁序号。"}, {"---@return boolean disablable"}))
    add("Duel.IsChainDisabled", sig("dot", "在连锁结算期间检查指定连锁项是否已被无效（非结算期恒返回 `false`）。", {"chainIndex"}, {"---@param chainIndex integer 连锁序号。"}, {"---@return boolean disabled"}))
    add("Duel.CheckChainTarget", sig("dot", "检查卡片 `card` 是否可以成为指定连锁项的合法转移对象。", {"chainIndex", "card"}, {"---@param chainIndex integer 连锁序号。", "---@param card Card 候选新对象卡片。"}, {"---@return boolean valid"}))
    add("Duel.CheckChainUniqueness", sig("dot", "检查当前连锁串中是否不存在同名卡发动（空连锁返回 `true`）。", {}, {}, {"---@return boolean unique"}))
    add("Duel.GetActivityCount", sig(
        "dot",
        "查询玩家本回合一项或多项内置行动计数（`ACTIVITY_SUMMON=1`、`NORMALSUMMON=2`、`SPSUMMON=3`、`FLIPSUMMON=4`、`ATTACK=5`、`BATTLE_PHASE=6`）。",
        {"player", "activityType", "..."},
        {
            "---@param player Player 玩家编号（0 或 1）。",
            "---@param activityType ActivityType 首个行动类型常量。",
            "---@param ... ActivityType 更多要同时查询的行动类型。"
        },
        {"---@return integer ... 与输入行动类型一一对应的计数值。"}
    ))
    add("Duel.CheckPhaseActivity", sig("dot", "检查当前阶段是否已经发生过玩家行动。", {}, {}, {"---@return boolean active"}))
    add("Duel.AddCustomActivityCounter", sig(
        "dot",
        "注册自定义行动计数器（用于“此卡发动的回合，自己不是 XX 怪兽不能特召”等誓约判定）。",
        {"counterId", "activityType", "filter"},
        {
            "---@param counterId integer 计数器 ID（通常为卡号）。",
            "---@param activityType ActivityType 监听的行动类型（支持 `SUMMON`/`NORMALSUMMON`/`SPSUMMON`/`FLIPSUMMON`/`ATTACK`/`CHAIN`）。",
            "---@param filter FilterFunction|EffectFilterFunction 满足条件时不计入违约的过滤函数（`ACTIVITY_CHAIN` 时回调参数为 `Effect`，其余为 `Card`）。"
        },
        {"---@return nil"}
    ))
    add("Duel.GetCustomActivityCount", sig(
        "dot",
        "查询指定自定义行动计数器的本回合违约计数。",
        {"counterId", "player", "activityType"},
        {
            "---@param counterId integer 计数器 ID。",
            "---@param player Player 玩家编号（0 或 1）。",
            "---@param activityType ActivityType 行动类型。"
        },
        {"---@return integer count"}
    ))
    add("Duel.GetBattledCount", sig("dot", "取得玩家本回合怪兽进行战斗的次数。", {"player"}, {"---@param player Player 玩家编号（0 或 1）。"}, {"---@return integer|nil count"}))
    add("Duel.IsAbleToEnterBP", sig("dot", "检查当前回合是否允许进入战斗阶段。", {}, {}, {"---@return boolean able"}))
    add("Duel.SwapDeckAndGrave", sig("dot", "互换指定玩家的主卡组与墓地（现世与冥界的逆转专用）。", {"player"}, {"---@param player Player 玩家编号。"}, {"---@return nil"}))
    add("Duel.MajesticCopy", sig(
        "dot",
        "救世星龙/救世魔龙专用：克隆 `copyTarget` 的初始怪兽入连锁效果给 `dst` 至回合结束（必发转选发）。",
        {"dst", "copyTarget"},
        {"---@param dst Card 接收效果的卡片。", "---@param copyTarget Card 被复制效果的源怪兽。"},
        {"---@return nil"}
    ))
end

return { register = register }
