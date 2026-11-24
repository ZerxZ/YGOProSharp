-- =============================================================================
-- ExampleBlueEyes.common.lua
-- 青眼白龙对话公共片段：由各语言对话文件通过 includes("ExampleBlueEyes.common.lua") 共享复用。
-- =============================================================================

set_author("YGOProSharp")
-- "auto" 按当前 BotLocale/Locale 自动继承对应内置 JSON（zh-CN -> zh-CN.json，否则 default.json）
set_fallback("auto")

on_init(function(ctx)
    ctx:LogInfo("ExampleBlueEyes Lua dialog loaded (Locale=" .. ctx.Locale .. ", File=" .. ctx.ScriptChunkName .. ").")
end)

on_dispose(function(ctx)
    ctx:LogDebug("ExampleBlueEyes Lua dialog disposed.")
end)
