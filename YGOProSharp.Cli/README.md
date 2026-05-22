# YGOProSharp.Cli

`YGOProSharp.Cli` 是唯一命令行 host，也是应用组合根。

## 职责

- 解析子命令和 `Key=Value` 参数。
- 读取 `Config=...` 配置文件，并与命令行参数合并。
- 配置全局日志 `AppLog`。
- 创建 `NativeOcgRuntime`。
- 将命令行配置转换成 typed options。
- 调用 `YGOProSharp.Server` 或 `YGOProSharp.WindBot` 的库式 API。
- 保证 `YGOProSharp.Native` runtime assets 随输出复制。

## 子命令

启动服务端：

```bash
dotnet run --project YGOProSharp.Cli/YGOProSharp.Cli.csproj -c Release -- server RootPath=. DatabaseFile=cards.cdb ScriptDirectory=script Port=7911
```

启动 WindBot 客户端：

```bash
dotnet run --project YGOProSharp.Cli/YGOProSharp.Cli.csproj -c Release -- windbot DbPath=cards.cdb Host=127.0.0.1 Port=7911 Deck=AI_Yubel
```

启动 WindBot HTTP server mode：

```bash
dotnet run --project YGOProSharp.Cli/YGOProSharp.Cli.csproj -c Release -- windbot ServerMode=true DbPath=cards.cdb ServerPort=2399 BotListPath=bots.json
```

无子命令时 CLI 会输出简短错误并返回非零退出码，不再隐式启动服务端。

## 常用参数

- `LogLevel=Trace|Debug|Information|Warning|Error|Critical`
- `Config=server.conf`
- 服务端：`RootPath`、`ScriptDirectory`、`DatabaseFile`、`BanlistFile`、`Port`、`ClientVersion`、`StandardStreamProtocol`
- WindBot：`DbPath`、`DbPaths`、`Databases`、`Host`、`Port`、`Deck`、`DeckFile`、`Dialog`、`ServerMode`、`BotListPath`

## 边界

CLI 不承载房间状态、协议解析、卡组校验、native API 封装或 Bot AI 行为。这些能力分别属于 `Server`、`Protocol`、`Core`、`NativeApi` 和 `WindBot`。
