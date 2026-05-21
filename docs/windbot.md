# WindBot 适配说明

## 上游对比

本轮使用 `.external/windbot-upstream/` 作为 `IceYGO/windbot` 的本地只读对比目录，该目录通过 `.gitignore` 排除，不纳入提交。当前项目只保守补齐能直接适配到 `YGOProSharp.Protocol`、`YGOProSharp.Core`、`YGOProSharp.Abstractions` 的缺失代码。

`Decks/` 和 `Dialogs/` 不复制进仓库；需要运行 Bot 时，请在运行目录或配置指定位置自行提供卡组与对话资源。旧 `YGOSharp.*`、`MDPro3`、旧工程文件、sqlite 二进制和 `.meta` 文件不会回流。

`YGOProSharp.WindBot` 是一个独立的协议 Bot 客户端。它通过 `YGOProSharp.Protocol` 连接 `YGOProSharp.Server`，发送和接收 CTOS/STOC 消息；它不是服务端内置 AI，也不引用 `YGOProSharp.Server` 或 `YGOProSharp.NativeApi`。

## 来源

- 原始项目：[`IceYGO/windbot`](https://github.com/IceYGO/windbot)。
- 当前导入代码基于：[`sherry_chaos/MDPro3`](https://code.moenext.com/sherry_chaos/MDPro3) 版本中的 WindBot 代码。
- 本仓库只做 YGOProSharp 适配：替换协议、卡库和日志边界，不保证 AI 对局完整可玩。

## 项目位置

- 项目文件：`YGOProSharp.WindBot/YGOProSharp.WindBot.csproj`
- 命名空间：继续保留 `WindBot.*`，减少对原 WindBot 源码的侵入式改动。
- 引用边界：
  - `YGOProSharp.Abstractions`：OCG enum、全局日志入口。
  - `YGOProSharp.Core`：卡片数据库、`NamedCard`、卡组相关能力。
  - `YGOProSharp.Protocol`：网络连接、包读写、CTOS/STOC 协议类型。

## 运行示例

```powershell
dotnet run --project YGOProSharp.WindBot/YGOProSharp.WindBot.csproj -c Release -- DbPath=cards.cdb Deck=AI_Yubel LogLevel=Information
```

常用配置仍沿用 WindBot 的参数模型，例如 `Name`、`Host`、`Port`、`HostInfo`、`Version`、`Deck`、`DeckFile`、`Dialog`、`Hand`、`ServerMode`、`ServerPort`。适配后新增或重点使用的数据库参数如下：

- `DbPath`：主卡片数据库路径。可以是绝对路径，也可以是当前目录、`Data/`、`cdb/`、`../cdb/` 下的相对路径。
- `DbPaths` / `Databases`：额外数据库路径列表，支持用 `;` 或 `,` 分隔。
- `Locale`：如果设置，会额外尝试 `Data/locales/<Locale>/<DbPath>`。
- `LogLevel`：全局控制台日志级别，支持 `Trace`、`Debug`、`Information`、`Warning`、`Error`、`Critical`。

## 卡片数据

旧 WindBot 里依赖 `YGOSharp.OCGWrapper.NamedCard.Get` 的路径已经改成 `CardDatabase` 适配层。启动时 `Program.InitDatas` 使用 `SqliteCardDatabaseManager.LoadNamedCards(...)` 加载数据库，然后将 `INamedCardRepository` 注入到 WindBot 的卡组读取和行为逻辑。

这样做的边界是：

- WindBot 只知道 `NamedCard` 和 `INamedCardRepository`。
- SQLite 读取仍由 `YGOProSharp.Core.Cards.SqliteCardDatabaseManager` 负责。
- WindBot 不再扫描 `MDPro3.Program.PATH_EXPANSIONS`，也不再依赖 `ZipHelper`。

## 日志

WindBot 保留原来的 `Logger.WriteLine`、`Logger.DebugWriteLine`、`Logger.WriteErrorLine` 调用入口，但内部统一转发到 `AppLog`。CLI 启动时配置一次全局 console logger；源码中不直接使用 `Console.WriteLine` 输出日志。

## 当前限制

本轮适配目标是让 WindBot 作为独立客户端项目纳入 solution，能够编译，并通过 YGOProSharp 的协议层发送基础 CTOS 响应。AI 行为完整性、对局稳定性和实际可玩性仍不保证，这和项目本身的 AI code vibing 定位一致。
