local generator = {}

local tables = {
    { name = "Card", table = "cardlib", source = "libcard.cpp", style = "colon" },
    { name = "Effect", table = "effectlib", source = "libeffect.cpp", style = "colon" },
    { name = "Group", table = "grouplib", source = "libgroup.cpp", style = "colon" },
    { name = "Duel", table = "duellib", source = "libduel.cpp", style = "dot" },
    { name = "Debug", table = "debuglib", source = "libdebug.cpp", style = "dot" }
}

local function path_join(...)
    if path and path.join then
        return path.join(...)
    end

    local sep = (package and package.config and package.config:sub(1, 1)) or "/"
    local parts = {...}
    local result = tostring(parts[1] or "")
    for i = 2, #parts do
        local part = tostring(parts[i] or "")
        if part ~= "" then
            if result == "" then
                result = part
            elseif result:sub(-1) == "/" or result:sub(-1) == "\\" then
                result = result .. part
            else
                result = result .. sep .. part
            end
        end
    end
    return result
end

local function read_file(file)
    local handle, err = io.open(file, "rb")
    if not handle then
        error(("cannot read %s: %s"):format(file, err or "unknown error"))
    end
    local data = handle:read("*a")
    handle:close()
    return data
end

local function write_file(file, data)
    local dir = file:match("^(.*)[/\\][^/\\]+$")
    if dir and dir ~= "" then
        os.mkdir(dir)
    end
    local handle, err = io.open(file, "wb")
    if not handle then
        error(("cannot write %s: %s"):format(file, err or "unknown error"))
    end
    handle:write(data)
    handle:close()
end

local function same_file(file, data)
    local handle = io.open(file, "rb")
    if not handle then
        return false
    end
    local current = handle:read("*a")
    handle:close()
    return current == data
end

local function signature(style, description, args, params, returns, notes)
    return {
        style = style,
        description = description,
        args = args or {},
        params = params or {},
        returns = returns or {},
        notes = notes or {}
    }
end

local exact = {}
local function add_exact(key, sig)
    exact[key] = sig
end

local function add_getters(prefix, names, description, returns)
    for _, name in ipairs(names) do
        add_exact(prefix .. "." .. name, signature("colon", description:gsub("{name}", name), {}, {}, returns))
    end
end

local function add_predicates(prefix, names, description)
    for _, name in ipairs(names) do
        add_exact(prefix .. "." .. name, signature("colon", description:gsub("{name}", name), {"..."}, {"---@param ... any"}, {"---@return boolean result"}))
    end
end

add_exact("Effect.CreateEffect", signature("dot", "创建绑定到处理者卡片的效果对象（Effect object）。", {"handler"}, {"---@param handler Card 效果处理者卡片。"}, {"---@return Effect effect 新效果对象。"}))
add_exact("Effect.GlobalEffect", signature("dot", "创建全局效果对象（global Effect）。", {}, {}, {"---@return Effect effect 全局效果对象。"}))
add_exact("Effect.Clone", signature("colon", "克隆当前效果对象。", {}, {}, {"---@return Effect effect 克隆出的效果对象。"}))
add_exact("Effect.Reset", signature("colon", "重置当前效果对象。", {}, {}, {"---@return nil"}))
add_exact("Effect.GetFieldID", signature("colon", "取得效果在 duel 内部的 field id。", {}, {}, {"---@return integer fieldId"}))
add_exact("Effect.SetDescription", signature("colon", "设置效果描述文本 id 或描述值。", {"description"}, {"---@param description integer|string 描述 id 或文本。"}, {"---@return nil"}))
add_exact("Effect.SetCode", signature("colon", "设置效果代码（EffectCode）。", {"code"}, {"---@param code EffectCode"}, {"---@return nil"}))
add_exact("Effect.SetRange", signature("colon", "设置效果适用位置范围。", {"range"}, {"---@param range Location"}, {"---@return nil"}))
add_exact("Effect.SetTargetRange", signature("colon", "设置双方目标范围。", {"selfRange", "opponentRange"}, {"---@param selfRange Location", "---@param opponentRange Location"}, {"---@return nil"}))
add_exact("Effect.SetAbsoluteRange", signature("colon", "按指定玩家视角设置绝对范围。", {"player", "selfRange", "opponentRange"}, {"---@param player Player", "---@param selfRange Location", "---@param opponentRange Location"}, {"---@return nil"}))
add_exact("Effect.SetCountLimit", signature("colon", "设置每回合/每局次数限制。", {"count", "code", "flags"}, {"---@param count integer", "---@param code? integer", "---@param flags? integer"}, {"---@return nil"}))
add_exact("Effect.SetReset", signature("colon", "设置 reset 条件和次数。", {"reset", "count"}, {"---@param reset Reset", "---@param count? integer"}, {"---@return nil"}))
add_exact("Effect.SetType", signature("colon", "设置效果类型（EffectType）。", {"type"}, {"---@param type EffectType"}, {"---@return nil"}))
add_exact("Effect.SetProperty", signature("colon", "设置效果属性 bitmask。", {"property"}, {"---@param property EffectProperty"}, {"---@return nil"}))
add_exact("Effect.SetLabel", signature("colon", "保存一个或多个整型 label。", {"..."}, {"---@param ... integer"}, {"---@return nil"}))
add_exact("Effect.SetLabelObject", signature("colon", "保存关联对象 label object。", {"object"}, {"---@param object Card|Effect|Group|nil"}, {"---@return nil"}))
add_exact("Effect.SetCategory", signature("colon", "设置效果分类（EffectCategory）。", {"category"}, {"---@param category EffectCategory"}, {"---@return nil"}))
add_exact("Effect.SetHintTiming", signature("colon", "设置提示时点（hint timing）。", {"selfTiming", "opponentTiming"}, {"---@param selfTiming integer", "---@param opponentTiming? integer"}, {"---@return nil"}))
add_exact("Effect.SetCondition", signature("colon", "设置发动/适用条件回调。", {"condition"}, {"---@param condition ConditionFunction"}, {"---@return nil"}))
add_exact("Effect.SetTarget", signature("colon", "设置目标选择回调。", {"target"}, {"---@param target TargetFunction"}, {"---@return nil"}))
add_exact("Effect.SetCost", signature("colon", "设置 cost 支付回调。", {"cost"}, {"---@param cost CostFunction"}, {"---@return nil"}))
add_exact("Effect.SetValue", signature("colon", "设置固定值或动态值回调。", {"value"}, {"---@param value integer|boolean|ValueFunction"}, {"---@return nil"}))
add_exact("Effect.SetOperation", signature("colon", "设置效果处理回调。", {"operation"}, {"---@param operation OperationFunction"}, {"---@return nil"}))
add_exact("Effect.SetOwnerPlayer", signature("colon", "设置效果拥有玩家。", {"player"}, {"---@param player Player"}, {"---@return nil"}))

add_getters("Effect", {"GetDescription", "GetCode", "GetType", "GetProperty", "GetLabel", "GetCategory", "GetRange", "GetActiveType", "GetActivateLocation", "GetActivateSequence"}, "取得效果字段 {name}。", {"---@return integer value"})
add_getters("Effect", {"GetOwner", "GetHandler"}, "取得效果关联卡片 {name}。", {"---@return Card|nil card"})
add_getters("Effect", {"GetCondition", "GetTarget", "GetCost", "GetValue", "GetOperation"}, "取得 Lua 回调 {name}。", {"---@return function|integer|nil callback"})
add_predicates("Effect", {"IsActiveType", "IsHasProperty", "IsHasCategory", "IsHasType", "IsHasRange", "IsActivatable", "IsActivated", "IsCostChecked", "CheckCountLimit"}, "检查效果状态 {name}。")

add_exact("Group.CreateGroup", signature("dot", "创建空卡片集合。", {}, {}, {"---@return Group group"}))
add_exact("Group.FromCards", signature("dot", "从若干卡片创建集合。", {"..."}, {"---@param ... Card"}, {"---@return Group group"}))
add_exact("Group.KeepAlive", signature("colon", "保持集合跨调用存活。", {}, {}, {"---@return nil"}))
add_exact("Group.DeleteGroup", signature("colon", "释放由 KeepAlive 保持的集合。", {}, {}, {"---@return nil"}))
add_exact("Group.Clone", signature("colon", "克隆当前集合。", {}, {}, {"---@return Group group"}))
add_exact("Group.Clear", signature("colon", "清空集合。", {}, {}, {"---@return nil"}))
add_exact("Group.AddCard", signature("colon", "向集合加入卡片。", {"card"}, {"---@param card Card"}, {"---@return nil"}))
add_exact("Group.RemoveCard", signature("colon", "从集合移除卡片。", {"card"}, {"---@param card Card"}, {"---@return nil"}))
add_exact("Group.GetFirst", signature("colon", "取得集合第一个卡片，并重置迭代游标。", {}, {}, {"---@return Card|nil card"}))
add_exact("Group.GetNext", signature("colon", "取得集合迭代游标的下一个卡片。", {}, {}, {"---@return Card|nil card"}))
add_exact("Group.GetCount", signature("colon", "取得集合内卡片数量。", {}, {}, {"---@return integer count"}))
add_exact("Group.Filter", signature("colon", "按过滤函数返回新集合。", {"filter", "excluded", "..."}, {"---@param filter FilterFunction", "---@param excluded? Card|Group|nil", "---@param ... any"}, {"---@return Group group"}))
add_exact("Group.FilterCount", signature("colon", "统计满足过滤函数的卡片数量。", {"filter", "excluded", "..."}, {"---@param filter FilterFunction", "---@param excluded? Card|Group|nil", "---@param ... any"}, {"---@return integer count"}))
add_exact("Group.FilterSelect", signature("colon", "按过滤函数让玩家选择卡片。", {"player", "filter", "min", "max", "excluded", "..."}, {"---@param player Player", "---@param filter FilterFunction", "---@param min integer", "---@param max integer", "---@param excluded? Card|Group|nil", "---@param ... any"}, {"---@return Group selected"}))
add_exact("Group.Select", signature("colon", "让玩家从集合中选择卡片。", {"player", "min", "max", "cancelable"}, {"---@param player Player", "---@param min integer", "---@param max integer", "---@param cancelable? boolean|integer"}, {"---@return Group selected"}))
add_exact("Group.RandomSelect", signature("colon", "随机选择集合内卡片。", {"player", "count"}, {"---@param player Player", "---@param count integer"}, {"---@return Group selected"}))
add_exact("Group.IsExists", signature("colon", "检查集合内是否存在满足过滤条件的指定数量卡片。", {"filter", "count", "excluded", "..."}, {"---@param filter FilterFunction", "---@param count integer", "---@param excluded? Card|Group|nil", "---@param ... any"}, {"---@return boolean result"}))
add_exact("Group.IsContains", signature("colon", "检查集合是否包含卡片。", {"card"}, {"---@param card Card"}, {"---@return boolean result"}))
add_exact("Group.Merge", signature("colon", "合并另一个集合。", {"other"}, {"---@param other Group"}, {"---@return nil"}))
add_exact("Group.Sub", signature("colon", "从当前集合减去另一个集合。", {"other"}, {"---@param other Group"}, {"---@return nil"}))
add_exact("Group.Equal", signature("colon", "检查两个集合是否相同。", {"other"}, {"---@param other Group"}, {"---@return boolean result"}))
add_exact("Group.GetSum", signature("colon", "按求值函数计算总和。", {"value", "..."}, {"---@param value SumValueFunction|integer", "---@param ... any"}, {"---@return integer sum"}))
add_exact("Group.GetClassCount", signature("colon", "统计分类数量。", {"classFunction", "..."}, {"---@param classFunction ClassValueFunction", "---@param ... any"}, {"---@return integer count"}))

add_exact("Card.GetCode", signature("colon", "取得当前代码；部分状态下可能额外返回另一个代码。", {}, {}, {"---@return CardCode code", "---@return CardCode? anotherCode"}))
add_exact("Card.GetOriginalCode", signature("colon", "取得原始卡号。", {}, {}, {"---@return CardCode code"}))
add_exact("Card.GetOriginalCodeRule", signature("colon", "取得规则视角原始卡号，可能返回两个代码。", {}, {}, {"---@return CardCode code", "---@return CardCode? secondCode"}))
add_getters("Card", {"GetType", "GetOriginalType", "GetLevel", "GetRank", "GetLink", "GetLeftScale", "GetRightScale", "GetAttribute", "GetOriginalAttribute", "GetRace", "GetOriginalRace", "GetAttack", "GetBaseAttack", "GetDefense", "GetBaseDefense", "GetOwner", "GetControler", "GetLocation", "GetSequence", "GetPosition", "GetReason", "GetSummonType", "GetStatus"}, "取得卡片字段 {name}。", {"---@param ... any", "---@return integer value"})
add_predicates("Card", {"IsCode", "IsSetCard", "IsType", "IsFusionCode", "IsLinkCode", "IsRace", "IsAttribute", "IsLocation", "IsPosition", "IsControler", "IsFaceup", "IsFacedown", "IsAttackPos", "IsDefensePos", "IsAbleToHand", "IsAbleToDeck", "IsAbleToGrave", "IsAbleToRemove", "IsRelateToEffect", "IsCanBeEffectTarget", "IsCanBeSpecialSummoned", "IsCanBeSummoned", "IsCanBeFlipSummoned", "IsCanBeSynchroMaterial", "IsCanBeXyzMaterial", "IsCanBeLinkMaterial"}, "检查卡片条件 {name}。")
add_exact("Card.RegisterEffect", signature("colon", "把效果注册到当前卡片。", {"effect", "forced", "player"}, {"---@param effect Effect", "---@param forced? boolean|integer", "---@param player? Player"}, {"---@return integer|Effect|nil result"}))
add_exact("Card.GetEquipGroup", signature("colon", "取得装备到此卡的集合。", {}, {}, {"---@return Group group"}))
add_exact("Card.GetOverlayGroup", signature("colon", "取得叠放素材集合。", {}, {}, {"---@return Group group"}))
add_exact("Card.GetColumnGroup", signature("colon", "取得同列相关卡片集合。", {"left", "right"}, {"---@param left? integer", "---@param right? integer"}, {"---@return Group group"}))
add_exact("Card.GetLinkedGroup", signature("colon", "取得此连接怪兽连接端指向的卡片集合。", {}, {}, {"---@return Group group"}))
add_exact("Card.GetMutualLinkedGroup", signature("colon", "取得互相连接的卡片集合。", {}, {}, {"---@return Group group"}))

add_exact("Duel.GetLP", signature("dot", "取得玩家 LP。", {"player"}, {"---@param player Player"}, {"---@return integer lp"}))
add_exact("Duel.SetLP", signature("dot", "设置玩家 LP。", {"player", "lp"}, {"---@param player Player", "---@param lp integer"}, {"---@return nil"}))
add_exact("Duel.IsTurnPlayer", signature("dot", "检查当前回合玩家。", {"player"}, {"---@param player Player"}, {"---@return boolean result"}))
add_exact("Duel.GetTurnPlayer", signature("dot", "取得当前回合玩家。", {}, {}, {"---@return Player player"}))
add_exact("Duel.GetTurnCount", signature("dot", "取得当前回合数。", {}, {}, {"---@return integer count"}))
add_exact("Duel.RegisterEffect", signature("dot", "注册全局/玩家效果。", {"effect", "player"}, {"---@param effect Effect", "---@param player Player"}, {"---@return integer|nil result"}))
add_exact("Duel.RegisterFlagEffect", signature("dot", "为玩家注册 flag effect。", {"player", "code", "reset", "property", "count", "label"}, {"---@param player Player", "---@param code integer", "---@param reset Reset", "---@param property integer", "---@param count integer", "---@param label? integer"}, {"---@return integer result"}))
add_exact("Duel.GetFlagEffect", signature("dot", "取得 flag effect 数量。", {"player", "code"}, {"---@param player Player", "---@param code integer"}, {"---@return integer count"}))
add_exact("Duel.Destroy", signature("dot", "破坏卡片或集合。", {"targets", "reason"}, {"---@param targets Card|Group", "---@param reason Reason"}, {"---@return integer count"}))
add_exact("Duel.Remove", signature("dot", "除外卡片或集合。", {"targets", "position", "reason"}, {"---@param targets Card|Group", "---@param position Position", "---@param reason Reason"}, {"---@return integer count"}))
add_exact("Duel.SendtoGrave", signature("dot", "把卡片或集合送去墓地。", {"targets", "reason"}, {"---@param targets Card|Group", "---@param reason Reason"}, {"---@return integer count"}))
add_exact("Duel.SendtoHand", signature("dot", "把卡片或集合加入手牌。", {"targets", "player", "reason"}, {"---@param targets Card|Group", "---@param player? Player", "---@param reason Reason"}, {"---@return integer count"}))
add_exact("Duel.SendtoDeck", signature("dot", "把卡片或集合送回卡组。", {"targets", "player", "sequence", "reason"}, {"---@param targets Card|Group", "---@param player? Player", "---@param sequence integer", "---@param reason Reason"}, {"---@return integer count"}))
add_exact("Duel.GetOperatedGroup", signature("dot", "取得最近一次操作影响的卡片集合。", {}, {}, {"---@return Group group"}))
add_exact("Duel.CreateToken", signature("dot", "创建 token 卡片。", {"player", "code"}, {"---@param player Player", "---@param code CardCode"}, {"---@return Card token"}))
add_exact("Duel.SpecialSummon", signature("dot", "特殊召唤卡片或集合。", {"targets", "summonType", "player", "targetPlayer", "nocheck", "nolimit", "position", "zone"}, {"---@param targets Card|Group", "---@param summonType SummonType", "---@param player Player", "---@param targetPlayer Player", "---@param nocheck boolean|integer", "---@param nolimit boolean|integer", "---@param position Position", "---@param zone? integer"}, {"---@return integer count"}))
add_exact("Duel.GetFieldGroup", signature("dot", "取得指定玩家双方位置的卡片集合。", {"player", "selfLocation", "opponentLocation"}, {"---@param player Player", "---@param selfLocation Location", "---@param opponentLocation Location"}, {"---@return Group group"}))
add_exact("Duel.GetFieldGroupCount", signature("dot", "取得指定玩家双方位置的卡片数量。", {"player", "selfLocation", "opponentLocation"}, {"---@param player Player", "---@param selfLocation Location", "---@param opponentLocation Location"}, {"---@return integer count"}))
add_exact("Duel.GetMatchingGroup", signature("dot", "取得满足过滤条件的卡片集合。", {"filter", "player", "selfLocation", "opponentLocation", "excluded", "..."}, {"---@param filter FilterFunction", "---@param player Player", "---@param selfLocation Location", "---@param opponentLocation Location", "---@param excluded? Card|Group|nil", "---@param ... any"}, {"---@return Group group"}))
add_exact("Duel.GetMatchingGroupCount", signature("dot", "统计满足过滤条件的卡片数量。", {"filter", "player", "selfLocation", "opponentLocation", "excluded", "..."}, {"---@param filter FilterFunction", "---@param player Player", "---@param selfLocation Location", "---@param opponentLocation Location", "---@param excluded? Card|Group|nil", "---@param ... any"}, {"---@return integer count"}))
add_exact("Duel.IsExistingMatchingCard", signature("dot", "检查是否存在满足过滤条件的卡片。", {"filter", "player", "selfLocation", "opponentLocation", "count", "excluded", "..."}, {"---@param filter FilterFunction", "---@param player Player", "---@param selfLocation Location", "---@param opponentLocation Location", "---@param count integer", "---@param excluded? Card|Group|nil", "---@param ... any"}, {"---@return boolean result"}))
add_exact("Duel.SelectMatchingCard", signature("dot", "让玩家选择满足过滤条件的卡片。", {"player", "filter", "selectPlayer", "selfLocation", "opponentLocation", "min", "max", "excluded", "..."}, {"---@param player Player", "---@param filter FilterFunction", "---@param selectPlayer Player", "---@param selfLocation Location", "---@param opponentLocation Location", "---@param min integer", "---@param max integer", "---@param excluded? Card|Group|nil", "---@param ... any"}, {"---@return Group selected"}))
add_exact("Duel.SelectTarget", signature("dot", "让玩家选择效果目标。", {"player", "filter", "selectPlayer", "selfLocation", "opponentLocation", "min", "max", "excluded", "..."}, {"---@param player Player", "---@param filter FilterFunction", "---@param selectPlayer Player", "---@param selfLocation Location", "---@param opponentLocation Location", "---@param min integer", "---@param max integer", "---@param excluded? Card|Group|nil", "---@param ... any"}, {"---@return Group selected"}))
add_exact("Duel.SetOperationInfo", signature("dot", "设置当前连锁操作信息。", {"chainIndex", "category", "targets", "count", "player", "value"}, {"---@param chainIndex integer", "---@param category EffectCategory", "---@param targets Card|Group|nil", "---@param count integer", "---@param player Player", "---@param value integer"}, {"---@return nil"}))
add_exact("Duel.Hint", signature("dot", "向玩家发送提示。", {"hintType", "player", "value"}, {"---@param hintType Hint", "---@param player Player", "---@param value integer|string"}, {"---@return nil"}))
add_exact("Duel.SelectYesNo", signature("dot", "向玩家询问是否。", {"player", "description"}, {"---@param player Player", "---@param description integer|string"}, {"---@return boolean result"}))
add_exact("Duel.SelectOption", signature("dot", "向玩家展示选项并返回选择索引。", {"player", "..."}, {"---@param player Player", "---@param ... integer|string"}, {"---@return integer optionIndex"}))
add_exact("Duel.GetCurrentPhase", signature("dot", "取得当前阶段。", {}, {}, {"---@return Phase phase"}))
add_exact("Duel.GetCurrentChain", signature("dot", "取得当前连锁序号。", {}, {}, {"---@return integer chainIndex"}))
add_exact("Duel.GetChainInfo", signature("dot", "取得连锁信息；返回值随 ChainInfo 参数变化。", {"chainIndex", "..."}, {"---@param chainIndex integer", "---@param ... ChainInfo"}, {"---@return any ..."}))
add_exact("Duel.Readjust", signature("dot", "重新调整场面合法性。", {}, {}, {"---@return nil"}))
add_exact("Duel.BreakEffect", signature("dot", "切断效果处理时点。", {}, {}, {"---@return nil"}))
add_exact("Duel.Draw", signature("dot", "让玩家抽卡。", {"player", "count", "reason"}, {"---@param player Player", "---@param count integer", "---@param reason Reason"}, {"---@return integer count"}))
add_exact("Duel.Damage", signature("dot", "给予玩家伤害。", {"player", "amount", "reason", "isStep"}, {"---@param player Player", "---@param amount integer", "---@param reason Reason", "---@param isStep? boolean|integer"}, {"---@return integer amount"}))
add_exact("Duel.Recover", signature("dot", "回复玩家 LP。", {"player", "amount", "reason", "isStep"}, {"---@param player Player", "---@param amount integer", "---@param reason Reason", "---@param isStep? boolean|integer"}, {"---@return integer amount"}))
add_exact("Duel.Win", signature("dot", "立即判定玩家胜利。", {"player", "reason"}, {"---@param player Player", "---@param reason integer"}, {"---@return nil"}))

add_exact("Debug.Message", signature("dot", "输出调试消息。", {"message"}, {"---@param message string|integer"}, {"---@return nil"}))
add_exact("Debug.SetPlayerInfo", signature("dot", "设置调试 duel 的玩家信息。", {"player", "lp", "startCount", "drawCount"}, {"---@param player Player", "---@param lp integer", "---@param startCount integer", "---@param drawCount integer"}, {"---@return nil"}))
add_exact("Debug.SetAIName", signature("dot", "设置 AI 名称。", {"name"}, {"---@param name string"}, {"---@return nil"}))

local function table_by_name(name)
    for _, item in ipairs(tables) do
        if item.name == name then
            return item
        end
    end
end

local function get_entries(core_dir, item)
    local file = path_join(core_dir, item.source)
    local text = read_file(file)
    local body = text:match("static%s+const%s+struct%s+luaL_Reg%s+" .. item.table .. "%[%]%s*=%s*%{(.-)\n%};")
    if not body then
        error(("cannot find luaL_Reg table %s in %s"):format(item.table, file))
    end

    local entries = {}
    for name, cpp in body:gmatch('%{%s*"([^"]+)"%s*,%s*scriptlib::([A-Za-z0-9_]+)%s*%}') do
        entries[#entries + 1] = {
            table = item.name,
            name = name,
            cpp = "scriptlib::" .. cpp,
            source = item.source,
            default_style = item.style
        }
    end
    return entries
end

local function default_return(entry)
    local name = entry.name
    local table_name = entry.table
    if name:match("^(__add|__bor|__sub|__band|__bxor)$") then
        return {"---@return Group group"}
    end
    if name == "__len" then
        return {"---@return integer count"}
    end
    if name:match("^(Is|Can|Check|Has|Equal)") then
        return {"---@return boolean result"}
    end
    if name:match("Count$") then
        return {"---@return integer count"}
    end

    if table_name == "Card" then
        if name:match("Group$") then
            return {"---@return Group group"}
        end
        if name:match("(Card|Owner|Handler|ReasonCard|EquipTarget|OverlayTarget|BattleTarget)$") then
            return {"---@return Card|nil card"}
        end
        if name:match("^Get") then
            return {"---@return integer|Card|Effect|Group|nil value"}
        end
        if name:match("^(Register|Create)") then
            return {"---@return integer|Effect|nil result"}
        end
        return {"---@return any result"}
    elseif table_name == "Effect" then
        if name:match("^Set") or name:match("^Reset") or name:match("^Use") then
            return {"---@return nil"}
        end
        if name:match("(Owner|Handler)$") then
            return {"---@return Card|nil card"}
        end
        if name:match("Object$") then
            return {"---@return Card|Effect|Group|nil object"}
        end
        if name:match("^Get") then
            return {"---@return integer|function|Card|Effect|Group|nil value"}
        end
        return {"---@return any result"}
    elseif table_name == "Group" then
        if name:match("^(Clear|AddCard|RemoveCard|Merge|Sub|KeepAlive|DeleteGroup|Remove)$") then
            return {"---@return nil"}
        end
        if name:match("(Select|Filter|Group|Clone)$") then
            return {"---@return Group group"}
        end
        if name:match("(First|Next|SearchCard)$") then
            return {"---@return Card|nil card"}
        end
        if name:match("Sum$") or name:match("ClassCount$") then
            return {"---@return integer value"}
        end
        return {"---@return any result"}
    elseif table_name == "Duel" then
        if name:match("^Get.*Group") then
            return {"---@return Group group"}
        end
        if name:match("^Get.*Card") or name:match("^CreateToken") then
            return {"---@return Card|Group|nil card"}
        end
        if name:match("^Select.*Card") or name:match("^Select.*Group") then
            return {"---@return Group|integer|boolean result"}
        end
        return {"---@return any result"}
    elseif table_name == "Debug" then
        return {"---@return nil"}
    end
    return {"---@return any result"}
end

local function function_name(entry, style)
    if style == "dot" then
        return entry.table .. "." .. entry.name
    end
    return entry.table .. ":" .. entry.name
end

local function add_lines(lines, values)
    for _, value in ipairs(values or {}) do
        lines[#lines + 1] = value
    end
end

local function build_outputs(core_dir)
    local all_entries = {}
    for _, item in ipairs(tables) do
        all_entries[item.name] = get_entries(core_dir, item)
    end

    local lua = {}
    add_lines(lua, {
        "---@meta",
        "---@diagnostic disable: lowercase-global, missing-return, unused-local, duplicate-set-field",
        "",
        "--- ygopro-core Lua API 类型声明。",
        "---",
        "--- 该文件由 `xmake gen-lua-api -P YGOProSharp.Native` 从 YGOProSharp.Native/ygopro-core/lib*.cpp 的",
        "--- luaL_Reg 注册表生成，覆盖 native core 实际注册到 Lua 的 Card、Effect、Group、",
        "--- Duel、Debug API。它只用于 LuaLS/EmmyLua 补全和静态检查，不应被对局运行时加载。",
        "--- 高频 API 采用明确签名；无法从 C++ 注册表安全推断的长尾 API 使用保守签名，并保留来源函数。",
        "",
        "---@alias Player integer 玩家编号，通常为 0 或 1。",
        "---@alias CardCode integer 卡号。",
        "---@alias Location integer 位置 bitmask，例如 LOCATION_MZONE / LOCATION_GRAVE。",
        "---@alias Position integer 表示形式 bitmask，例如 POS_FACEUP_ATTACK。",
        "---@alias Reason integer 原因 bitmask，例如 REASON_EFFECT / REASON_COST。",
        "---@alias Reset integer reset bitmask，例如 RESET_EVENT + RESETS_STANDARD。",
        "---@alias EffectCode integer 效果代码，例如 EFFECT_UPDATE_ATTACK。",
        "---@alias EffectType integer 效果类型 bitmask，例如 EFFECT_TYPE_SINGLE。",
        "---@alias EffectProperty integer 效果属性 bitmask，例如 EFFECT_FLAG_CARD_TARGET。",
        "---@alias EffectCategory integer 效果分类 bitmask，例如 CATEGORY_DESTROY。",
        "---@alias Race integer 种族 bitmask。",
        "---@alias Attribute integer 属性 bitmask。",
        "---@alias Phase integer 阶段常量，例如 PHASE_MAIN1。",
        "---@alias SummonType integer 召唤类型常量。",
        "---@alias ChainInfo integer 连锁信息查询常量，例如 CHAININFO_TARGET_CARDS。",
        "---@alias QueryFlag integer query_card / query_field_card 查询 bitmask。",
        "---@alias Hint integer 提示类型，例如 HINT_SELECTMSG。",
        "---@alias Zone integer 区域 bitmask。",
        "---@alias CountLimitCode integer 次数限制 code，可以是卡号或自定义 id。",
        "",
        "---@alias FilterFunction fun(c: Card, ...: any): boolean|integer",
        "---@alias MaterialFilter fun(c: Card, ...: any): boolean|integer",
        "---@alias ClassValueFunction fun(c: Card, ...: any): integer|string|boolean",
        "---@alias SumValueFunction fun(c: Card, ...: any): integer",
        "---@alias ValueFunction fun(e: Effect, c: Card|nil, ...: any): integer|boolean|Card|Group|nil",
        "---@alias ChainLimitFunction fun(e: Effect, ep: Player, tp: Player): boolean|integer",
        "---@alias ConditionFunction fun(e: Effect, tp: Player, eg: Group, ep: Player, ev: integer, re: Effect, r: Reason, rp: Player): boolean|integer",
        "---@alias CostFunction fun(e: Effect, tp: Player, eg: Group, ep: Player, ev: integer, re: Effect, r: Reason, rp: Player, chk: integer): boolean|integer|nil",
        "---@alias TargetFunction fun(e: Effect, tp: Player, eg: Group, ep: Player, ev: integer, re: Effect, r: Reason, rp: Player, chk: integer, chkc?: Card): boolean|integer|nil",
        "---@alias OperationFunction fun(e: Effect, tp: Player, eg: Group, ep: Player, ev: integer, re: Effect, r: Reason, rp: Player): nil",
        "---@alias InitialEffectFunction fun(c: Card): nil",
        "",
        "---@class Card",
        "Card = {}",
        "",
        "---@class Effect",
        "Effect = {}",
        "",
        "---@class Group",
        "Group = {}",
        "",
        "---@class Duel",
        "Duel = {}",
        "",
        "---@class Debug",
        "Debug = {}"
    })

    for _, item in ipairs(tables) do
        local entries = all_entries[item.name]
        lua[#lua + 1] = ""
        lua[#lua + 1] = "-------------------------------------------------------------------------------"
        lua[#lua + 1] = ("-- %s API (%d functions)"):format(item.name, #entries)
        lua[#lua + 1] = "-------------------------------------------------------------------------------"
        for _, entry in ipairs(entries) do
            local key = entry.table .. "." .. entry.name
            local sig = exact[key]
            local style = sig and sig.style or entry.default_style
            local args = sig and sig.args or {"..."}
            lua[#lua + 1] = ""
            if sig then
                lua[#lua + 1] = "--- " .. sig.description
                add_lines(lua, sig.notes)
                lua[#lua + 1] = ("--- C++: `%s`"):format(entry.cpp)
                add_lines(lua, sig.params)
                add_lines(lua, sig.returns)
            else
                lua[#lua + 1] = "--- 保守签名：该函数已由 native 注册到 Lua，但参数语义需要结合脚本约定确认。"
                lua[#lua + 1] = ("--- C++: `%s`"):format(entry.cpp)
                lua[#lua + 1] = "---@param ... any"
                add_lines(lua, default_return(entry))
            end
            lua[#lua + 1] = ("function %s(%s) end"):format(function_name(entry, style), table.concat(args, ", "))
        end
    end

    local coverage = {
        "# ygopro-core Lua API 覆盖报告",
        "",
        "本文件由 `xmake gen-lua-api -P YGOProSharp.Native` / `lua-api/generator.lua` 根据 `YGOProSharp.Native/ygopro-core/lib*.cpp` 的 `luaL_Reg` 表生成。",
        "它用于确认 `ygopro-core.lua` 是否覆盖 native core 实际暴露给卡片脚本的 Lua API。",
        "",
        "## 汇总",
        "",
        "| 全局表 | 来源文件 | 函数数 | 精确签名 | 保守签名 |",
        "|---|---|---:|---:|---:|"
    }
    for _, item in ipairs(tables) do
        local entries = all_entries[item.name]
        local exact_count = 0
        for _, entry in ipairs(entries) do
            if exact[entry.table .. "." .. entry.name] then
                exact_count = exact_count + 1
            end
        end
        coverage[#coverage + 1] = ("| `%s` | `%s` | %d | %d | %d |"):format(item.name, item.source, #entries, exact_count, #entries - exact_count)
    end

    coverage[#coverage + 1] = ""
    coverage[#coverage + 1] = "## 详细清单"
    for _, item in ipairs(tables) do
        coverage[#coverage + 1] = ""
        coverage[#coverage + 1] = "### " .. item.name
        coverage[#coverage + 1] = ""
        coverage[#coverage + 1] = "| API | C++ 入口 | 签名状态 |"
        coverage[#coverage + 1] = "|---|---|---|"
        for _, entry in ipairs(all_entries[item.name]) do
            local key = entry.table .. "." .. entry.name
            local status = exact[key] and "精确" or "保守"
            coverage[#coverage + 1] = ("| `%s` | `%s` | %s |"):format(key, entry.cpp, status)
        end
    end

    return {
        lua = table.concat(lua, "\n") .. "\n",
        coverage = table.concat(coverage, "\n") .. "\n",
        entries = all_entries
    }
end

function generator.run(options)
    options = options or {}
    local project_dir = options.project_dir or os.projectdir and os.projectdir() or "."
    local core_dir = options.core_dir or path_join(project_dir, "ygopro-core")
    local out_dir = options.out_dir or path_join(project_dir, "lua-api")
    local api_file = path_join(out_dir, "ygopro-core.lua")
    local coverage_file = path_join(out_dir, "API-COVERAGE.md")
    local outputs = build_outputs(core_dir)
    local changed = {}

    if options.check then
        if not same_file(api_file, outputs.lua) then
            changed[#changed + 1] = api_file
        end
        if not same_file(coverage_file, outputs.coverage) then
            changed[#changed + 1] = coverage_file
        end
        if #changed > 0 then
            error("generated lua-api files are out of date: " .. table.concat(changed, ", "))
        end
    else
        write_file(api_file, outputs.lua)
        write_file(coverage_file, outputs.coverage)
        print("Generated " .. api_file)
        print("Generated " .. coverage_file)
    end

    if options.verbose or not options.check then
        for _, item in ipairs(tables) do
            print(("%s: %d"):format(item.name, #outputs.entries[item.name]))
        end
    end

    return outputs
end

function run(options)
    return generator.run(options)
end

return generator
