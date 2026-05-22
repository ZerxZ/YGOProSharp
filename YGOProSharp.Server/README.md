# YGOProSharp.Server

`YGOProSharp.Server` 保存服务端运行状态机。它组合 `Core`、`Protocol` 和 `NativeApi`，但不承担命令行参数解析，也不直接处理 console host 生命周期。

## 职责

- 服务端库入口：`YGOProSharpServer.RunAsync(ServerOptions, IOcgRuntime, CancellationToken)`。
- 服务监听与客户端集合：`CoreServer`。
- 玩家消息解析与状态迁移：`Player`。
- 房间与 duel 生命周期：`Game`。
- OCG message 转 STOC / response：`GameAnalyser`。
- Replay、Addon 和 `StandardStreamProtocol`。

## Typed Options

Server 通过 `ServerOptions` 接收配置：

- 路径：`RootPath`、`ScriptDirectory`、`DatabaseFile`、`BanlistFile`
- 网络：`Port`、`ClientVersion`
- addon：`StandardStreamProtocol`
- 对局：`GameOptions`
- 卡组规则：`DeckRules`

`Key=Value`、`Config=...` 和子命令解析属于 `YGOProSharp.Cli`。Server 项目内不保留 `Config.Load(args)` 这类 CLI 职责。

## 边界

Server 可以引用 `Core`、`Protocol`、`NativeApi`。`Core`、`Protocol`、`NativeApi` 不应反向引用 Server。

Server 负责组合运行策略，例如卡库加载、脚本 provider、native runtime 初始化、玩家状态和房间状态；具体命令行 host、日志 provider 和进程退出策略由 CLI 负责。

## 构建

```bash
dotnet build YGOProSharp.Server/YGOProSharp.Server.csproj -c Release
```
