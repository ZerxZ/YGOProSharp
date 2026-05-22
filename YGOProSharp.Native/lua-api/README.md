# ygopro-core Lua API 类型声明

本目录整理 `ygopro-core` 暴露给 Lua 卡片脚本的 API 类型声明、调用示例和覆盖报告。它面向 LuaLS / EmmyLua，用于编辑器补全和静态检查，不参与对局运行时加载。

## 文件

| 文件 | 说明 |
|---|---|
| `ygopro-core.lua` | LuaLS/EmmyLua `---@meta` 类型文件，覆盖 `Card`、`Effect`、`Group`、`Duel`、`Debug` 全部 native 注册 API。 |
| `API-COVERAGE.md` | 从 `luaL_Reg` 注册表生成的覆盖报告，列出每个 API 的 C++ 入口和签名状态。 |
| `generator.lua` | Xmake Lua 生成器，读取 `YGOProSharp.Native/ygopro-core/lib*.cpp` 并重建类型文件和覆盖报告。 |
| `generate-ygopro-lua-api.lua` | 直接 `xmake lua` 调试入口，和 xmake task 使用同一份生成逻辑。 |
| `examples/*.lua` | 原创最小调用示例，只用于说明脚本写法，不作为运行时脚本。 |

## 覆盖范围

`ygopro-core.lua` 来自以下 native 注册表：

| 全局表 / 类型 | 数量 | 来源 |
|---|---:|---|
| `Card` | 276 | `libcard.cpp` |
| `Effect` | 55 | `libeffect.cpp` |
| `Group` | 40 | `libgroup.cpp` |
| `Duel` | 229 | `libduel.cpp` |
| `Debug` | 11 | `libdebug.cpp` |

这些数量应与 `API-COVERAGE.md` 保持一致。若上游 `ygopro-core` 增删 Lua API，先更新 native 注册表，再运行生成器。

## LuaLS 使用方式

把本目录加入 LuaLS 的 `workspace.library`，例如：

```json
{
  "Lua.workspace.library": [
    "YGOProSharp.Native/lua-api"
  ],
  "Lua.diagnostics.globals": [
    "aux",
    "GetID"
  ]
}
```

也可以只把 `ygopro-core.lua` 放到编辑器能索引的类型目录里。该文件只用于编辑器，不要放进 `script_reader` 会加载的卡片脚本目录。

## 调用约定

- `Card`、`Effect`、`Group` 通常使用冒号调用，例如 `c:RegisterEffect(e)`、`e:SetTarget(s.tg)`、`g:GetCount()`。
- `Duel`、`Debug` 通常使用点调用，例如 `Duel.SelectMatchingCard(...)`、`Debug.Message(...)`。
- `Effect.CreateEffect(c)` 和 `Group.CreateGroup()` 是构造入口。
- `condition`、`cost`、`target`、`operation` 回调使用 YGOPro 常见签名：`e, tp, eg, ep, ev, re, r, rp`，其中 `target` / `cost` 通常额外接收 `chk`。

最小脚本结构：

```lua
local s, id = GetID()

function s.initial_effect(c)
    local e1 = Effect.CreateEffect(c)
    e1:SetType(EFFECT_TYPE_SINGLE)
    e1:SetCode(EFFECT_UPDATE_ATTACK)
    e1:SetValue(500)
    c:RegisterEffect(e1)
end
```

更多示例见 `examples/`。

## 签名精度

类型文件分为两类签名：

- **精确签名**：常用 API 已补充明确参数和返回值，例如 `Effect.SetTarget`、`Group.Filter`、`Duel.SelectMatchingCard`。
- **保守签名**：长尾 API 只从 native 注册表确认函数名和 C++ 入口，参数使用 `...`，返回值使用宽类型。这样可以保证补全完整，同时避免在语义未逐项核对前写入错误窄类型。

需要继续精细化时，以 C++ 实现、上游脚本调用习惯和现有卡片脚本为准。每次精细化后同步更新 `API-COVERAGE.md`。

## 重新生成

在仓库根目录运行：

```powershell
xmake gen-lua-api -P YGOProSharp.Native
xmake gen-lua-api -P YGOProSharp.Native --check
```

也可以直接运行脚本入口：

```powershell
xmake lua YGOProSharp.Native/lua-api/generate-ygopro-lua-api.lua --project=YGOProSharp.Native --check
```

说明：Xmake 的命令语法是 `xmake [task] [options]`，本地 task/plugin 需要把
`gen-lua-api` 放在 `-P` 前面；`-P` 仍然指定 `YGOProSharp.Native` 项目目录。

生成器会重写：

- `YGOProSharp.Native/lua-api/ygopro-core.lua`
- `YGOProSharp.Native/lua-api/API-COVERAGE.md`

生成后建议检查：

```powershell
rg -n "<mojibake-patterns>" YGOProSharp.Native\lua-api
git diff --check
```
