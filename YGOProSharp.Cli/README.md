# YGOProSharp.Cli

`YGOProSharp.Cli` 是命令行 host，也是应用组合根。

## 职责

- 解析启动参数。
- 配置全局日志 `AppLog`。
- 创建 `NativeOcgRuntime`。
- 调用 `YGOProSharpServer.RunAsync` 启动服务端。
- 保证 `YGOProSharp.Native` runtime assets 随输出复制。

## 边界

CLI 不承载房间状态、协议解析或卡组校验逻辑。这些能力分别属于 Server、Protocol 和 Core。

## 运行

```bash
dotnet run --project YGOProSharp.Cli/YGOProSharp.Cli.csproj -c Release -- LogLevel=Information
```

常用参数：

- `LogLevel=Trace|Debug|Information|Warning|Error|Critical`
- `StandardStreamProtocol=true`
