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

启动服务端并默认内置添加一个进程内 WindBot（无需额外开端口或进程）：

```bash
dotnet run --project YGOProSharp.Cli/YGOProSharp.Cli.csproj -c Release -- server RootPath=. DatabaseFile=cards.cdb ScriptDirectory=script Port=7911 AddBot=true BotName=BlueEyesLua BotDeck=ExampleBlueEyes
```

启动 2v2 双打（Tag Duel）服务端（默认 4 席位、16000 LP，进入房间后可输入 `/ai fill ExampleBlueEyes` 一键召唤 3 名 AI 补满席位开局）：

```bash
dotnet run --project YGOProSharp.Cli/YGOProSharp.Cli.csproj -c Release -- server RootPath=. DatabaseFile=cards.cdb ScriptDirectory=script Port=7911 Mode=2v2
```

启动多房间服务端并同时开启三端口物理隔离的 **游戏服 (`7911`) + 后台管理面板 (`7912`) + 前台玩家用户面板 (`7913`)**（可选指定 `AdminUser` / `AdminPassword` 自定义管理员账号密码，密码最少 6 位字符；不填则默认账号 `admin` 并在首次建库时自动生成初始密码）：

```bash
dotnet run --project YGOProSharp.Cli/YGOProSharp.Cli.csproj -c Release -- server RootPath=. Port=7911 MultiRoom=true EnableWeb=true AdminPort=7912 PortalPort=7913 AdminUser=admin AdminPassword=123456
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
- 服务端：`RootPath`、`ScriptDirectory`、`DatabaseFile`、`BanlistFile`、`Port`、`ClientVersion`、`StandardStreamProtocol`、`MultiRoom`、`RandomDeck`、`SuddenDeath`、`AddBot`（或指定 `BotDeck`/`BotDeckFile` 自动启用）、`BotName`、`BotDeck`、`BotDeckFile`、`BotDialog`、`BotChat`、`BotDebug`
- 控制面、用户面板与持久化：`EnableWeb`（一键同时开启管理面板与用户面板）、`EnableAdmin`、`AdminHost`（默认 `127.0.0.1`）、`AdminPort`（默认 `7912`，兼容别名 `WebPort`）、`EnablePortal`、`PortalHost`（默认 `127.0.0.1`）、`PortalPort`（默认 `7913`）、`PublicGameHost`、`EnablePersistence`、`DbProvider`（`Sqlite` 或 `Postgres`）、`DbConnectionString`、`RedisUrl`、`ReplayDirectory`、`AdminUser`（可选自定义管理员用户名，不填默认 `admin`，兼容 `BootstrapAdminUser`）、`AdminPassword`（可选自定义管理员密码，最少 6 位字符，不填首次建库自动生成随机密码，兼容 `BootstrapAdminPassword`）
- WindBot：`DbPath`、`DbPaths`、`Databases`、`Host`、`Port`、`Deck`、`DeckFile`、`Dialog`、`ServerMode`、`BotListPath`

## 边界

CLI 不承载房间状态、协议解析、卡组校验、native API 封装或 Bot AI 行为。这些能力分别属于 `Server`、`Protocol`、`Core`、`NativeApi` 和 `WindBot`。
