# YGOProSharp.Server

`YGOProSharp.Server` 保存服务端运行状态机。它组合 Core、Protocol 和 NativeApi，但不把这些底层细节暴露给调用方。

## 职责

- 启动入口：`YGOProSharpServer`。
- 监听与客户端集合：`CoreServer`。
- 玩家消息解析：`Player`。
- 房间和 duel 生命周期：`Game`。
- OCG message 转 STOC / response：`GameAnalyser`。
- Replay、Addon 和 StandardStreamProtocol。

## 边界

Server 可以引用 Core、Protocol、NativeApi。Core、Protocol、NativeApi 不应反向引用 Server。

Server 负责组合运行策略，例如配置读取、卡库加载、脚本 provider、native runtime 初始化、玩家状态和房间状态。

## 常用命令

```bash
dotnet build YGOProSharp.Server/YGOProSharp.Server.csproj -c Release
```
