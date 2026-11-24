# YGOProSharp.Server

`YGOProSharp.Server` 保存服务端运行状态机与可扩展的房间/DIY 架构。它组合 `Core`、`Protocol` 和 `NativeApi`，但不承担命令行参数解析，也不直接处理 console host 生命周期。

## 职责

- 服务端库入口：`YGOProSharpServer.CreateHost` / `YGOProSharpServer.RunAsync` / `YGOProSharpServer.ResolveDatabasePaths`。
- 嵌入式宿主：`YGOProSharpServerHost`（支持纯内存无端口运行、手动 `Tick`、后台循环、活动房间列表查询 `GetRooms()`）。
- 服务监听、客户端管理与单/多房间路由：`CoreServer`。
- 玩家协议适配器：`Player`（解析 `CTOS_JOIN_GAME` 房号/规则前缀、`CTOS_CREATE_GAME`、`CTOS_CHAT` 等）。
- 房间与 duel 生命周期：`Game`（含 `OnBeforePlayerChat`、`OnBeforeDuelStart`、`IDeckValidator`、`ExtraScripts` 预加载）。
- 房号规则解析器：`RoomPasswordParser`（支持 `M#`、`T#`、`DIY#`、`LP<n>`、`MR<n>`、`LF<n>`、`NF`、`BO<n>`、`NC`、`NS`、`PR` 等以及自定义 DIY token 注册）。
- 插件机制：`AddonBase`、`AddonsManager`、`StandardStreamProtocol`、`ChatCommandAddon`（内置 `/help`、`/rules`、`/room`、`/rooms`、`/surrender` 并支持自定义命令）。

## 扩展与玩家 DIY 能力

### 1. 分层资源覆盖（无需修改基础卡库）

`YGOProSharpServer` 在 `AutoLoadExpansions = true`（默认开启）时会自动扫描 `<RootPath>/expansions` 与 `<RootPath>/diy`，并支持通过 `ServerOptions` 显式配置叠加路径或代码级数据源：

- **卡库（`.cdb`）**：基础 `cards.cdb` → `expansions/*.cdb` → `diy/*.cdb` → `ExtraDatabases`（后者覆盖前者），外层还可叠加 `AdditionalCardRepositories`（纯内存 DIY 卡）。
- **卡脚本（`.lua`）**：`AdditionalScriptProviders`（内存覆写）→ `ExtraScriptDirectories` → `diy/script/` → `expansions/script/` → 基础 `script/` → `.ypk`/`.zip` 扩展包（`ZipArchiveScriptProvider`）。
- **禁限表（`lflist.conf`）**：`ExtraBanlistFiles` → `diy/lflist.conf` → `expansions/lflist.conf` → 基础 `lflist.conf`。
- **DIY 卡片区域**：`Region = 3`（或密码前缀 `DIY#` / `CUSTOM#`）允许 `ot = 4` 的 DIY 卡与 OCG/TCG 混用；`Region = 5`（`ALL#`）放行全部区域。

### 2. 对局规则与插件钩子

- **`GameOptions.ExtraScripts`**：在 `IDuelSession.Start` 前自动调用 `PreloadScript(...)`，适合全局 DIY 规则/技能脚本。
- **`Game.OnBeforeDuelStart`**：在卡组已写入且 `IDuelSession.Start` 之前触发，暴露 `DuelStartContext`（可调用 `Session.PreloadScript`、`Session.AddCard` 或修改 `DuelOptions`）。
- **`GameOptions.DeckValidator`（`IDeckValidator`）**：可替换默认卡组校验逻辑（如单卡限制、主题限定、DIY 专场）。
- **`Game.OnBeforePlayerChat` & `ChatCommandAddon`**：拦截 `/command` 消息避免刷屏广播，并可通过 `ServerOptions.ChatCommands` 注册自定义房间命令。
  - 查询类：`/help [命令名|分类]`、`/rules`、`/room`、`/rooms`、`/banlists [页码]`
  - 玩家与娱乐：`/ready`、`/unready`、`/duelist`、`/observer`、`/surrender`（`/ff`、`/gg`）、`/roll [上限]`、`/coin`
  - 房主实时改规则（自动同步客户端左侧面板）：`/lp`、`/mr`、`/region`、`/banlist`、`/nocheck`、`/noshuffle`、`/hand`、`/draw`、`/timer`、`/mode`、`/preset`、`/script`、`/kick`、`/start`
  - CLI 宿主额外注入：`/ai [卡组名] [对话名]`、`/decks [关键字]`
- **`ServerOptions.AddonFactories`**：为每个房间挂载自定义 `AddonBase`（房间关闭时自动 `Dispose()`）。

### 3. 多房间模式与房号语法

- 默认 `EnableMultiRoom = false` 保持单房间兼容；设为 `true` 时按 `JoinGame` 密码 / `CreateGame` 房间名动态创建并隔离多个并发房间，房间结束后自动清理并释放插件。
- 可通过 `RoomPasswordParser.RegisterToken` / `RegisterPrefixToken` 注册自定义 DIY 模式关键字（如 `BOSS#`、`SPEED#`）。

## Typed Options

Server 通过 `ServerOptions` 与 `GameOptions` 接收强类型配置：

- 路径与分层 DIY：`RootPath`、`ScriptDirectory`、`DatabaseFile`、`BanlistFile`、`AutoLoadExpansions`、`ExpansionDirectories`、`ExtraDatabases`、`ExtraScriptDirectories`、`ExtraScriptArchives`、`ExtraBanlistFiles`、`AdditionalCardRepositories`、`AdditionalScriptProviders`
- 网络与多房间：`Port`、`EnableNetworkListener`、`ClientVersion`、`EnableMultiRoom`、`MaxRooms`、`ApplyPasswordRulesInSingleRoom`、`RoomPasswordParser`
- 插件与命令：`StandardStreamProtocol`、`EnableChatCommands`、`ChatCommands`、`AddonFactories`
- 对局与 DIY：`Mode`、`Region`、`MasterRule`、`Banlist`、`StartLp`、`StartHand`、`DrawCount`、`GameTimer`、`MatchWins`、`DeckRules`、`DeckValidator`、`ExtraScripts`、`CustomTags`

`Key=Value`、`Config=...` 和子命令解析属于 `YGOProSharp.Cli`。Server 项目内不保留 `Config.Load(args)` 这类 CLI 职责。

## 边界

Server 可以引用 `Core`、`Protocol`、`NativeApi`。`Core`、`Protocol`、`NativeApi` 不应反向引用 Server。

## 构建

```bash
dotnet build YGOProSharp.Server/YGOProSharp.Server.csproj -c Release
```
