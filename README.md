# YGOProSharp

YGOProSharp 是一个实验性的 C# / .NET 项目，目标是尝试把 YGOPro/OCGCore 相关能力整理成更现代的 .NET 结构。

这个仓库主要用于 **AI code vibing**：用 AI 辅助阅读、迁移、封装、重构和补全代码，体验 AI 编程流程本身。它不是一个稳定发行版，也不保证能正常联机、开局或完整游玩。

## 来源与参考

本项目使用或参考了以下项目：

- [Fluorohydride/ygopro-core](https://github.com/Fluorohydride/ygopro-core)：提供 `ocgcore` 原生决斗核心。
- [IceYGO/ygosharp](https://github.com/IceYGO/ygosharp)：提供 C# 服务端结构、协议和实现思路参考。

感谢这些项目和社区的长期积累。本仓库只是基于它们做 AI 辅助实验，不代表上游项目状态，也不保证兼容性。

## 项目状态

- 不保证能玩。
- 不保证协议、卡片数据库、脚本和客户端版本兼容。
- 不建议直接用于正式服务器。
- 主要价值是观察 AI 参与代码迁移、原生互操作、网络协议处理和工程自动化的过程。

## 项目结构

- `YGOProSharp`：托管端核心代码、服务器逻辑和 OCGCore 包装。
- `YGOProSharp.Cli`：命令行启动入口。
- `YGOProSharp.Native`：`ocgcore` 原生库构建与运行时资源。
- `YGOProSharp.Tests`：基础测试项目。

## 构建

需要安装 .NET SDK。若要重新构建原生 `ocgcore`，还需要 xmake 和对应平台的 C/C++ 工具链。

```bash
dotnet build YGOProSharp.slnx -c Release
```

运行 CLI：

```bash
dotnet run --project YGOProSharp.Cli -c Release
```

运行时通常还需要 `cards.cdb`、`script`、`lflist.conf` 等 YGOPro 数据文件。默认会从当前目录读取：

- `cards.cdb`
- `script/`
- `lflist.conf`

## 说明

这个项目的核心定位是“AI 编程体验场”。如果它能跑起来，那很好；如果不能，也符合预期。代码会随着实验继续变化，接口、项目结构和构建方式都可能调整。
