# WindBot Lua 开发者类型声明（LuaLS / EmmyLua）

本目录提供给 IDE / 编辑器（VS Code `LuaLS`、JetBrains Rider `EmmyLua` 等）使用的 `---@meta` 类型提示文件，覆盖 WindBot Lua 卡组（`Decks/Lua/*.lua`）与 Lua 对话（`Dialogs/Lua/*.lua`）的全部 xmake 风格 DSL 函数、枚举常量、宿主对象与回调签名。

> 本目录只用于开发期补全和静态检查，不会随构建复制到运行目录，也不参与运行时加载。

## 文件说明

| 文件 | 适用目录 | 内容 |
|---|---|---|
| [windbot-deck.lua](windbot-deck.lua) | `YGOProSharp.WindBot/Decks/Lua/*.lua` | 卡组 DSL（`deck` / `set_*` / `add_executor` / `add_default` / `on_*`）、全部 68 条 `DefaultRuleName` 字面量联合提示、全部 44 个生命周期钩子、`ExecutorType`/`CardLocation`/`CardPosition` 等枚举，以及 `LuaDeckExecutor` / `WindBotDuel` / `WindBotGameAI` / `WindBotAIUtil` / `WindBotClientField` / `WindBotClientCard` 成员定义。 |
| [windbot-dialog.lua](windbot-dialog.lua) | `YGOProSharp.WindBot/Dialogs/Lua/*.lua` | 对话 DSL（`dialog` / `locale` / `is_locale` / `set_fallback` / `add_*` / `set_*` / `clear_lines` / `on_*`）、事件名联合类型 `WindBotDialogEvent`、`LuaDialogContext` 成员定义与模块返回表类型 `WindBotDialogModule`。 |

## IDE 配置方式

### VS Code（LuaLS）

仓库根目录已提供 [.luarc.json](../../.luarc.json)，安装 **Lua（sumneko.lua）** 插件打开仓库即可自动启用补全与悬浮文档，无需额外配置。

如果你在外部宿主项目（如 Unity + XLua）中编写脚本，只需把本目录加入 `.luarc.json`：

```json
{
  "$schema": "https://raw.githubusercontent.com/LuaLS/vscode-lua/master/setting/schema.json",
  "runtime.version": "Lua 5.4",
  "workspace.library": [
    "YGOProSharp.WindBot/lua-api"
  ]
}
```

### JetBrains Rider / IDEA（EmmyLua）

1. 安装 **EmmyLua** 或 **EmmyLua2** 插件。
2. 在项目设置中将 `YGOProSharp.WindBot/lua-api` 标记为 **Lua Source Root**（或加入 EmmyLua 的 External Library）。
3. 打开 `YGOProSharp.WindBot/Decks/Lua/ExampleBlueEyes.lua` 或 `YGOProSharp.WindBot/Dialogs/Lua/ExampleBlueEyes.lua` 即可获得自动补全、参数类型检查与中文文档悬浮提示。

## 关于同名简写函数

`on_activate`、`on_summon`、`on_chaining`、`on_new_turn`、`on_init`、`on_dispose` 在卡组 DSL 与对话 DSL 中同名但签名不同：

- 在 `Decks/Lua/*.lua` 中：接收卡号/卡组规则回调 `fun(ctx: LuaDeckExecutor): boolean`；
- 在 `Dialogs/Lua/*.lua` 中：接收对话事件回调 `fun(ctx: LuaDialogContext, ...): string|string[]|boolean|nil`。

在单脚本里如需让编辑器明确区分具名回调的 `ctx` 类型，推荐在函数定义上方写一行注解：

```lua
---@param ctx LuaDeckExecutor
function be_dragon_shrine(ctx)
    ...
end
```

```lua
---@param ctx LuaDialogContext
---@param monster string
---@return string|nil
function format_blue_eyes_summon(ctx, monster)
    ...
end
```
