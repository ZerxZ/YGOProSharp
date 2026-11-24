-- Debug 库全部 11 个 native Lua API 精确签名（对照 ygopro-core/libdebug.cpp）

function register(add, sig)
    add("Debug.Message", sig(
        "dot",
        "将任意 Lua 值经 `tostring` 转换后作为调试消息（message_type=2）发送给宿主。",
        {"message"},
        {"---@param message any 任意可转为字符串的调试内容。"},
        {"---@return nil"}
    ))

    add("Debug.AddCard", sig(
        "dot",
        "在残局/调试脚本中向指定区域直接放置一张卡片；若目标怪兽区已有超量怪兽则附加为其超量素材。",
        {"code", "owner", "player", "location", "sequence", "position", "proc"},
        {
            "---@param code CardCode 卡号。",
            "---@param owner Player 卡片持有者（0 或 1）。",
            "---@param player Player 放置目标玩家（0 或 1）。",
            "---@param location Location 目标区域（支持 `LOCATION_PZONE` 自动映射灵摆格）。",
            "---@param sequence integer 区域内序号。",
            "---@param position Position 表示形式（`LOCATION_EXTRA` 且传 `0` 时自动置为 `POS_FACEDOWN_DEFENSE`）。",
            "---@param proc? boolean 是否同时设置 `STATUS_PROC_COMPLETE`（视为已完成正规召唤手续，默认 `false`）。"
        },
        {"---@return Card|nil card 成功创建并放置时返回新卡片；玩家或目标格子非法时无返回值（`nil`）。"}
    ))

    add("Debug.SetPlayerInfo", sig(
        "dot",
        "设置调试对局中玩家的 LP、初手张数与每回合通常抽卡数。",
        {"player", "lp", "startCount", "drawCount"},
        {
            "---@param player Player 玩家编号（0 或 1）。",
            "---@param lp integer 初始生命值。",
            "---@param startCount integer 初始手牌张数。",
            "---@param drawCount integer 每回合通常抽卡张数。"
        },
        {"---@return nil"}
    ))

    add("Debug.PreSummon", sig(
        "dot",
        "为已放置的卡片预先写入召唤方式与召唤前所在区域（用于残局环境模拟）。",
        {"card", "summonType", "summonLocation"},
        {
            "---@param card Card 目标卡片。",
            "---@param summonType SummonType 召唤类型常量（如 `SUMMON_TYPE_SYNCHRO`）。",
            "---@param summonLocation? Location 召唤前所在区域（默认 `0`）。"
        },
        {"---@return nil"}
    ))

    add("Debug.PreEquip", sig(
        "dot",
        "在残局初始化阶段将魔陷区的装备卡预先装备给场上表侧怪兽。",
        {"equipCard", "target"},
        {
            "---@param equipCard Card 位于 `LOCATION_SZONE` 的装备卡。",
            "---@param target Card 位于 `LOCATION_MZONE` 且为表侧表示的装备目标怪兽。"
        },
        {"---@return boolean success 满足位置与表侧条件并成功装备返回 `true`，否则返回 `false`。"}
    ))

    add("Debug.PreSetTarget", sig(
        "dot",
        "在残局初始化阶段预先建立 `card` 指向 `target` 的持续取对象关联。",
        {"card", "target"},
        {
            "---@param card Card 取对象来源卡片。",
            "---@param target Card 被持续取对象的目标卡片。"
        },
        {"---@return nil"}
    ))

    add("Debug.PreAddCounter", sig(
        "dot",
        "在残局初始化阶段直接向卡片累加指定种类的指示物（不经过效果检查）。",
        {"card", "counterType", "count"},
        {
            "---@param card Card 目标卡片。",
            "---@param counterType CounterType 指示物类型编码。",
            "---@param count? integer 放置数量（默认 `1`）。"
        },
        {"---@return nil"}
    ))

    add("Debug.ReloadFieldBegin", sig(
        "dot",
        "清空当前对局状态并开始残局重载，配置对局选项与大师规则版本。",
        {"flag", "rule"},
        {
            "---@param flag DuelOption 对局选项位掩码（`DUEL_*`）。",
            "---@param rule? integer 大师规则编号（省略或 `0` 时：含 `DUEL_OBSOLETE_RULING` 取 `1`，否则取最新规则 `5`）。"
        },
        {"---@return nil"}
    ))

    add("Debug.ReloadFieldEnd", sig(
        "dot",
        "结束残局重载并向客户端广播完整场地快照（`MSG_RELOAD_FIELD`）。",
        {},
        {},
        {"---@return nil"}
    ))

    add("Debug.SetAIName", sig(
        "dot",
        "向客户端发送 `MSG_AI_NAME` 设置残局/AI 对手名称（最长截断至 127 字节）。",
        {"name"},
        {"---@param name string AI 名称字符串。"},
        {"---@return nil"}
    ))

    add("Debug.ShowHint", sig(
        "dot",
        "将任意值经 `tostring` 转换后作为 `MSG_SHOW_HINT` 提示框文本发送给客户端（最长截断至 1023 字节）。",
        {"message"},
        {"---@param message any 提示内容。"},
        {"---@return nil"}
    ))
end

return { register = register }
