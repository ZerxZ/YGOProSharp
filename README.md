# YGOProSharp

YGOProSharp 是一个实验性的 C# / .NET 项目，目标是把 YGOPro / OCGCore 相关能力整理成更清晰的 .NET 工程结构。

这个仓库主要用于 **AI code vibing / AI 编程体验**：用 AI 辅助阅读、迁移、封装、重构和补全代码，观察 AI 参与工程化改造的过程。它不是稳定发行版，不保证能联机、开局或完整游玩。

## 来源与参考

本项目使用或参考了以下项目：

- [Fluorohydride/ygopro-core](https://github.com/Fluorohydride/ygopro-core)：提供 `ocgcore` 原生决斗核心。
- [IceYGO/ygosharp](https://github.com/IceYGO/ygosharp)：提供 C# 服务端结构、协议与实现思路参考。

感谢这些项目和社区的长期积累。本仓库只是基于它们做 AI 辅助实验，不代表上游项目状态，也不保证兼容性。

## 项目状态

- 不保证能玩。
- 不保证协议、卡片数据库、脚本和客户端版本兼容。
- 不建议直接用于正式服务器。
- 主要价值是体验 AI 参与代码迁移、native interop、网络协议处理和工程自动化。

## 项目结构

```text
YGOProSharp/
├─ YGOProSharp.Abstractions/  # 纯契约、事件参数、OCG DTO、枚举和 provider 接口
├─ YGOProSharp.Native/        # ocgcore runtime 二进制与 xmake 构建，不提供托管 API
├─ YGOProSharp.NativeApi/     # ocgapi.h 的安全托管封装
├─ YGOProSharp/               # 核心服务器、游戏流程、数据库与脚本 provider
├─ YGOProSharp.Cli/           # 组合根和命令行入口
└─ YGOProSharp.Tests/         # 基础测试和架构边界测试
```

核心边界：

- `YGOProSharp.Abstractions` 只放契约，不依赖 Native、NativeApi、Sqlite 或 SevenZip。
- `YGOProSharp.Native` 只负责提供 `ocgcore` runtime 文件。
- `YGOProSharp.NativeApi` 独占 `LibraryImport`、`SafeHandle`、native buffer 和 `ocgapi.h` raw binding。
- `YGOProSharp` 通过 `IDuelFactory` / `IDuelSession` 调用 native 能力，不直接接触 `IntPtr`、`byte*` 或 P/Invoke。
- `YGOProSharp.Cli` 作为组合根创建 `NativeOcgRuntime`，注入卡片数据库与脚本 provider。

## Native API

托管 native 边界基于 `YGOProSharp.Native/ygopro-core/ocgapi.h`。

`YGOProSharp.NativeApi` 默认使用 `create_duel_v2(uint[8])` 创建决斗；`create_duel(uint)` 仅作为 legacy / replay 路径保留在 wrapper 中。业务层只应依赖 `YGOProSharp.Abstractions.Ocg` 中的接口：

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

运行时通常还需要 YGOPro 数据文件，默认从当前目录读取：

- `cards.cdb`
- `script/`
- `lflist.conf`

## Logging

```bash
dotnet run --project YGOProSharp.Cli -c Release -- LogLevel=Debug
dotnet run --project YGOProSharp.Cli -c Release -- LogLevel=Information StandardStreamProtocol=true
```

`LogLevel` supports `Trace`, `Debug`, `Information`, `Warning`, `Error`, and `Critical`.
The CLI uses structured single-line console logging at `Information` by default.
Lifecycle events are logged at `Information`; packet and message details are logged at `Debug` or `Trace`.

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
