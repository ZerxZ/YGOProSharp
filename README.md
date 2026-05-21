# YGOProSharp

YGOProSharp 是一个实验性的 C# / .NET 项目，目标是把 YGOPro / OCGCore 相关能力整理成更清晰的工程结构。

这个仓库主要用于 **AI code vibing / AI 编程体验**：用 AI 辅助阅读、迁移、封装、重构和补全文档，观察 AI 参与工程化改造的过程。它不是稳定发行版，不保证能联机、开局或完整游玩。

## 来源与参考

本项目使用或参考了以下项目：

- [Fluorohydride/ygopro-core](https://github.com/Fluorohydride/ygopro-core)：提供 `ocgcore` 原生决斗核心。
- [IceYGO/ygosharp](https://github.com/IceYGO/ygosharp)：提供 C# 服务端结构、协议与实现思路参考。

感谢这些项目和社区的长期积累。本仓库只是基于它们做 AI 辅助实验，不代表上游项目状态，也不保证兼容性。

## 项目状态

- 不保证能玩。
- 不保证协议、卡片数据库、脚本和客户端版本兼容。
- 不建议直接用于正式服务器。
- 当前重点是验证 native interop、核心库、协议层和服务器层的解耦方式。

## 快速阅读路线

- [架构说明](docs/architecture.md)：Core / Protocol / Server / Native 分层与边界。
- [调用流程](docs/call-flow.md)：CLI 启动、网络包、duel、卡库 callback 和错误日志流程。
- [服务器迁移说明](docs/server-migration.md)：本轮把服务器和“前端协议层”抽成新项目的迁移清单。
- [NativeApi 文档](YGOProSharp.NativeApi/README.md)：`ocgapi.h` 到托管 API 的映射。
- [Native runtime 文档](YGOProSharp.Native/README.md)：`ocgcore` runtime assets、RID 和 xmake 构建说明。

## 项目结构

```text
YGOProSharp/
├── YGOProSharp.Abstractions/  # OCG 契约、DTO、provider 接口，以及全局日志入口
├── YGOProSharp.Native/        # ocgcore runtime 二进制与 xmake 构建，不提供托管 API
├── YGOProSharp.NativeApi/     # ocgapi.h 的安全托管封装
├── YGOProSharp.Core/          # 卡片、卡组、禁限表、脚本 provider、OCG message reader
├── YGOProSharp.Protocol/      # YGO 客户端协议、CTOS/STOC、packet framing、socket client/server
├── YGOProSharp.Server/        # 服务端启动流程、房间、玩家、Game 状态机、Replay、Addon
├── YGOProSharp.Cli/           # 组合根和命令行入口
├── YGOProSharp.Tests/         # 行为测试与架构边界测试
└── docs/                      # 架构说明和调用流程文档
```

关键边界：

- `YGOProSharp.Core` 不引用 Protocol、Server、NativeApi 或 SevenZip。
- `YGOProSharp.Protocol` 不引用 Core、Server、NativeApi 或 SQLite。
- `YGOProSharp.Server` 组合 Core、Protocol、NativeApi，并保留原来的服务端状态机行为。
- `YGOProSharp.NativeApi` 是唯一直接接触 `ocgapi.h` raw binding、native handle 和 unsafe buffer 的项目。
- `YGOProSharp.Cli` 只负责创建 `NativeOcgRuntime`、配置全局日志并启动 Server。

## Native API

托管 native 边界基于 `YGOProSharp.Native/ygopro-core/ocgapi.h`。

`YGOProSharp.NativeApi` 默认使用 `create_duel_v2(uint[8])` 创建决斗；`create_duel(uint)` 仅作为 legacy / replay 路径保留在 wrapper 中。业务层应通过 `YGOProSharp.Abstractions.Ocg` 中的接口依赖 native 能力：

- `IOcgRuntime`
- `IDuelFactory`
- `IDuelSession`
- `ICardDataProvider`
- `IScriptProvider`

## 构建

需要安装 .NET SDK。若要重新构建原生 `ocgcore`，还需要 xmake 和对应平台的 C/C++ 工具链。

```bash
dotnet build YGOProSharp.slnx -c Release
```

运行测试：

```bash
dotnet run --project YGOProSharp.Tests -c Release
```

运行 CLI：

```bash
dotnet run --project YGOProSharp.Cli -c Release
```

运行时通常还需要 YGOPro 数据文件。默认从当前目录读取：

- `cards.cdb`
- `script/`
- `lflist.conf`

## Logging

```bash
dotnet run --project YGOProSharp.Cli -c Release -- LogLevel=Debug
dotnet run --project YGOProSharp.Cli -c Release -- LogLevel=Information StandardStreamProtocol=true
```

`LogLevel` 支持 `Trace`、`Debug`、`Information`、`Warning`、`Error`、`Critical`。CLI 默认使用 `Information` 等级的结构化单行 console 日志。

生命周期事件在 `Information` 输出；包级和消息级细节在 `Debug` 输出；payload 短预览只在 `Trace` 输出。

## Native Runtime

`YGOProSharp.Native` 按标准 .NET runtime asset 布局提供原生库：

```text
runtimes/<rid>/native/
```

支持的 RID 包括：

- `win-x64`
- `win-arm64`
- `linux-x64`
- `linux-arm64`
- `osx-x64`
- `osx-arm64`

托管 API 文档见 [YGOProSharp.NativeApi/README.md](YGOProSharp.NativeApi/README.md)，runtime 包文档见 [YGOProSharp.Native/README.md](YGOProSharp.Native/README.md)。

## WindBot

`YGOProSharp.WindBot` 是独立的协议 Bot 客户端项目，通过 `YGOProSharp.Protocol` 连接服务端，不嵌入服务器状态机，也不引用 `YGOProSharp.Server` / `YGOProSharp.NativeApi`。它复用 `YGOProSharp.Core` 的卡片数据库能力，并保留 WindBot 自己的客户端状态模型。

运行与配置说明见 [docs/windbot.md](docs/windbot.md)。当前适配目标是能编译、能按协议连接并发送基础 CTOS 响应，不保证 AI 对局完整可玩。
