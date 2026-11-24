# WindBot 泛用 Lua 卡组执行器指南

`YGOProSharp.WindBot` 内置了零第三方依赖的 **Lua 卡组执行器（`LuaDeckExecutor`）**，通过 `YGOProSharp.Abstractions.Scripting` 中的泛用 Lua 抽象接口与具体 Lua 运行时解耦。

宿主项目可以自由接入 **XLua**、**MoonSharp**、**NLua**、**Lua-CSharp** 或任意自研 Lua 绑定库，而无需把任何 Lua 引擎依赖引入 `YGOProSharp.Abstractions` 或 `YGOProSharp.WindBot`。

---

## 1. 分层设计

- **泛用 Lua 契约层**（[LuaScriptingContracts.cs](../YGOProSharp.Abstractions/Scripting/LuaScriptingContracts.cs)）：
  - `ILuaRuntime`：进程/全局级运行时入口，负责为每个卡组执行器实例创建独立作用域 `ILuaScriptContext`。
  - `ILuaScriptContext : IDisposable`：隔离的脚本执行上下文，负责注入全局变量（`SetGlobal`）、执行源码（`DoString`）、解析函数（`TryGetFunction`），并提供可选的 `TryWrapFunction` 和 `TryConvertList<T>` 适配钩子。
  - `ILuaTable : IDisposable`：可选模块表抽象，支持 Lua 脚本通过 `return M` 返回模块。
  - `ILuaFunction : IDisposable`：可调用的 Lua 函数句柄，带 `Call()` / `Call(arg1)` / `Call(arg1, arg2)` 低分配重载。
  - `ILuaScriptSourceProvider`：脚本源码加载抽象，可对接文件系统、内存字典、嵌入式资源或 Unity `TextAsset` / `Addressables`。
- **WindBot Lua 执行器层**（`YGOProSharp.WindBot/Game/AI/Lua/`）：
  - [LuaDeckExecutor.cs](../YGOProSharp.WindBot/Game/AI/Lua/LuaDeckExecutor.cs)：继承 `DefaultExecutor` 并实现 `IDisposable`，自动暴露 `Self`/`Executor`/`Duel`/`AI`/`Util`/`Bot`/`Enemy` 及枚举常量，未覆写的回调自动回退到 `DefaultExecutor`。
  - [LuaDeckDescriptor.cs](../YGOProSharp.WindBot/Game/AI/Lua/LuaDeckDescriptor.cs)：描述 Lua 卡组的名称、关联 `.ydk` 文件、脚本名、等级与覆盖策略。
  - [LuaDeckScriptProviders.cs](../YGOProSharp.WindBot/Game/AI/Lua/LuaDeckScriptProviders.cs)：内置 `FileSystemLuaDeckScriptProvider`（支持目录扫描发现）和 `InMemoryLuaDeckScriptProvider`。
  - [LuaDeckHookNames.cs](../YGOProSharp.WindBot/Game/AI/Lua/LuaDeckHookNames.cs)：全部标准 Lua 回调函数名常量表。

---

## 2. 生命周期与释放保障

XLua 等绑定库要求在释放 `LuaEnv` 前释放所有 C# 持有的 `LuaTable` 与 `LuaFunction`。

YGOProSharp 已打通完整释放链路：

`WindBotService.RunBotAsync` -> `GameClient.Dispose()` -> `GameBehavior.Dispose()` -> `LuaDeckExecutor.Dispose()` -> 依次调用脚本 `OnDispose(self)`、清空 `Executors`、释放所有缓存/包装的 `ILuaFunction`、释放 `ILuaTable`、释放 `ILuaScriptContext`。

---

## 3. 接入 XLua 适配器示例

> 以下示例位于你自己的宿主/Unity 工程中（引用 `YGOProSharp.Abstractions` 与 XLua 即可，无需改动 YGOProSharp 源码）。

XLua 官方推荐全局共享单个 `LuaEnv`，并为每个脚本实例创建独立 `LuaTable` 沙箱（通过 metatable 的 `__index = _G` 继承标准库）：

```csharp
using System;
using System.Collections.Generic;
using XLua;
using YGOProSharp.Abstractions.Scripting;

public sealed class XLuaRuntimeAdapter : ILuaRuntime
{
    private readonly LuaEnv _luaEnv;

    public XLuaRuntimeAdapter(LuaEnv luaEnv)
    {
        _luaEnv = luaEnv ?? throw new ArgumentNullException(nameof(luaEnv));
    }

    public ILuaScriptContext CreateContext(string? name = null) => new XLuaScriptContext(_luaEnv);

    private sealed class XLuaScriptContext : ILuaScriptContext
    {
        private readonly LuaEnv _luaEnv;
        private readonly LuaTable _envTable;
        private readonly LuaTable _metaTable;
        private bool _disposed;

        public XLuaScriptContext(LuaEnv luaEnv)
        {
            _luaEnv = luaEnv;
            _envTable = _luaEnv.NewTable();
            _metaTable = _luaEnv.NewTable();
            _metaTable.Set("__index", _luaEnv.Global);
            _envTable.SetMetaTable(_metaTable);
        }

        public void SetGlobal(string name, object? value) => _envTable.Set(name, value);

        public bool TryGetGlobal(string name, out object? value)
        {
            value = _envTable.Get<object>(name);
            return value != null;
        }

        public object? DoString(string code, string? chunkName = null)
        {
            object[] results = _luaEnv.DoString(code, chunkName ?? "LuaDeck.lua", _envTable);
            if (results == null || results.Length == 0)
                return null;

            return results[0] is LuaTable table ? new XLuaTableAdapter(table) : results[0];
        }

        public bool TryGetFunction(string name, out ILuaFunction? function)
        {
            LuaFunction? fn = _envTable.Get<LuaFunction>(name);
            if (fn == null)
            {
                function = null;
                return false;
            }

            function = new XLuaFunctionAdapter(fn);
            return true;
        }

        public bool TryWrapFunction(object rawFunction, out ILuaFunction? function)
        {
            if (rawFunction is LuaFunction fn)
            {
                function = new XLuaFunctionAdapter(fn);
                return true;
            }

            function = null;
            return false;
        }

        public bool TryConvertList<T>(object? value, out IList<T>? list)
        {
            if (value is LuaTable table)
            {
                try
                {
                    list = table.Cast<List<T>>();
                    return list != null;
                }
                finally
                {
                    table.Dispose();
                }
            }

            list = null;
            return false;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _metaTable.Dispose();
            _envTable.Dispose();
        }
    }

    private sealed class XLuaTableAdapter : ILuaTable
    {
        private readonly LuaTable _table;
        private bool _disposed;

        public XLuaTableAdapter(LuaTable table) => _table = table;

        public bool TryGet(string key, out object? value)
        {
            value = _table.Get<object>(key);
            return value != null;
        }

        public bool TryGetFunction(string key, out ILuaFunction? function)
        {
            LuaFunction? fn = _table.Get<LuaFunction>(key);
            if (fn == null)
            {
                function = null;
                return false;
            }

            function = new XLuaFunctionAdapter(fn);
            return true;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _table.Dispose();
        }
    }

    private sealed class XLuaFunctionAdapter : ILuaFunction
    {
        private readonly LuaFunction _function;
        private bool _disposed;

        public XLuaFunctionAdapter(LuaFunction function) => _function = function;

        public object? Call(params object?[] args)
        {
            object[]? ret = _function.Call(args);
            return ret is { Length: > 0 } ? ret[0] : null;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _function.Dispose();
        }
    }
}
```

### 接入其他 Lua 库（MoonSharp / NLua / Lua-CSharp）

只需实现同一个 `ILuaRuntime` / `ILuaScriptContext` / `ILuaFunction`：
- **MoonSharp**：`CreateContext` 创建 `new Script()`；`TryWrapFunction` 包装 `Closure` / `DynValue`；`TryConvertList<T>` 遍历 `Table.Values` 转换。
- **NLua**：`CreateContext` 创建 `new NLua.Lua()`；`TryWrapFunction` 包装 `NLua.LuaFunction`；`TryConvertList<T>` 遍历 `NLua.LuaTable.Values`。
- **Lua-CSharp**：`CreateContext` 创建 `LuaState.Create()`；在 `Call` 中把 `LuaValue` 映射回 CLR 对象。

---

## 4. 在 WindBot 中注册与运行 Lua 卡组

### 方式 A：通过 `WindBotRuntimeOptions` 统一配置

```csharp
ILuaRuntime luaRuntime = new XLuaRuntimeAdapter(luaEnv);
FileSystemLuaDeckScriptProvider scriptProvider = new("LuaDecks");

WindBotRuntimeOptions runtimeOptions = new()
{
    CardRepository = cards,
    LuaRuntime = luaRuntime,
    LuaScriptProvider = scriptProvider,
    LuaDecks =
    [
        new LuaDeckDescriptor(
            name: "MyLuaBlueEyes",
            file: "AI_BlueEyes",
            scriptName: "MyLuaBlueEyes.lua",
            level: "Normal",
            allowOverride: false)
    ],
    PreferLuaDecks = false,
    ThrowOnLuaScriptError = false
};

WindBotService service = new(runtimeOptions);
await service.RunBotAsync(new WindBotInfo { Deck = "MyLuaBlueEyes" }, cancellationToken);
```

### 方式 B：免注册动态按脚本名解析

只要配置了 `LuaRuntime` 和 `LuaScriptProvider`，当 `WindBotInfo.Deck` 指向某个未在 C# 中注册的卡组名（例如 `MyDeck`）且 `scriptProvider` 能解析到 `MyDeck.lua` 时，`DecksManager.Instantiate` 会自动实例化 `LuaDeckExecutor`。

### 方式 C：覆写内置 C# 卡组

- 单卡组覆写：注册 `LuaDeckDescriptor` 时设置 `allowOverride: true`。
- 全局优先 Lua：在 `WindBotRuntimeOptions` 或 `DecksManager.ConfigureLua(...)` 中设置 `preferLuaDecks: true`。

### 方式 D：通过 `YGOProSharp.Cli` 开箱即用（内置 NLua 5.4 运行时）

`YGOProSharp.Abstractions` 与 `YGOProSharp.WindBot` 保持零第三方 Lua 依赖；`YGOProSharp.Cli` 作为组合根内置了稳定的 **NLua（Lua 5.4，MIT License）** 适配器（[NLuaRuntime.cs](../YGOProSharp.Cli/NLuaRuntime.cs)）。

全部内置 Lua 脚本均集中存放在 `YGOProSharp.WindBot` 下，并在构建时随 `Decks/**/*` 与 `Dialogs/**/*` 自动复制到输出目录：

- 卡组脚本目录：`YGOProSharp.WindBot/Decks/Lua/`（内置示例：[ExampleOldSchool.lua](../YGOProSharp.WindBot/Decks/Lua/ExampleOldSchool.lua)、[ExampleBlueEyes.lua](../YGOProSharp.WindBot/Decks/Lua/ExampleBlueEyes.lua)）
- 对话脚本目录：`YGOProSharp.WindBot/Dialogs/Lua/`（内置示例：[ExampleBlueEyes.lua](../YGOProSharp.WindBot/Dialogs/Lua/ExampleBlueEyes.lua)）
- 开发者智能提示：`YGOProSharp.WindBot/lua-api/`（[windbot-deck.lua](../YGOProSharp.WindBot/lua-api/windbot-deck.lua)、[windbot-dialog.lua](../YGOProSharp.WindBot/lua-api/windbot-dialog.lua) 与仓库根目录 [.luarc.json](../.luarc.json)）

直接通过命令行即可运行：

```powershell
dotnet run --project YGOProSharp.Cli/YGOProSharp.Cli.csproj -c Release -- windbot DbPath=cards.cdb Host=127.0.0.1 Port=7911 Deck=ExampleOldSchool LogLevel=Debug
```

相关 CLI 可选参数：
- `LuaDeckDir` / `LuaDeckDirs`：额外指定 Lua 卡组脚本目录（支持 `;` 或 `,` 分隔，默认优先扫描 `Decks/Lua`、`Deck/Lua`）。
- `PreferLuaDecks`：`true` 时同名卡组优先使用 Lua 版本。
- `ThrowOnLuaScriptError`：`true` 时 Lua 钩子异常直接抛出，便于开发调试。
- `EnableLuaDecks`：默认 `true`，设为 `false` 可关闭 CLI 的 Lua 运行时装配。

---

## 5. Lua 卡组脚本写法示例

`LuaDeckExecutor` 同时支持三种写法，可按习惯选择或混用：
1. **xmake 风格声明式 DSL（推荐）**：`deck("...") ... set_* ... add_* ... on_* ... deck_end()`
2. **模块返回式**：`local M = {}; ... return M`
3. **全局函数式**：直接定义 `function OnInit(self) ... end`

### 5.1 xmake 风格声明式 DSL（推荐）

支持顶层公共规则 + `deck("Name") ... deck_end()` 作用域（类似 `xmake.lua` 的全局作用域与 `target("...") ... target_end()`），也支持规则引用脚本下方才定义的函数名，以及单文件声明多个卡组：

```lua
-- 文件顶层规则：类似 xmake.lua 顶层作用域，对当前脚本内的所有 deck 生效
add_default("SpellSet", "DefaultSpellSet")
add_default("Repos", "DefaultMonsterRepos")

deck("MyBlueEyes")
    set_deckfile("AI_BlueEyes")
    set_level("Normal")
    set_description("Blue-Eyes Lua AI")
    set_author("YGOProSharp")

    -- 复用 DefaultExecutor 内置规则
    add_default("Activate", 5318639, "DefaultMysticalSpaceTyphoon")
    add_default("Activate", 12580477, "DefaultRaigeki")

    -- 批量为多张卡注册相同动作规则（类似 xmake add_files）
    add_executors("SummonOrSet", { 43096270, 69247929, 77542832 })

    -- 语义化简写：on_activate / on_summon / on_spsummon / on_repos / on_spell_set / on_monster_set / on_summon_or_set
    -- 规则回调可直接写匿名函数，或写函数名（支持先声明、后定义）
    on_summon(89631139, "can_summon_blue_eyes")
    on_activate(38517737, function(ctx)
        return ctx.BotField:HasInHand(89631139)
    end)

    -- 生命周期与决策钩子（类似 xmake 的 on_load / on_build / on_clean）
    on_init(function(ctx)
        ctx:LogInfo("MyBlueEyes Lua deck loaded")
    end)

    on_new_turn(function(ctx)
        ctx:LogDebug("Turn " .. tostring(ctx.Duel.Turn))
    end)

    on_select_yesno(function(ctx, desc)
        -- 返回 nil 自动回退 DefaultExecutor 默认实现
        return nil
    end)

    on_dispose(function(ctx)
        ctx:LogDebug("MyBlueEyes Lua deck disposed")
    end)
deck_end()

-- 支持在脚本底部定义具名规则函数，DSL 会在 DoString 完成后按书写顺序延迟解析
function can_summon_blue_eyes(ctx)
    if ctx.CurrentCard == nil then
        return false
    end
    return ctx:CallDefaultRule("DefaultMonsterSummon")
end
```

#### 常用 DSL 速查

| 分类 | DSL 函数 | 说明 |
| --- | --- | --- |
| 作用域与元数据 | `deck(name)` / `deck_end()` | 开启/结束当前卡组作用域；单文件可写多个 `deck`，顶层规则对全部生效 |
| 作用域与元数据 | `set_deckfile(file)` / `set_level(level)` / `set_override(bool)` / `set_description(desc)` / `set_author(author)` / `set_throw_on_error(bool)` | 配置 `.ydk` 文件名、难度、覆盖标记与错误处理 |
| 规则注册 | `add_executor(type, cardId?, callback?)` | 注册单条规则（支持 `ExecutorType` 枚举、整数或字符串） |
| 规则注册 | `add_executors(type, { id1, id2, ... }, callback?)` | 批量为多个卡号注册同一规则 |
| 规则注册 | `add_default(type, cardId?, ruleName)` | 注册 `DefaultExecutor` 内置规则（如 `"DefaultTrap"`、`"DefaultMonsterRepos"`） |
| 动作简写 | `on_activate` / `on_summon` / `on_spsummon` / `on_repos` / `on_monster_set` / `on_spell_set` / `on_summon_or_set` / `on_goto_battle` / `on_goto_main2` / `on_goto_end` / `on_surrender` | 按动作类型直接注册规则 |
| 生命周期与决策钩子 | `on_init` / `on_dispose` / `on_new_turn` / `on_new_phase` / `on_draw` / `on_move` / `on_chaining` / `on_chain_solved` / `on_chain_end` / `on_select_chain` / `on_pre_activate` / `on_pre_battle` / `on_battle` / `on_select_attacker` / `on_select_attack_target` / `on_select_battle_replay` / `on_select_card` / `on_select_sum` / `on_select_fusion_material` / `on_select_synchro_material` / `on_select_xyz_material` / `on_select_link_material` / `on_select_ritual_tribute` / `on_select_pendulum_summon` / `on_card_sorting` / `on_select_yesno` / `on_select_option` / `on_select_place` / `on_select_position` / `on_select_monster_summon_or_set` / `on_announce_card` / `on_rock_paper_scissors` / `on_select_hand` / `on_sp_summoned` / `on_player_hint` / `on_hint_zone` / `on_receiving_announce` | 覆盖 `Executor` / `DefaultExecutor` 对应回调，返回 `nil` 时回退默认逻辑 |

> **XLua 提示**：由于 DSL 函数通过 `LuaDslAction0` / `LuaDslAction1` / `LuaDslAction2` / `LuaDslAction3` 委托注入到全局表，如果开启了 XLua 代码生成，建议将这 4 个委托类型加入 XLua 的 `[LuaCallCSharp]` 配置列表，并把 `LuaDeckExecutor`、`Duel`、`GameAI`、`AIUtil`、`ClientField`、`ClientCard` 一并加入以获得最佳性能。

### 5.2 模块返回式（`return M`）

```lua
local M = {}

local CARD_MST = 5318639
local CARD_BLUE_EYES = 89631139

function M.OnInit(self)
    -- 1. 复用 DefaultExecutor 内置规则
    self:AddDefaultExecutor("Activate", CARD_MST, "DefaultMysticalSpaceTyphoon")
    self:AddDefaultExecutor("SpellSet", "DefaultSpellSet")
    self:AddDefaultExecutor("Repos", "DefaultMonsterRepos")

    -- 2. 绑定模块内函数名（所有 Lua 引擎 100% 通用，不依赖闭包转 C# Delegate）
    self:AddLuaExecutor("Summon", CARD_BLUE_EYES, "CanSummonBlueEyes")

    -- 3. 直接传匿名函数（支持 XLua 原生 Func<bool> 或 TryWrapFunction 包装）
    self:AddLuaExecutor(ExecutorType.Activate, 12580477, function(exec)
        return exec:CallDefaultRule("DefaultRaigeki")
    end)
end

function M.CanSummonBlueEyes(self)
    if self.CurrentCard == nil then
        return false
    end
    return self:CallDefaultRule("DefaultMonsterSummon")
end

function M.OnNewTurn(self)
    self:LogDebug("New turn: " .. tostring(self.Duel.Turn))
end

function M.OnSelectYesNo(self, desc)
    -- 返回 nil 表示未处理，自动回退 DefaultExecutor 默认实现
    return nil
end

function M.OnDispose(self)
    -- 可选清理钩子
end

return M
```

---

## 6. Lua 对话系统（xmake 声明式 DSL / XLua 风格）

`YGOProSharp.WindBot` 的对话系统（[Dialogs.cs](../YGOProSharp.WindBot/Game/AI/Dialogs.cs) / [DialogsManager.cs](../YGOProSharp.WindBot/Game/AI/DialogsManager.cs)）采用与 Lua 卡组执行器完全一致的泛用 `ILuaRuntime` 抽象与 **xmake 风格 DSL**，兼容 XLua、NLua、MoonSharp、Lua-CSharp，同时 100% 保留原有 `Dialogs/*.json` 兼容。

### 6.1 模块与释放链路

- [LuaDialogDsl.cs](../YGOProSharp.WindBot/Game/AI/Lua/LuaDialogDsl.cs)：xmake 风格声明式 DSL 宿主，复用 `LuaDslAction0`~`LuaDslAction3` 固定参委托，避免 XLua 变参桥接问题。
- [LuaDialogScript.cs](../YGOProSharp.WindBot/Game/AI/Lua/LuaDialogScript.cs)：支持 **xmake 风格 DSL**、**模块表返回式（`return { welcome = {...}, OnSummon = ... }`）**、**全局函数式** 三种写法。
- [LuaDialogContext.cs](../YGOProSharp.WindBot/Game/AI/Lua/LuaDialogContext.cs)：向动态回调暴露 `Self` / `Dialog`（含 `Game`、`Duel`、`BotField`、`EnemyField`、`Turn`、`BotName`、`Deck`、`Pick(...)`、`PickEventLine(...)`、`Format(...)` 等）。
- [LuaDialogScriptProviders.cs](../YGOProSharp.WindBot/Game/AI/Lua/LuaDialogScriptProviders.cs)：提供 `FileSystemLuaDialogScriptProvider`（默认优先扫描 `Dialogs/Lua`、`Dialog/Lua`、`Dialogs`）、`InMemoryLuaDialogScriptProvider`、`CompositeLuaDialogScriptProvider`。
- **完整释放链路（XLua 安全）**：
  `GameClient.Dispose()` -> `GameBehavior.Dispose()` -> `GameAI.Dispose()` -> `Dialogs.Dispose()` -> `LuaDialogScript.Dispose()`（触发 `on_dispose`，释放所有缓存 `ILuaFunction`、`ILuaTable`、`ILuaScriptContext`）。纯静态声明脚本（无动态钩子）在加载完成后即主动释放底层上下文。

### 6.2 多文件组织、本地化（i18n）与聊天节奏控制

WindBot Dialog 同时支持 **像 JSON 一样按文件拆分维护**、**子目录归档**、**xmake 风格 `includes(...)` 组合** 以及 **单文件 `locale(...)` 条件块**：

1. **按文件拆分（推荐，便于维护）**：
   - 扁平拆分（与 `Dialogs/*.json` 同风格）：`Dialogs/Lua/ExampleBlueEyes.lua`、`Dialogs/Lua/ExampleBlueEyes.zh-CN.lua`、`Dialogs/Lua/ExampleBlueEyes.zh-TW.lua`，共用片段放进 `ExampleBlueEyes.common.lua` 并通过 `includes("ExampleBlueEyes.common.lua")` 引入。
   - 子目录归档：`Dialogs/Lua/<角色名>/default.lua`、`Dialogs/Lua/<角色名>/zh-CN.lua`、`Dialogs/Lua/<角色名>/zh-TW.lua`。
2. **自动本地化文件解析（JSON 与 Lua 均适用）**：
   - 当 `Dialog=ExampleBlueEyes` 且 `Locale=zh-CN` 时，自动按 `ExampleBlueEyes.zh-CN` -> `ExampleBlueEyes.zh` -> `ExampleBlueEyes` 顺序查找（直接命中 `Dialogs/Lua/ExampleBlueEyes.zh-CN.lua`）。
   - 当 `Dialog=cirno` 且 `Locale=zh-CN` 时，直接命中 `Dialogs/cirno.zh-CN.json`；当 `Dialog=default` 且 `Locale=zh-CN` 时，直接命中 `Dialogs/zh-CN.json`。
3. **跨文件回退继承**：`set_fallback("auto")` 自动按当前 `Locale` 继承 `<locale>.json` -> `default.json`；`set_fallback("OtherLuaDialog")` 也支持跨 `.lua` 对话继承（内置循环检测）。
4. **MDPro3 友好的聊天节流与开局延迟**：
   - 服务端 `Game.Chat` 广播 `StocMessage.Chat` 给全体客户端（含发送者自身），确保 MDPro3 双方聊天均正常显示；
   - Bot 端 `GameClient` 内置非阻塞聊天节流队列：开局延后 `DuelStartChatDelayMilliseconds`（默认 `4000ms`，等待 MDPro3 完成 3D 对局场景加载与开局过渡）、连续消息间隔 `ChatIntervalMilliseconds`（默认 `800ms`）、队列上限 `MaxQueuedChatMessages`（默认 `6` 条，避免连招积压过久）；人类玩家入房时 Bot 自动补发欢迎语。

多文件示例见 [ExampleBlueEyes.common.lua](../YGOProSharp.WindBot/Dialogs/Lua/ExampleBlueEyes.common.lua)、[ExampleBlueEyes.lua](../YGOProSharp.WindBot/Dialogs/Lua/ExampleBlueEyes.lua)、[ExampleBlueEyes.zh-CN.lua](../YGOProSharp.WindBot/Dialogs/Lua/ExampleBlueEyes.zh-CN.lua) 与 [ExampleBlueEyes.zh-TW.lua](../YGOProSharp.WindBot/Dialogs/Lua/ExampleBlueEyes.zh-TW.lua)。

### 6.3 对话 DSL 速查

| 分类 | DSL 函数 | 说明 |
| --- | --- | --- |
| 作用域与多文件 | `dialog(name)` / `dialog_end()` / `includes(f1, f2?, f3?)` / `include(f)` | 开启/结束对话作用域，或按 xmake 风格组合引入多个子对话 Lua 文件 |
| 作用域与本地化 | `locale(tag1?, tag2?, tag3?)` / `locale_end()` / `is_locale(tag1?, tag2?, tag3?)` | 按当前 `Locale`（支持精确与主语言前缀，如 `"zh"` 匹配 `"zh-CN"`）条件启用多语言台词块 |
| 元数据与回退 | `set_locale(locale)` / `set_description(desc)` / `set_author(author)` / `set_fallback(name)` / `set_override(bool)` / `set_throw_on_error(bool)` / `set_facedown_monster_name(name)` | 元数据、`"auto"`/指定 JSON 回退继承、同名覆盖、错误处理与里侧怪兽称呼 |
| 台词声明 | `add_lines(event, lines)` / `set_lines(event, lines)` / `clear_lines(event)` | 通用按事件名追加、覆盖或显式清空（阻止回退） |
| 事件简写 | `add_*` / `set_*`：`welcome`、`deckerror`(`deck_error`)、`duelstart`(`duel_start`)、`newturn`(`new_turn`)、`endturn`(`end_turn`)、`directattack`(`direct_attack`)、`attack`、`ondirectattack`(`on_direct_attack`)、`activate`、`summon`、`setmonster`(`set_monster`)、`chaining`、`surrender`、`custom` | 按事件类型追加或替换静态台词 |
| 动态钩子 | `on_init` / `on_dispose` / `on_message` / `on_welcome` / `on_deckerror` / `on_duelstart` / `on_newturn` / `on_endturn` / `on_directattack` / `on_attack` / `on_ondirectattack` / `on_activate` / `on_summon` / `on_setmonster` / `on_chaining` / `on_surrender` / `on_custom`（均支持 snake_case 别名） | 动态生成台词或静默拦截，支持匿名函数、延迟解析函数名及 XLua 委托 |

### 6.4 配置、CLI 参数与开发者智能提示

- **代码装配**：在 `WindBotRuntimeOptions` 中配置 `Locale`、`LuaRuntime`、`LuaDialogScriptProvider`、`LuaDialogs`、`PreferLuaDialogs`（或在 `WindBotInfo.Locale` 为单个 Bot 指定语言）。
- **解析优先级**：显式 `.lua` 扩展名 > 已注册且 `AllowOverride=true` 的 `LuaDialogDescriptor` > `PreferLuaDialogs=true` 时的同名/本地化 Lua 脚本 > 内置本地化 JSON（`<name>.<locale>.json` -> `<name>.json`） > 动态按名/本地化解析的 `.lua` > 优雅退化为空对话。
- **CLI 参数**：
  - `Dialog` / `BotDialog`：指定对话名（如 `BotDialog=ExampleBlueEyes`、`Dialog=cirno`）。
  - `Locale` / `BotLocale`：指定语言区域（如 `BotLocale=zh-CN`、`Locale=zh-TW`、`Locale=en-US`）。
  - `ChatIntervalMs` / `BotChatIntervalMs`：连续聊天最小间隔（默认 `800` ms，设为 `0` 立即发送）。
  - `DuelStartChatDelayMs` / `BotDuelStartChatDelayMs`：开局首条聊天延迟（默认 `4000` ms，等待 MDPro3 完成对局场景加载）。
  - `MaxQueuedChatMessages`：排队聊天上限（默认 `6` 条）。
  - `LuaDialogDir` / `LuaDialogDirs`：额外指定 Lua 对话脚本目录（支持 `;` 或 `,` 分隔，默认优先扫描 `Dialogs/Lua`、`Dialog/Lua`、`Dialogs`）。
  - `PreferLuaDialogs`：`true` 时同名对话优先加载 `.lua` 而非 `.json`。
  - `EnableLuaDialogs`：默认跟随 `EnableLuaDecks`（为 `true`），设为 `false` 可关闭 CLI Lua 对话加载。
- **开发者 IDE 提示（LuaLS / EmmyLua）**：见 [YGOProSharp.WindBot/lua-api/README.md](../YGOProSharp.WindBot/lua-api/README.md) 与根目录 [.luarc.json](../.luarc.json)。
- **XLua 提示**：开启 XLua 代码生成时，建议将 `LuaDialogContext`、`LuaDialogDsl`、`LuaDslAction0`~`LuaDslAction3` 与 `LuaDslPredicate3` 一并加入 `[LuaCallCSharp]`。
