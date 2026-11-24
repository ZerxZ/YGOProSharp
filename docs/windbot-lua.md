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

`YGOProSharp.Abstractions` 与 `YGOProSharp.WindBot` 保持零第三方 Lua 依赖；`YGOProSharp.Cli` 作为组合根内置了稳定的 **NLua（Lua 5.4，MIT License）** 适配器（[NLuaRuntime.cs](../YGOProSharp.Cli/NLuaRuntime.cs)）以及示例卡组脚本 [ExampleOldSchool.lua](../YGOProSharp.Cli/LuaDecks/ExampleOldSchool.lua)，直接通过命令行即可运行：

```powershell
dotnet run --project YGOProSharp.Cli/YGOProSharp.Cli.csproj -c Release -- windbot DbPath=cards.cdb Host=127.0.0.1 Port=7911 Deck=ExampleOldSchool LogLevel=Debug
```

相关 CLI 可选参数：
- `LuaDeckDir` / `LuaDeckDirs`：额外指定 Lua 卡组脚本目录（支持 `;` 或 `,` 分隔，默认自动扫描 `LuaDecks`）。
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
