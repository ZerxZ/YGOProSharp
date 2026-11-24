local generator = {}

local tables = {
    { name = "Card", table = "cardlib", source = "libcard.cpp", style = "colon", module = "card" },
    { name = "Effect", table = "effectlib", source = "libeffect.cpp", style = "colon", module = "effect" },
    { name = "Group", table = "grouplib", source = "libgroup.cpp", style = "colon", module = "group" },
    { name = "Duel", table = "duellib", source = "libduel.cpp", style = "dot", module = "duel" },
    { name = "Debug", table = "debuglib", source = "libdebug.cpp", style = "dot", module = "debug" }
}

local function fail(msg)
    if raise then
        raise(msg)
    elseif error then
        error(msg)
    else
        assert(false, msg)
    end
end

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
        fail(("cannot read %s: %s"):format(file, err or "unknown error"))
    end
    local data = handle:read("*a")
    handle:close()
    return data
end

local function write_file(file, data)
    local dir = file:match("^(.*)[/\\][^/\\]+$")
    if dir and dir ~= "" and os and os.mkdir then
        os.mkdir(dir)
    end
    local handle, err = io.open(file, "wb")
    if not handle then
        fail(("cannot write %s: %s"):format(file, err or "unknown error"))
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

local function signature(style, description, args, params, returns, notes, overloads)
    return {
        style = style,
        description = description,
        args = args or {},
        params = params or {},
        returns = returns or {},
        notes = notes or {},
        overloads = overloads or {}
    }
end

local function load_signature_module(lua_api_dir, module_name)
    if import then
        return import("signatures." .. module_name, {rootdir = lua_api_dir, anonymous = true})
    end
    local filepath = path_join(lua_api_dir, "signatures", module_name .. ".lua")
    local loader, err = loadfile(filepath)
    if not loader then
        fail(("cannot load signature module %s: %s"):format(filepath, err or "unknown error"))
    end
    return loader()
end

local function load_exact_signatures(lua_api_dir)
    local exact = {}
    local function add_exact(key, sig)
        if exact[key] then
            fail("duplicate signature definition for " .. key)
        end
        exact[key] = sig
    end

    for _, item in ipairs(tables) do
        local mod = load_signature_module(lua_api_dir, item.module)
        if not mod or not mod.register then
            fail("signature module missing register(): " .. item.module)
        end
        mod.register(add_exact, signature)
    end

    return exact
end

local function get_entries(core_dir, item)
    local file = path_join(core_dir, item.source)
    local text = read_file(file)
    local body = text:match("static%s+const%s+struct%s+luaL_Reg%s+" .. item.table .. "%[%]%s*=%s*%{(.-)\n%};")
    if not body then
        fail(("cannot find luaL_Reg table %s in %s"):format(item.table, file))
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

local function starts_with_any(text, prefixes)
    for _, prefix in ipairs(prefixes) do
        if text:sub(1, #prefix) == prefix then
            return true
        end
    end
    return false
end

local function ends_with_any(text, suffixes)
    for _, suffix in ipairs(suffixes) do
        if #text >= #suffix and text:sub(-#suffix) == suffix then
            return true
        end
    end
    return false
end

local function default_return(entry)
    local name = entry.name
    local table_name = entry.table
    if name == "__add" or name == "__bor" or name == "__sub" or name == "__band" or name == "__bxor" then
        return {"---@return Group group"}
    end
    if name == "__len" then
        return {"---@return integer count"}
    end
    if starts_with_any(name, {"Is", "Can", "Check", "Has", "Equal"}) then
        return {"---@return boolean result"}
    end
    if ends_with_any(name, {"Count"}) then
        return {"---@return integer count"}
    end

    if table_name == "Card" then
        if ends_with_any(name, {"Group"}) then
            return {"---@return Group group"}
        end
        if ends_with_any(name, {"Card", "Owner", "Handler", "ReasonCard", "EquipTarget", "OverlayTarget", "BattleTarget"}) then
            return {"---@return Card|nil card"}
        end
        if starts_with_any(name, {"Get"}) then
            return {"---@return integer|Card|Effect|Group|nil value"}
        end
        if starts_with_any(name, {"Register", "Create"}) then
            return {"---@return integer|Effect|nil result"}
        end
        return {"---@return any result"}
    elseif table_name == "Effect" then
        if starts_with_any(name, {"Set", "Reset", "Use"}) then
            return {"---@return nil"}
        end
        if ends_with_any(name, {"Owner", "Handler"}) then
            return {"---@return Card|nil card"}
        end
        if ends_with_any(name, {"Object"}) then
            return {"---@return Card|Effect|Group|nil object"}
        end
        if starts_with_any(name, {"Get"}) then
            return {"---@return integer|function|Card|Effect|Group|nil value"}
        end
        return {"---@return any result"}
    elseif table_name == "Group" then
        if name == "Clear" or name == "AddCard" or name == "RemoveCard" or name == "Merge" or name == "Sub" or name == "KeepAlive" or name == "DeleteGroup" or name == "Remove" then
            return {"---@return nil"}
        end
        if ends_with_any(name, {"Select", "Filter", "Group", "Clone"}) then
            return {"---@return Group group"}
        end
        if ends_with_any(name, {"First", "Next", "SearchCard"}) then
            return {"---@return Card|nil card"}
        end
        if ends_with_any(name, {"Sum", "ClassCount"}) then
            return {"---@return integer value"}
        end
        return {"---@return any result"}
    elseif table_name == "Duel" then
        if name:match("^Get.*Group") then
            return {"---@return Group group"}
        end
        if name:match("^Get.*Card") or starts_with_any(name, {"CreateToken"}) then
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

local function build_outputs(core_dir, lua_api_dir)
    local exact = load_exact_signatures(lua_api_dir)

    local all_entries = {}
    local registered_keys = {}
    for _, item in ipairs(tables) do
        local entries = get_entries(core_dir, item)
        all_entries[item.name] = entries
        for _, entry in ipairs(entries) do
            registered_keys[entry.table .. "." .. entry.name] = true
        end
    end

    -- 校验 signatures/*.lua 中没有残留失效或拼写错误的 API 键
    local stale_keys = {}
    for key in pairs(exact) do
        if not registered_keys[key] then
            stale_keys[#stale_keys + 1] = key
        end
    end
    if #stale_keys > 0 then
        table.sort(stale_keys)
        fail("stale or unknown signature keys not present in ygopro-core luaL_Reg: " .. table.concat(stale_keys, ", "))
    end

    local lua = {}
    add_lines(lua, {
        "---@meta",
        "---@diagnostic disable: lowercase-global, missing-return, unused-local, duplicate-set-field",
        "",
        "--- ygopro-core Lua API 精确类型声明（LuaLS / EmmyLua）。",
        "---",
        "--- 该文件由 `xmake gen-lua-api -P YGOProSharp.Native` 依据",
        "--- `YGOProSharp.Native/ygopro-core/lib*.cpp` 的 `luaL_Reg` 注册表与",
        "--- `YGOProSharp.Native/lua-api/signatures/*.lua` 生成。",
        "--- 仅用于编辑器补全与静态类型检查，不应放入 `script_reader` 运行时目录。",
        "",
        "---@alias Player integer 玩家编号：`0` / `1`（部分查询支持 `PLAYER_NONE=2`、`PLAYER_ALL=3`）。",
        "---@alias CardCode integer 卡片密码（卡号）。",
        "---@alias SetCode integer 字段编码（16 位字段值）。",
        "---@alias CardType integer 卡片种类位掩码（`TYPE_*`）。",
        "---@alias Location integer 区域位掩码（`LOCATION_*`）。",
        "---@alias Position integer 表示形式位掩码（`POS_*`）。",
        "---@alias Reason integer 移动/操作原因位掩码（`REASON_*`）。",
        "---@alias Reset integer 效果重置条件位掩码（`RESET_*` / `RESETS_*`）。",
        "---@alias EffectCode integer 效果代码或事件代码（`EFFECT_*` / `EVENT_*`）。",
        "---@alias EffectType integer 效果类型位掩码（`EFFECT_TYPE_*`）。",
        "---@alias EffectProperty integer 效果主属性位掩码（`EFFECT_FLAG_*`）。",
        "---@alias EffectProperty2 integer 效果扩展属性位掩码（`EFFECT_FLAG2_*`）。",
        "---@alias EffectCategory integer 效果分类位掩码（`CATEGORY_*`）。",
        "---@alias Race integer 怪兽种族位掩码（`RACE_*`）。",
        "---@alias Attribute integer 怪兽属性位掩码（`ATTRIBUTE_*`）。",
        "---@alias Phase integer 阶段常量（`PHASE_*`）。",
        "---@alias SummonType integer 召唤类型位掩码（`SUMMON_TYPE_*`）。",
        "---@alias SummonInfo integer 特殊召唤快照查询标志（`SUMMON_INFO_*`）。",
        "---@alias ChainInfo integer 连锁信息查询标志（`CHAININFO_*`）。",
        "---@alias QueryFlag integer 场面查询位掩码（`QUERY_*`）。",
        "---@alias Hint integer 提示类型常量（`HINT_*`）。",
        "---@alias CardHint integer 单卡提示类型常量（`CHINT_*`）。",
        "---@alias Zone integer 场上格子位掩码。",
        "---@alias CountLimitCode integer 次数限制编码（卡号或自定义 ID，可按位或 `EFFECT_COUNT_CODE_*`）。",
        "---@alias CounterType integer 指示物类型编码。",
        "---@alias GlobalFlag integer 全局标志位掩码（`GLOBALFLAG_*`）。",
        "---@alias ActivityType integer 行动计数类型常量（`ACTIVITY_*`）。",
        "---@alias Status integer 卡片运行时状态位掩码（`STATUS_*`）。",
        "---@alias LinkMarker integer 连接标记方向位掩码（`LINK_MARKER_*`）。",
        "---@alias AssumeType integer 假设属性类型常量（`ASSUME_*`）。",
        "---@alias Opcode integer 宣言卡名过滤后缀表达式操作码（`OPCODE_*`）。",
        "---@alias Timing integer 提示时点位掩码（`TIMING_*`）。",
        "---@alias DuelOption integer 对局规则选项位掩码（`DUEL_*`）。",
        "",
        "---@alias FilterFunction fun(c: Card, ...: any): boolean|integer|nil",
        "---@alias EffectFilterFunction fun(e: Effect): boolean|integer|nil",
        "---@alias MaterialFilter fun(c: Card, ...: any): boolean|integer|nil",
        "---@alias ClassValueFunction fun(c: Card, ...: any): integer",
        "---@alias SumValueFunction fun(c: Card, ...: any): integer",
        "---@alias EffectTargetFilter fun(e: Effect, c: Card, ...: any): boolean|integer|nil",
        "---@alias ValueFunction fun(e: Effect, c?: Card|Effect|nil, ...: any): integer|boolean|nil",
        "---@alias ChainLimitFunction fun(e: Effect, rp: Player, tp: Player): boolean|integer|nil",
        "---@alias ConditionFunction fun(e: Effect, tp: Player, eg: Group, ep: Player, ev: integer, re: Effect, r: Reason, rp: Player): boolean|integer|nil",
        "---@alias CostFunction fun(e: Effect, tp: Player, eg: Group, ep: Player, ev: integer, re: Effect, r: Reason, rp: Player, chk: integer): boolean|integer|nil",
        "---@alias TargetFunction fun(e: Effect, tp: Player, eg: Group, ep: Player, ev: integer, re: Effect, r: Reason, rp: Player, chk: integer, chkc?: Card): boolean|integer|nil",
        "---@alias OperationFunction fun(e: Effect, tp: Player, eg: Group, ep: Player, ev: integer, re: Effect, r: Reason, rp: Player): nil",
        "---@alias InitialEffectFunction fun(c: Card): nil",
        "",
        "--- 卡片脚本全局初始化上下文（加载 `c<id>.lua` 时由 `interpreter::load_card_script` 临时注入）。",
        "---@type table|nil",
        "self_table = nil",
        "---@type CardCode|nil",
        "self_code = nil",
        "",
        "---@class Card",
        "---@operator add(Group): Group",
        "---@operator bor(Group): Group",
        "---@operator sub(Group): Group",
        "---@operator band(Group): Group",
        "---@operator bxor(Group): Group",
        "Card = {}",
        "",
        "---@class Effect",
        "Effect = {}",
        "",
        "---@class Group",
        "---@operator len: integer",
        "---@operator add(Card|Group): Group",
        "---@operator bor(Card|Group): Group",
        "---@operator sub(Card|Group): Group",
        "---@operator band(Card|Group): Group",
        "---@operator bxor(Card|Group): Group",
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
                add_lines(lua, sig.overloads)
            else
                lua[#lua + 1] = "--- 保守签名：该函数已由 native 注册到 Lua，但尚未补充精确签名。"
                lua[#lua + 1] = ("--- C++: `%s`"):format(entry.cpp)
                lua[#lua + 1] = "---@param ... any"
                add_lines(lua, default_return(entry))
            end
            lua[#lua + 1] = ("function %s(%s) end"):format(function_name(entry, style), table.concat(args, ", "))
        end
    end

    local total_funcs = 0
    local total_exact = 0
    local coverage = {
        "# ygopro-core Lua API 覆盖报告",
        "",
        "本文件由 `xmake gen-lua-api -P YGOProSharp.Native` / `lua-api/generator.lua` 根据 `YGOProSharp.Native/ygopro-core/lib*.cpp` 的 `luaL_Reg` 表生成。",
        "它用于确认 `ygopro-core.lua` 是否完整且无歧义地覆盖 native core 实际暴露给卡片脚本的 Lua API。",
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
        total_funcs = total_funcs + #entries
        total_exact = total_exact + exact_count
        coverage[#coverage + 1] = ("| `%s` | `%s` | %d | %d | %d |"):format(item.name, item.source, #entries, exact_count, #entries - exact_count)
    end
    coverage[#coverage + 1] = ("| **合计** | `lib*.cpp` | **%d** | **%d** | **%d** |"):format(total_funcs, total_exact, total_funcs - total_exact)

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
        entries = all_entries,
        total_funcs = total_funcs,
        total_exact = total_exact
    }
end

function generator.run(options)
    options = options or {}
    local project_dir = options.project_dir or (os and os.projectdir and os.projectdir()) or "."
    local core_dir = options.core_dir or path_join(project_dir, "ygopro-core")
    local out_dir = options.out_dir or path_join(project_dir, "lua-api")
    local api_file = path_join(out_dir, "ygopro-core.lua")
    local coverage_file = path_join(out_dir, "API-COVERAGE.md")
    local outputs = build_outputs(core_dir, out_dir)
    local changed = {}

    if options.check then
        if not same_file(api_file, outputs.lua) then
            changed[#changed + 1] = api_file
        end
        if not same_file(coverage_file, outputs.coverage) then
            changed[#changed + 1] = coverage_file
        end
        if #changed > 0 then
            fail("generated lua-api files are out of date: " .. table.concat(changed, ", "))
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
        print(("Total: %d/%d exact"):format(outputs.total_exact, outputs.total_funcs))
    end

    return outputs
end

function run(options)
    return generator.run(options)
end

return generator
