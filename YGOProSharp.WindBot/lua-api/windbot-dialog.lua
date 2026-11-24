---@meta
---@diagnostic disable: lowercase-global, missing-return, unused-local, duplicate-set-field

--- YGOProSharp.WindBot Lua 对话系统（Dialogs/Lua/*.lua）开发者类型声明（LuaLS / EmmyLua）。
---
--- 面向 xmake 声明式 DSL（支持 `dialog(...)` 与 `locale(...)` 本地化分区）、模块返回式（`return { ... }`）与全局函数式，
--- 兼容 XLua / NLua / MoonSharp / Lua-CSharp。
--- 仅用于编辑器补全与静态类型检查，不参与运行时加载。

---@alias WindBotDialogEvent
---| "welcome"
---| "deckerror"
---| "deck_error"
---| "duelstart"
---| "duel_start"
---| "newturn"
---| "new_turn"
---| "endturn"
---| "end_turn"
---| "directattack"
---| "direct_attack"
---| "attack"
---| "ondirectattack"
---| "on_direct_attack"
---| "activate"
---| "summon"
---| "setmonster"
---| "set_monster"
---| "chaining"
---| "surrender"
---| "custom"

--- 动态对话钩子返回值约定：
--- - 非空 `string` / `string[]`：直接发送该台词（数组时随机抽取一条）；
--- - `""` 或 `false`：静默跳过本次对话（不回退静态台词池）；
--- - `nil`：未处理，自动回退静态台词池。
---@alias LuaDialogHookResult string|string[]|boolean|nil

---@alias LuaDialogLineArg string|string[]

---@class LuaDialogDsl
---@field ActiveLocale string|nil 当前加载对话时生效的本地化区域标识。
---@field DeclaredDialogName string|nil
---@field DeclaredLocale string|nil
---@field DeclaredDescription string|nil
---@field DeclaredAuthor string|nil
---@field DeclaredFallback string|nil
---@field DeclaredAllowOverride boolean|nil
---@field DeclaredThrowOnError boolean|nil
---@field DeclaredFacedownMonsterName string|nil

---@class LuaDialogContext
---@field Game WindBotGameClient 当前 Bot 客户端实例。
---@field Duel WindBotDuel|nil 当前对局状态（独立构造 `Dialogs` 时可能为 `nil`）。
---@field BotField WindBotClientField|nil 己方场地快照。
---@field EnemyField WindBotClientField|nil 对方场地快照。
---@field Turn integer 当前回合数（无 `Duel` 时为 `0`）。
---@field BotName string 当前 Bot 昵称。
---@field Deck string 当前 Bot 卡组名。
---@field DialogName string 当前对话逻辑名。
---@field Locale string 当前生效的本地化区域标识（如 `"zh-CN"`、`"en-US"`，未配置时为 `""`）。
---@field ScriptChunkName string 当前脚本块名。
---@field FacedownMonsterName string 里侧表示怪兽的称呼文本。
local LuaDialogContext = {}

--- 判断当前生效区域是否匹配给定语言标签（支持精确匹配与主语言前缀匹配，例如 `"zh"` 匹配 `"zh-CN"`）。
---@param localeTag string
---@return boolean
function LuaDialogContext:IsLocale(localeTag) end

--- 立即发送一条聊天消息（受 `Game._chat` 控制）。
---@param message string
function LuaDialogContext:Say(message) end

--- 从 Lua 字符串数组表中随机挑选一条台词。
---@param lines string[]|string
---@return string
function LuaDialogContext:Pick(lines) end

--- 从指定事件的静态台词池中随机抽取一条并可选格式化 `{0}` / `{1}`。
---@param eventName WindBotDialogEvent|string
---@param arg0? any
---@param arg1? any
---@return string
function LuaDialogContext:PickEventLine(eventName, arg0, arg1) end

--- 返回指定事件当前已装配的只读静态台词列表。
---@param eventName WindBotDialogEvent|string
---@return string[]
function LuaDialogContext:GetLines(eventName) end

--- 使用 C# `string.Format` 风格格式化模板（支持 `{0}` / `{1}` 占位符）。
---@param template string
---@param arg0? any
---@param arg1? any
---@return string
function LuaDialogContext:Format(template, arg0, arg1) end

--- 输出 WindBot 信息日志。
---@param message string
function LuaDialogContext:LogInfo(message) end

--- 输出 WindBot 调试日志。
---@param message string
function LuaDialogContext:LogDebug(message) end

--- 输出 WindBot 警告/错误日志。
---@param message string
function LuaDialogContext:LogWarning(message) end

-- =============================================================================
-- 注入的对话宿主全局变量
-- =============================================================================

---@type LuaDialogContext
Dialog = nil
---@type WindBotGameClient
Game = nil

-- =============================================================================
-- xmake 风格对话 DSL 全局函数
-- =============================================================================

--- 开启一个角色对话作用域（类似 `xmake.lua` 的 `target("...")`）。单文件可包含多个 `dialog(...)`。
---@param name string 对话逻辑名（对应 `Dialog=` / `BotDialog=`）。
function dialog(name) end

--- 结束当前 `dialog(...)` 作用域，同时重置内部 `locale(...)` 作用域。
function dialog_end() end

--- 引入单个子对话 Lua 文件（xmake 风格 `include("common.lua")`），在当前上下文顺序执行（带防循环引入保护）。
---@param scriptName string
function include(scriptName) end

--- 批量引入多个子对话 Lua 文件（xmake 风格 `includes("a.lua", "b.lua")` 或 `includes({ "a.lua", "b.lua" })`）。
---@param script1 string|string[]
---@param script2? string|string[]
---@param script3? string|string[]
function includes(script1, script2, script3) end

--- 开启本地化覆盖作用域（类似 `xmake` 条件块），仅当当前生效 `Locale` 匹配任意传入标签时内部语句生效。
--- 例如 `locale("zh-CN", "zh") ... locale_end()`；不传参或传 `"default"` 匹配未指定区域。
---@param tag1? string|string[]
---@param tag2? string
---@param tag3? string
function locale(tag1, tag2, tag3) end

--- 结束当前 `locale(...)` 本地化作用域。
function locale_end() end

--- 判断当前生效 `Locale` 是否匹配任意传入标签（类似 `xmake` 的 `is_plat(...)`）。
---@param tag1? string|string[]
---@param tag2? string
---@param tag3? string
---@return boolean
function is_locale(tag1, tag2, tag3) end

--- 声明当前对话块的语言区域元数据（如 `"zh-CN"`、`"en-US"`）。
---@param localeTag string
function set_locale(localeTag) end

--- 声明对话回退基线：传入 `"auto"` 按当前 `Locale` 自动继承 `<locale>.json -> default.json`，或直接指定 `"zh-CN"` / `"default"` 等对话名。
---@param dialogName string
function set_fallback(dialogName) end

--- 设置里侧表示怪兽的称呼文本（攻击里侧怪兽时用于替换 `{1}`）。
---@param name string
function set_facedown_monster_name(name) end

--- `set_facedown_monster_name` 的 JSON 同名别名。
---@param name string
function set_facedownmonstername(name) end

--- 按事件名追加静态台词（支持单条、1~2 参或字符串数组表）。
---@param eventName WindBotDialogEvent|string
---@param lineOrTable LuaDialogLineArg
---@param extraLine? LuaDialogLineArg
function add_lines(eventName, lineOrTable, extraLine) end

--- 按事件名覆盖静态台词。
---@param eventName WindBotDialogEvent|string
---@param lineOrTable LuaDialogLineArg
---@param extraLine? LuaDialogLineArg
function set_lines(eventName, lineOrTable, extraLine) end

--- 显式清空指定事件台词（并阻止从 `set_fallback` 继承该事件）。
---@param eventName WindBotDialogEvent|string
function clear_lines(eventName) end

-- -----------------------------------------------------------------------------
-- 各事件静态台词追加（add_*）与覆盖（set_*）简写
-- -----------------------------------------------------------------------------

---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function add_welcome(line1, line2, line3) end
---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function set_welcome(line1, line2, line3) end

---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function add_deckerror(line1, line2, line3) end
---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function set_deckerror(line1, line2, line3) end
---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function add_deck_error(line1, line2, line3) end
---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function set_deck_error(line1, line2, line3) end

---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function add_duelstart(line1, line2, line3) end
---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function set_duelstart(line1, line2, line3) end
---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function add_duel_start(line1, line2, line3) end
---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function set_duel_start(line1, line2, line3) end

---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function add_newturn(line1, line2, line3) end
---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function set_newturn(line1, line2, line3) end
---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function add_new_turn(line1, line2, line3) end
---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function set_new_turn(line1, line2, line3) end

---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function add_endturn(line1, line2, line3) end
---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function set_endturn(line1, line2, line3) end
---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function add_end_turn(line1, line2, line3) end
---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function set_end_turn(line1, line2, line3) end

---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function add_directattack(line1, line2, line3) end
---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function set_directattack(line1, line2, line3) end
---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function add_direct_attack(line1, line2, line3) end
---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function set_direct_attack(line1, line2, line3) end

---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function add_attack(line1, line2, line3) end
---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function set_attack(line1, line2, line3) end

---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function add_ondirectattack(line1, line2, line3) end
---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function set_ondirectattack(line1, line2, line3) end
---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function add_on_direct_attack(line1, line2, line3) end
---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function set_on_direct_attack(line1, line2, line3) end

---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function add_activate(line1, line2, line3) end
---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function set_activate(line1, line2, line3) end

---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function add_summon(line1, line2, line3) end
---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function set_summon(line1, line2, line3) end

---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function add_setmonster(line1, line2, line3) end
---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function set_setmonster(line1, line2, line3) end
---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function add_set_monster(line1, line2, line3) end
---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function set_set_monster(line1, line2, line3) end

---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function add_chaining(line1, line2, line3) end
---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function set_chaining(line1, line2, line3) end

---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function add_surrender(line1, line2, line3) end
---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function set_surrender(line1, line2, line3) end

---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function add_custom(line1, line2, line3) end
---@param line1 LuaDialogLineArg
---@param line2? LuaDialogLineArg
---@param line3? LuaDialogLineArg
function set_custom(line1, line2, line3) end

-- -----------------------------------------------------------------------------
-- 动态对话钩子（支持匿名函数、底部具名函数名字符串或 XLua 委托）
-- -----------------------------------------------------------------------------

--- 全局出站消息拦截/改写钩子（在发送任何事件消息前触发）。
---@param fn (fun(ctx: LuaDialogContext, eventName: WindBotDialogEvent, text: string): LuaDialogHookResult)|string|nil
function on_message(fn) end

---@param fn (fun(ctx: LuaDialogContext): LuaDialogHookResult)|string|nil
function on_welcome(fn) end

---@param fn (fun(ctx: LuaDialogContext, card: string): LuaDialogHookResult)|string|nil
function on_deckerror(fn) end
---@param fn (fun(ctx: LuaDialogContext, card: string): LuaDialogHookResult)|string|nil
function on_deck_error(fn) end

---@param fn (fun(ctx: LuaDialogContext): LuaDialogHookResult)|string|nil
function on_duelstart(fn) end
---@param fn (fun(ctx: LuaDialogContext): LuaDialogHookResult)|string|nil
function on_duel_start(fn) end

---@param fn (fun(ctx: LuaDialogContext): LuaDialogHookResult)|string|nil
function on_newturn(fn) end

---@param fn (fun(ctx: LuaDialogContext): LuaDialogHookResult)|string|nil
function on_endturn(fn) end
---@param fn (fun(ctx: LuaDialogContext): LuaDialogHookResult)|string|nil
function on_end_turn(fn) end

---@param fn (fun(ctx: LuaDialogContext, attacker: string): LuaDialogHookResult)|string|nil
function on_directattack(fn) end
---@param fn (fun(ctx: LuaDialogContext, attacker: string): LuaDialogHookResult)|string|nil
function on_direct_attack(fn) end

---@param fn (fun(ctx: LuaDialogContext, attacker: string, defender: string): LuaDialogHookResult)|string|nil
function on_attack(fn) end

---@param fn (fun(ctx: LuaDialogContext, attacker?: string): LuaDialogHookResult)|string|nil
function on_ondirectattack(fn) end
---@param fn (fun(ctx: LuaDialogContext, attacker?: string): LuaDialogHookResult)|string|nil
function on_on_direct_attack(fn) end
---@param fn (fun(ctx: LuaDialogContext, attacker?: string): LuaDialogHookResult)|string|nil
function on_receive_direct_attack(fn) end

---@param fn (fun(ctx: LuaDialogContext): LuaDialogHookResult)|string|nil
function on_setmonster(fn) end
---@param fn (fun(ctx: LuaDialogContext): LuaDialogHookResult)|string|nil
function on_set_monster(fn) end

---@param fn (fun(ctx: LuaDialogContext, index: integer, ...: any): LuaDialogHookResult)|string|nil
function on_custom(fn) end

--- 模块返回式对话脚本（`return { welcome = {...}, OnSummon = ... }`）的可选接口提示。
---@class WindBotDialogModule
---@field fallback? string
---@field facedownmonstername? string
---@field welcome? string[]
---@field deckerror? string[]
---@field duelstart? string[]
---@field newturn? string[]
---@field endturn? string[]
---@field directattack? string[]
---@field attack? string[]
---@field ondirectattack? string[]
---@field activate? string[]
---@field summon? string[]
---@field setmonster? string[]
---@field chaining? string[]
---@field surrender? string[]
---@field custom? string[]
---@field OnInit? fun(ctx: LuaDialogContext)
---@field OnDispose? fun(ctx: LuaDialogContext)
---@field OnMessage? fun(ctx: LuaDialogContext, eventName: WindBotDialogEvent, text: string): LuaDialogHookResult
---@field OnWelcome? fun(ctx: LuaDialogContext): LuaDialogHookResult
---@field OnDeckError? fun(ctx: LuaDialogContext, card: string): LuaDialogHookResult
---@field OnDuelStart? fun(ctx: LuaDialogContext): LuaDialogHookResult
---@field OnNewTurn? fun(ctx: LuaDialogContext): LuaDialogHookResult
---@field OnEndTurn? fun(ctx: LuaDialogContext): LuaDialogHookResult
---@field OnDirectAttack? fun(ctx: LuaDialogContext, attacker: string): LuaDialogHookResult
---@field OnAttack? fun(ctx: LuaDialogContext, attacker: string, defender: string): LuaDialogHookResult
---@field OnOnDirectAttack? fun(ctx: LuaDialogContext, attacker?: string): LuaDialogHookResult
---@field OnActivate? fun(ctx: LuaDialogContext, spell: string): LuaDialogHookResult
---@field OnSummon? fun(ctx: LuaDialogContext, monster: string): LuaDialogHookResult
---@field OnSetMonster? fun(ctx: LuaDialogContext): LuaDialogHookResult
---@field OnChaining? fun(ctx: LuaDialogContext, card: string): LuaDialogHookResult
---@field OnSurrender? fun(ctx: LuaDialogContext): LuaDialogHookResult
---@field OnCustom? fun(ctx: LuaDialogContext, index: integer, ...: any): LuaDialogHookResult
