# YGOProSharp 架构说明

YGOProSharp 当前按 `Core / Protocol / Server` 拆分。拆分目标不是改变运行行为，而是让核心能力、客户端协议、服务端状态机、native runtime 和 Bot 客户端拥有清晰的项目边界。

## 分层

```text
YGOProSharp.Abstractions  -> OCG 契约、DTO、provider 接口、AppLog
YGOProSharp.Native        -> ocgcore runtime 二进制
YGOProSharp.NativeApi     -> ocgapi.h 的托管封装
YGOProSharp.Core          -> 卡片、卡组、禁限表、脚本 provider、OCG message reader
YGOProSharp.Protocol      -> CTOS/STOC、packet framing、socket client/server
YGOProSharp.Server        -> 服务端 typed options、房间、玩家、Game、Replay、Addon
YGOProSharp.Cli           -> 唯一命令行入口和组合根
YGOProSharp.WindBot       -> 外部协议 Bot 客户端库
YGOProSharp.Tests         -> 行为测试与架构边界测试
```

依赖方向固定为：

```text
Cli -> Server -> Core
              -> Protocol
              -> NativeApi -> Native

Cli -> WindBot -> Core
               -> Protocol
               -> Abstractions

Core -> Abstractions
Protocol -> Abstractions
NativeApi -> Abstractions + Native
```

`Core` 不知道服务端和客户端协议；`Protocol` 不知道卡库、房间和 native duel；`Server` 负责把这些部分组合起来；`Cli` 负责命令行 host 和配置解析。

## Native Interop 边界

主流程不直接调用 `ocgapi.h`。native 调用链路固定为：

```text
Server/Game -> IDuelFactory / IDuelSession -> YGOProSharp.NativeApi -> ocgcore
```

`YGOProSharp.NativeApi` 集中处理 native handle、buffer size、callback pinning 和错误转换。Server 只关心“创建 duel、喂卡、启动、处理消息、提交响应、查询场面”等托管动作。

## Core 边界

`YGOProSharp.Core` 保留可复用领域能力：

- `Card` / `NamedCard`
- `ICardRepository` / `INamedCardRepository`
- `SqliteCardDatabaseManager`
- `RepositoryCardDataProvider`
- `Deck` / `DeckRules`
- `Banlist` / `BanlistManager`
- `CoreMessage`
- `FileScriptProvider`

SQLite 只允许集中在数据库 manager 实现里。`Deck` 通过 `ICardRepository` 查询卡片，通过 `DeckRules` 接收大小限制，不读取 Server 配置。

## Protocol 边界

这里的“前端”指 YGO 客户端协议层，不是 UI 项目。`YGOProSharp.Protocol` 包含：

- socket 收发：`NetworkClient`、`NetworkServer`
- packet 边界：`PacketFramer`
- 小端读取：`PacketReader`
- YGO client 包装：`BinaryClient`、`YGOClient`
- 协议枚举：`CtosMessage`、`StocMessage`、`PlayerType`、`GameState`
- 协议辅助：`BinaryExtensions`、`ClientCard`

Protocol 不引用 Core、Server、NativeApi 或 SQLite。它只把 socket bytes 变成 YGO 协议包，或把 STOC/CTOS 类型写成网络 payload。

## Server 边界

`YGOProSharp.Server` 持有服务端状态机：

- `ServerOptions` / `GameOptions`：typed 配置对象。
- `YGOProSharpServer`：加载卡库、禁限表，初始化 native runtime，运行 loop。
- `CoreServer`：管理监听器、客户端集合和当前 `Game`。
- `Player`：解析 CTOS，并把客户端动作转成 `Game` 调用。
- `Game`：管理房间、玩家状态、duel 生命周期、广播和超时。
- `GameAnalyser`：把 OCG message 转换成 STOC 包或 response 请求。
- `Replay`、`AddonsManager`、`StandardStreamProtocol`：服务端运行能力。

Server 可以引用 Core、Protocol 和 NativeApi，但这些项目不能反向引用 Server。Server 不解析 CLI args，也不读取 `Config=...`；这些职责属于 CLI。

## CLI 边界

`YGOProSharp.Cli` 是唯一命令行入口：

- `server Key=Value ...`：构造 `ServerOptions`，创建 `NativeOcgRuntime`，调用 Server typed API。
- `windbot Key=Value ...`：加载 named card repository，构造 `WindBotInfo` / `WindBotRuntimeOptions`，调用 WindBot typed API。

`LogLevel`、`Console.CancelKeyPress`、crash file、配置文件读取、默认 console logger 都集中在 CLI 项目。

## Logging 边界

日志入口是 `YGOProSharp.Abstractions.Logging.AppLog`。CLI 或测试在启动时调用 `AppLog.Configure`；库代码通过 `AppLog.CreateLogger<T>()` 或 `AppLog.CreateLogger(string)` 获取 logger。

未配置时默认使用 `NullLoggerFactory.Instance`，所以 Core、Protocol、Server、NativeApi 和 WindBot 都可以独立构造对象而不崩溃。

## WindBot 边界

`YGOProSharp.WindBot` 是独立客户端库，不嵌入 Server。它复用 Core 的卡库能力和 Protocol 的网络协议能力，保留 WindBot 自己的客户端状态模型和 AI executor。

WindBot 不引用 `YGOProSharp.Server` 或 `YGOProSharp.NativeApi`。当前目标是适配编译和协议连接，不保证 AI 对局完整可玩。

## 参考方向

服务端、协议、WindBot、数据编码和线程模型的外部参考集中记录在 [参考项目索引](reference-projects.md)。这些项目用于阅读和对照，不改变当前 `Core / Protocol / Server / NativeApi / WindBot` 的依赖边界。
