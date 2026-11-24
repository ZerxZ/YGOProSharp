# YGOProSharp.WindBot

`YGOProSharp.WindBot` 是 YGOProSharp 的独立协议 Bot 客户端库。它通过 `YGOProSharp.Protocol` 连接服务端，复用 `YGOProSharp.Core` 的卡片数据库能力，不嵌入服务端状态机，也不引用 `YGOProSharp.Server` 或 `YGOProSharp.NativeApi`。

本项目不再生成可执行文件；命令行启动统一由 `YGOProSharp.Cli` 负责。

## 来源

- 原始项目：[IceYGO/windbot](https://github.com/IceYGO/windbot)
- 当前导入代码基于：[sherry_chaos/MDPro3](https://code.moenext.com/sherry_chaos/MDPro3) 版本中的 WindBot 代码

本目录保留 `WindBot.*` 命名空间，减少对原始 Bot 逻辑的侵入式改动。适配重点是替换旧 `YGOSharp` / `MDPro3` 依赖，改用 YGOProSharp 的协议、卡库和日志边界。

## 上游对比

`IceYGO/windbot` 已 clone 到本地忽略目录 `.external/windbot-upstream/` 作为只读对比源。当前适配只保守补齐可直接接入 YGOProSharp 边界的缺失代码。

`Decks/`（71 个 `.ydk` + `Decks/Lua/` Lua 卡组脚本）与 `Dialogs/`（20 个 `.json` + `Dialogs/Lua/` Lua 对话脚本）统一集中在 `YGOProSharp.WindBot` 内，并在构建时随 `CopyToOutputDirectory=PreserveNewest` 自动输出到运行目录；开发者 LuaLS / EmmyLua 智能提示位于 [lua-api/](lua-api/README.md)。

对比时会跳过旧 `YGOSharp.*`、旧工程文件、旧 sqlite 二进制和 `.meta` 文件，避免把上游旧依赖重新带回当前 `Core` / `Protocol` / `Abstractions` 分层。

## 通过 CLI 运行

```powershell
dotnet run --project YGOProSharp.Cli/YGOProSharp.Cli.csproj -c Release -- windbot DbPath=cards.cdb Host=127.0.0.1 Port=7911 Deck=AI_Yubel LogLevel=Information
```

HTTP server mode：

```powershell
dotnet run --project YGOProSharp.Cli/YGOProSharp.Cli.csproj -c Release -- windbot ServerMode=true DbPath=cards.cdb ServerPort=2399 BotListPath=bots.json
```

常用参数：

- `Host` / `Port`：要连接的 YGOProSharp 服务端地址和端口；`Host` 以 `ws://` 或 `wss://` 开头时自动使用 WebSocket。
- `UseWebSocket` / `WebSocketUseWss` / `WebSocketPath` / `WebSocketFramingMode`：WebSocket 传输选项，支持 `LengthPrefixed`（默认，2 字节长度头）与 `MessagePerPacket`（单帧即单包）。
- `HostInfo`：房间密码或主机信息。
- `Name`：Bot 昵称。
- `Deck` / `DeckFile`：Bot 使用的卡组。
- `Dialog`：Bot 使用的对话资源（支持内置 `Dialogs/*.json` 以及 xmake 风格 / XLua 兼容的 Lua 对话脚本，详见 [docs/windbot-lua.md](../docs/windbot-lua.md)）。
- `LuaDialogDir` / `LuaDialogDirs` / `PreferLuaDialogs` / `EnableLuaDialogs`：Lua 对话目录与优先级开关。
- `ChatIntervalMs` / `BotChatIntervalMs` / `DuelStartChatDelayMs` / `MaxQueuedChatMessages`：Bot 聊天节流间隔（默认 800ms）、开局切场延迟（默认 4000ms）与待发队列上限（默认 6 条）。
- `DbPath`：主 `cards.cdb` 路径。
- `DbPaths` / `Databases`：额外数据库路径，支持 `;` 或 `,` 分隔。
- `Locale` / `BotLocale`：本地化区域标识（如 `zh-CN`、`zh-TW`、`en-US`），同时用于 `Data/locales/<Locale>/<DbPath>` 与 Dialog 本地化自动解析（`<name>.<locale>.json` / `<name>.<locale>.lua` / `locale(...)` 条件块）。
- `LogLevel`：`Trace`、`Debug`、`Information`、`Warning`、`Error`、`Critical`，默认 `Information`。

## 库式调用（C# 直接调用）

### 1. 多 Bot 会话托管（替代 HTTP ServerMode）

```csharp
SqliteCardDatabaseManager databaseManager = new();
INamedCardRepository cards = databaseManager.LoadNamedCards(["cards.cdb"]);

await using WindBotService service = new(new WindBotRuntimeOptions
{
    CardRepository = cards,
    TickDelayMilliseconds = 30
});

// 支持直接走 TCP / WebSocket，或传入 InMemoryNetworkTransport（如 serverHost.CreateLocalTransport()）
using WindBotSession session = service.StartBot(new WindBotInfo
{
    Name = "WindBot",
    Host = "127.0.0.1",
    Port = 7911,
    Deck = "AI_Yubel"
});

session.Chat("你好！");
await session.StopAsync();
```

### 2. 纯包级直调（零网络、零后台线程）

```csharp
using WindBotDirectClient direct = service.CreateDirectClient(new WindBotInfo
{
    Name = "DirectBot",
    Deck = "AI_Yubel"
});

direct.CtosPacketProduced += packet => { /* 转发或消费 CTOS 包 */ };
direct.Start(pumpInitialHandshake: true);
direct.FeedStocPacket(StocMessage.SelectHand);
IReadOnlyList<byte[]> replies = direct.DrainCtosPackets();
```

## 边界

- 允许引用：`YGOProSharp.Abstractions`、`YGOProSharp.Core`、`YGOProSharp.Protocol`。
- 不允许引用：`YGOProSharp.Server`、`YGOProSharp.NativeApi` 或任何具体 Lua 引擎库（Lua 卡组执行器仅依赖 `YGOProSharp.Abstractions.Scripting` 泛用契约，详见 [docs/windbot-lua.md](../docs/windbot-lua.md)）。
- SQLite 读取由 `SqliteCardDatabaseManager` 完成，WindBot 只通过 `INamedCardRepository` 查询卡片。
- 日志通过 `AppLog` 输出，不直接调用 `Console.WriteLine`。

## 当前限制

当前目标是能编译、能作为外部协议客户端连接 YGOProSharp，并能发送基础 CTOS 响应。AI 行为完整性、对局稳定性和实际可玩性不保证。
