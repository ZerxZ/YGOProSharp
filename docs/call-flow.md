# YGOProSharp 调用流程

本文描述当前代码的主要运行路径。它只说明调用关系，不表示项目已经具备完整可玩能力。

## 启动流程

```mermaid
flowchart TD
    CLI[YGOProSharp.Cli] --> Log[AppLog.Configure]
    CLI --> Runtime[NativeOcgRuntime]
    CLI --> Entry[YGOProSharpServer.RunAsync]
    Entry --> Config[Config.Load]
    Entry --> Cards[SqliteCardDatabaseManager.LoadCards]
    Entry --> Banlist[BanlistManager.Init]
    Entry --> Providers[RepositoryCardDataProvider + FileScriptProvider]
    Providers --> Init[NativeOcgRuntime.Initialize]
    Init --> CoreServer[CoreServer]
    CoreServer --> Listen[NetworkServer.Start]
    CoreServer --> Tick[Tick loop]
```

CLI 是组合根：它配置全局日志，创建 native runtime，然后把控制权交给 `YGOProSharp.Server`。Server 入口读取配置、加载卡库和禁限表，创建 native callback provider，并进入轻量 tick loop。

## 网络到玩家动作

```mermaid
sequenceDiagram
    participant Socket
    participant NetworkClient
    participant BinaryClient
    participant YGOClient
    participant Player
    participant Game

    Socket->>NetworkClient: receive bytes
    NetworkClient->>BinaryClient: PacketReceivedRaw
    BinaryClient->>YGOClient: framed packet
    YGOClient->>Player: Player.Parse(packet)
    Player->>Player: read CTOS message
    Player->>Game: call server action
    Game->>YGOClient: send STOC packet
```

`YGOProSharp.Protocol` 只负责 socket 与 YGO packet。`Player.Parse` 是 CTOS 业务入口：认证前只处理 `PlayerInfo`、`JoinGame`、`CreateGame`；认证后才处理聊天、移动座位、准备、更新卡组、response、投降等动作。

## 对局与 native 调用

```mermaid
sequenceDiagram
    participant Player
    participant Game
    participant Factory as IDuelFactory
    participant Session as IDuelSession
    participant NativeApi as NativeDuelSession
    participant Core as ocgcore
    participant Analyser as GameAnalyser

    Player->>Game: StartDuel / HandResult / TpResult
    Game->>Factory: Create(seed[8])
    Factory->>NativeApi: create_duel_v2
    Game->>Session: SetPlayerInfo
    Game->>Session: AddCard / AddTagCard
    Game->>Session: Start
    loop Game.Process
        Game->>Session: Process
        Session->>Core: process + get_message
        Session->>Analyser: Analyse(GameMessage)
        Analyser->>Game: SendToAll / WaitForResponse / End
        Player->>Game: Response
        Game->>Session: SetResponse
    end
```

`Game` 负责选择随机种子、创建 `IDuelSession`、设置玩家信息、把双方卡组喂给 native，然后启动 duel。默认路径使用 `create_duel_v2`，由 `NativeDuelFactory.Create(ReadOnlySpan<uint>)` 封装。

## 卡片数据与脚本 callback

```mermaid
flowchart LR
    CDB[cards.cdb] --> DB[SqliteCardDatabaseManager]
    DB --> Repo[InMemoryCardRepository]
    Repo --> Deck[Deck + DeckRules]
    Repo --> Provider[RepositoryCardDataProvider]
    Provider --> DTO[OcgCardData]
    DTO --> Native[NativeOcgRuntime card callback]

    ScriptDir[script/] --> ScriptProvider[FileScriptProvider]
    ScriptProvider --> Runtime[NativeOcgRuntime script callback]
```

SQLite 只在 Core 的 `SqliteCardDatabaseManager` 内使用。加载完成后，卡片数据以 repository 形式提供给 `Deck` 和 native card callback。脚本由 `FileScriptProvider` 读取，再由 `NativeOcgRuntime` 复制到 native callback buffer。

## 错误与日志

```mermaid
flowchart TD
    CLI[CLI] --> AppLog[AppLog.Configure]
    AppLog --> Logs[Core / Protocol / Server logs]
    Native[ocgcore log callback] --> Session[NativeDuelSession.OnMessage]
    Session --> GameError[Game.HandleError]
    GameError --> File[lua_*.txt]
    GameError --> Broadcast[游戏内错误广播]
    GameError --> Logger[LogError]
```

日志入口是 `AppLog`。CLI 配置 console logger；测试可以配置捕获 provider。native / Lua 错误通过 `NativeDuelSession.OnMessage` 进入 `Game.HandleError`，当前行为会写入 `lua_*.txt`、向游戏内广播错误，并记录 `LogError`。
