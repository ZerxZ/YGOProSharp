# WindBot 适配说明

`YGOProSharp.WindBot` 是一个独立的协议 Bot 客户端库。它通过 `YGOProSharp.Protocol` 连接 YGOProSharp 服务端，发送和接收 CTOS/STOC 消息；它不是服务端内置 AI，也不引用 `YGOProSharp.Server` 或 `YGOProSharp.NativeApi`。

命令行启动统一由 `YGOProSharp.Cli` 负责，WindBot 项目本身不再生成 exe。

## 来源

- 原始项目：[IceYGO/windbot](https://github.com/IceYGO/windbot)（MIT License，当前已同步上游 `master` 至 `dec0535`，包含全部新卡组执行器、核心 AI 修复与已知卡枚举扩展）
- 早期导入基线：[sherry_chaos/MDPro3](https://code.moenext.com/sherry_chaos/MDPro3) 版本中的 WindBot 代码
- 本仓库负责 YGOProSharp 边界适配（协议、卡库、日志、WebSocket 与泛用 Lua 卡组执行器），不保证 AI 对局完整可玩

## 上游对比

`.external/windbot-upstream/` 是 `IceYGO/windbot` 的本地只读对比目录，通过 `.gitignore` 排除，不纳入提交。

当前项目只保守补齐能直接适配到 `YGOProSharp.Protocol`、`YGOProSharp.Core`、`YGOProSharp.Abstractions` 的缺失代码。旧 `YGOSharp.*`、`MDPro3`、旧工程文件、sqlite 二进制和 `.meta` 文件不会回流。

`Decks/` 和 `Dialogs/` 不复制进仓库。需要运行 Bot 时，请在运行目录或配置指定位置自行提供卡组与对话资源。

## 项目位置

- 项目文件：`YGOProSharp.WindBot/YGOProSharp.WindBot.csproj`
- 命名空间：继续保留 `WindBot.*`，减少对原始 Bot 逻辑的侵入式改动
- 引用边界：
  - `YGOProSharp.Abstractions`：OCG enum、全局日志入口
  - `YGOProSharp.Core`：卡片数据库、`NamedCard`、卡组相关能力
  - `YGOProSharp.Protocol`：网络连接、包读写、CTOS/STOC 协议类型

## 运行示例

普通客户端模式：

```powershell
dotnet run --project YGOProSharp.Cli/YGOProSharp.Cli.csproj -c Release -- windbot DbPath=cards.cdb Host=127.0.0.1 Port=7911 Deck=AI_Yubel LogLevel=Information
```

HTTP server mode：

```powershell
dotnet run --project YGOProSharp.Cli/YGOProSharp.Cli.csproj -c Release -- windbot ServerMode=true DbPath=cards.cdb ServerPort=2399 BotListPath=bots.json
```

常用参数：

- `Host` / `Port`：要连接的 YGOProSharp 服务端地址和端口；当 `Host` 以 `ws://` 或 `wss://` 开头时会自动切换为 WebSocket 传输。
- `UseWebSocket` / `WebSocketUseWss` / `WebSocketPath`：显式启用 WebSocket 并拼接 `ws(s)://<Host>:<Port><Path>`（ServerMode 查询参数对应 `ws`、`wss`、`wspath`）。
- `WebSocketFramingMode`：WebSocket 二进制分帧模式（ServerMode 查询参数 `wsframing`），可选 `LengthPrefixed`（默认，2 字节长度前缀流式封装，兼容 websockify / srvpro WS 网关）或 `MessagePerPacket`（单条 WS Binary 消息即单个 CTOS/STOC 包）。
- `HostInfo`：房间密码或主机信息。
- `Name`：Bot 昵称。
- `Deck` / `DeckFile`：Bot 使用的卡组。
- `Dialog`：Bot 使用的对话资源。
- `DbPath`：主 `cards.cdb` 路径。
- `DbPaths` / `Databases`：额外数据库路径，支持 `;` 或 `,` 分隔。
- `Locale`：额外尝试 `Data/locales/<Locale>/<DbPath>`。
- `ServerMode`：设为 `true` 时启动 HTTP host。
- `BotListPath`：server mode 可选 JSON Bot 列表路径。
- `LogLevel`：`Trace`、`Debug`、`Information`、`Warning`、`Error`、`Critical`。

## 卡片数据

旧 WindBot 里依赖 `YGOSharp.OCGWrapper.NamedCard.Get` 的路径已经改成 `CardDatabase` 适配层。

CLI 启动时使用 `SqliteCardDatabaseManager.LoadNamedCards(...)` 加载数据库，然后通过 `WindBotRuntimeOptions.CardRepository` 注入到 `WindBotService`。库层只接收 `INamedCardRepository`，不解析数据库路径。

边界如下：

- WindBot 只知道 `NamedCard` 和 `INamedCardRepository`。
- SQLite 读取由 `YGOProSharp.Core.Cards.SqliteCardDatabaseManager` 负责。
- WindBot 不再扫描 `MDPro3.Program.PATH_EXPANSIONS`，也不再依赖 `ZipHelper`。

## 日志

WindBot 保留原来的 `Logger.WriteLine`、`Logger.DebugWriteLine`、`Logger.WriteErrorLine` 调用入口，但内部统一转发到 `AppLog`。

全局 console logger 由 `YGOProSharp.Cli` 初始化；WindBot 库本身不引入 `Microsoft.Extensions.Logging.Console`，源码中也不直接使用 `Console.WriteLine` 输出日志。

## Lua 卡组执行器扩展

WindBot 支持通过泛用 Lua 接口（`YGOProSharp.Abstractions.Scripting`）扩展 Lua 卡组执行器（`WindBot.Game.AI.Lua.LuaDeckExecutor`），仓库本身不引入任何具体 Lua 引擎依赖，可按需接入 XLua、MoonSharp、NLua、Lua-CSharp 等库。

详细接入指南与 XLua 适配器示例见 [windbot-lua.md](windbot-lua.md)。

## C# 进程内直接调用（免 TCP / WS / HTTP）

除 CLI、TCP/WebSocket 与 HTTP ServerMode 外，C# 宿主可以直接在同一进程内通过 API 调用服务端与 WindBot：

1. **进程内无端口服务端 + 本地直连 WindBot**：
   - `YGOProSharpServerHost`（或 `CoreServer.StartEmbedded()`）可在 `EnableNetworkListener = false` 下运行，不占用任何 TCP 端口；
   - 通过 `serverHost.CreateLocalTransport()` / `serverHost.ConnectLocalClient()` 创建基于 `InMemoryNetworkTransport` 的纯内存双向通道；
   - 通过 `windBotService.StartBot(info, serverHost.CreateLocalTransport())` 获取 `WindBotSession` 句柄（支持 `Chat`、`Surrender`、`StopAsync`、`GetActiveSessions()`、`StopAllAsync()` 等控制能力，无需启动 HTTP ServerMode）。
2. **纯包级同步直调（`WindBotDirectClient`）**：
   - 适合 Unity、自定义服务端或离线评测场景：通过 `windBotService.CreateDirectClient(info)` 创建零后台线程客户端；
   - 直接调用 `FeedStocPacket(...)` 喂入 STOC 数据包，再通过 `CtosPacketProduced` 事件或 `DrainCtosPackets()` 同步取出 WindBot 产出的 CTOS 响应包。

## 当前限制

本轮适配目标是让 WindBot 作为独立客户端库纳入 solution，能够编译，并通过 YGOProSharp 的协议层发送基础 CTOS 响应。

AI 行为完整性、对局稳定性和实际可玩性仍不保证，这和项目本身的 AI code vibing 定位一致。
