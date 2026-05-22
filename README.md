# YGOProSharp

YGOProSharp 是一个实验性的 C# / .NET 项目，目标是把 YGOPro / OCGCore 相关能力整理成更清晰的工程结构。

这个仓库主要用于 **AI code vibing / AI 编程体验**：用 AI 辅助阅读、迁移、封装、重构和补全文档，观察 AI 参与工程化改造的过程。它不是稳定发行版，不保证能联机、开局或完整游玩。

## 来源与参考

本项目使用或参考了以下项目：

- [Fluorohydride/ygopro-core](https://github.com/Fluorohydride/ygopro-core)：提供 `ocgcore` 原生决斗核心。
- [IceYGO/ygosharp](https://github.com/IceYGO/ygosharp)：提供 C# 服务端结构、协议与实现思路参考。
- [IceYGO/windbot](https://github.com/IceYGO/windbot)：提供 WindBot 客户端 Bot 逻辑参考。
- [sherry_chaos/MDPro3](https://code.moenext.com/sherry_chaos/MDPro3)：当前导入的 WindBot 代码来源版本。

更多参考项目见 [参考项目索引](docs/reference-projects.md)，本轮对照实现记录见 [参考项目审计](docs/reference-audit.md)。

## 当前状态

- 不保证能玩。
- 不保证协议、卡片数据库、脚本和客户端版本兼容。
- 不建议直接用于正式服务器。
- 当前重点是验证 `NativeApi`、核心库、协议层、服务端层和 Bot 客户端的解耦方式。

## 项目结构

```text
YGOProSharp/
├── YGOProSharp.Abstractions/  # OCG 契约、DTO、provider 接口、全局日志入口
├── YGOProSharp.Native/        # ocgcore runtime 二进制与 xmake 构建
├── YGOProSharp.NativeApi/     # ocgapi.h 的安全托管封装
├── YGOProSharp.Core/          # 卡片、卡组、禁限表、脚本 provider、OCG message reader
├── YGOProSharp.Protocol/      # YGO 客户端协议、CTOS/STOC、packet framing、socket client/server
├── YGOProSharp.Server/        # 服务端配置、房间、玩家、Game 状态机、Replay、Addon
├── YGOProSharp.Cli/           # 命令行 host 和组合根
├── YGOProSharp.WindBot/       # 独立协议 Bot 客户端适配
├── YGOProSharp.Tests/         # 行为测试与架构边界测试
└── docs/                      # 架构、流程、迁移、参考项目和项目思路文档
```

关键边界：

- `Core` 不引用 `Protocol`、`Server`、`NativeApi` 或 `SevenZip`。
- `Protocol` 不引用 `Core`、`Server`、`NativeApi` 或 `SQLite`。
- `Server` 组合 `Core`、`Protocol`、`NativeApi`，保留服务端状态机行为。
- `NativeApi` 是唯一直接接触 `ocgapi.h` raw binding、native handle 和 unsafe buffer 的项目。
- `WindBot` 是外部协议 Bot 客户端，不引用 `Server` / `NativeApi`。

## 快速开始

构建：

```bash
dotnet build YGOProSharp.slnx -c Release
```

运行测试：

```bash
dotnet run --project YGOProSharp.Tests/YGOProSharp.Tests.csproj -c Release
```

运行 CLI：

```bash
dotnet run --project YGOProSharp.Cli/YGOProSharp.Cli.csproj -c Release
```

运行时通常还需要 YGOPro 数据文件：

- `cards.cdb`
- `script/`
- `lflist.conf`

## 日志

CLI 默认使用结构化单行 console 日志，默认级别是 `Information`。

```bash
dotnet run --project YGOProSharp.Cli -c Release -- LogLevel=Debug
dotnet run --project YGOProSharp.Cli -c Release -- LogLevel=Information StandardStreamProtocol=true
```

`LogLevel` 支持 `Trace`、`Debug`、`Information`、`Warning`、`Error`、`Critical`。生命周期事件在 `Information` 输出，包级和消息级细节在 `Debug` 输出，payload 短预览只在 `Trace` 输出。

## WindBot

`YGOProSharp.WindBot` 是独立协议 Bot 客户端，通过 `YGOProSharp.Protocol` 连接服务端。它不是服务端内置 AI，不嵌入房间状态机，也不引用 `YGOProSharp.Server` / `YGOProSharp.NativeApi`。

当前适配目标是能编译、能按协议连接 YGOProSharp、能发送基础 CTOS 响应，不保证 AI 对局完整可玩。`Decks/` 和 `Dialogs/` 不纳入仓库；运行 Bot 时请自行提供卡组和对话资源。

更多说明见 [YGOProSharp.WindBot/README.md](YGOProSharp.WindBot/README.md) 和 [docs/windbot.md](docs/windbot.md)。

## 文档索引

- [项目思路](docs/project-thinking.md)
- [架构说明](docs/architecture.md)
- [调用流程](docs/call-flow.md)
- [服务端拆分说明](docs/server-migration.md)
- [WindBot 适配说明](docs/windbot.md)
- [开发代理规范](AGENTS.md)
- [参考项目索引](docs/reference-projects.md)
- [参考项目审计](docs/reference-audit.md)
- [NativeApi 文档](YGOProSharp.NativeApi/README.md)
- [Native runtime 文档](YGOProSharp.Native/README.md)

## License

本仓库以 MIT License 发布。第三方来源与 notice 见 [LICENSE](LICENSE)。
