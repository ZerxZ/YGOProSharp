# YGOProSharp

YGOProSharp 是一个实验性的 C# / .NET 项目，目标是把 YGOPro / OCGCore 生态里的 native runtime、核心领域、客户端协议、服务端状态机和 Bot 客户端整理成更清楚的工程边界。

这个仓库主要用于 **AI code vibing / AI 编程体验**：用 AI 辅助阅读、迁移、封装、重构和补全文档，观察 AI 参与工程化改造的过程。它不是稳定发行版，不保证能联机、开局或完整游玩。

## 来源与参考

- [Fluorohydride/ygopro-core](https://github.com/Fluorohydride/ygopro-core)：`ocgcore` 原生决斗核心。
- [IceYGO/ygosharp](https://github.com/IceYGO/ygosharp)：C# 服务端结构、协议与实现思路参考。
- [IceYGO/windbot](https://github.com/IceYGO/windbot)：WindBot 客户端 Bot 逻辑参考。
- [sherry_chaos/MDPro3](https://code.moenext.com/sherry_chaos/MDPro3)：当前导入的 WindBot 代码来源版本。

更多参考项目见 [docs/reference-projects.md](docs/reference-projects.md)，本轮对照实现记录见 [docs/reference-audit.md](docs/reference-audit.md)。

## 当前状态

- 不保证能玩。
- 不保证协议、卡片数据库、脚本和客户端版本兼容。
- 不建议直接用于正式服务器。
- 当前重点是验证 `NativeApi`、`Core`、`Protocol`、`Server`、`WindBot` 和 `Cli` 的解耦方式。

## 项目结构

```text
YGOProSharp/
├── YGOProSharp.Abstractions/  # OCG 契约、DTO、provider 接口、AppLog
├── YGOProSharp.Native/        # ocgcore runtime 二进制与 xmake 构建
├── YGOProSharp.NativeApi/     # ocgapi.h 的安全托管封装
├── YGOProSharp.Core/          # 卡片、卡组、随机组卡引擎、DIY 包管理、热重载 provider
├── YGOProSharp.Protocol/      # CTOS/STOC、packet framing、socket/WS/内存传输
├── YGOProSharp.Server/        # 服务端配置、1v1/Match/2v2 房间状态机、随机发牌、ED 猝死、单卡沙盒
├── YGOProSharp.Persistence/   # 运营数据库（SQLite WAL / PostgreSQL）与 HybridCache（L1 + Redis L2）
├── YGOProSharp.AgentBridge/   # 受限视角协议决斗代理（1v1 / 2v2 战争迷雾与会话隔离）
├── YGOProSharp.Mcp/           # 标准 MCP Server（Streamable HTTP + stdio，按角色裁剪工具与 AI 自取昵称）
├── YGOProSharp.Web/           # 单端口融合 Web 门户 (:7912，`/` 默认玩家大厅，`/admin` 管理后台，`/mcp` AI 接入)
├── YGOProSharp.Cli/           # 唯一命令行 host 和组合根（server / windbot / mcp）
├── YGOProSharp.WindBot/       # 独立协议 Bot 客户端库（C# + xmake 风格 Lua 执行器）
├── YGOProSharp.Tests/         # 行为测试、架构边界测试与端到端回归测试
├── web/                       # React + TypeScript + Vite 前端源码
├── Dockerfile                 # 四阶段非 root 净化容器构建
└── docker-compose.yml         # 默认单机 SQLite 与 --profile prod (PostgreSQL + Redis) 编排
```

关键边界：

- `Core` 不引用 `Protocol`、`Server`、`NativeApi` 或 `SevenZip`。
- `Protocol` 不引用 `Core`、`Server`、`NativeApi` 或 `SQLite`。
- `Server` 通过 typed options 组合 `Core`、`Protocol`、`NativeApi`，不解析 CLI 参数。
- `NativeApi` 是唯一直接接触 `ocgapi.h` raw binding、native handle 和 unsafe buffer 的项目。
- `WindBot` 是外部协议 Bot 客户端库，不引用 `Server` / `NativeApi`，也不再生成 exe。
- `Cli` 是唯一命令行入口，负责日志、配置文件、`Key=Value` 参数解析和启动分发。

## 快速开始

构建：

```bash
dotnet build YGOProSharp.slnx -c Release
```

运行测试：

```bash
dotnet run --project YGOProSharp.Tests/YGOProSharp.Tests.csproj -c Release
```

启动服务端（单端口 Web `:7912`：访问 `/` 进入玩家大厅，访问 `/admin` 进入管理后台，`/mcp` 为外部 AI 端点）：

```bash
dotnet run --project YGOProSharp.Cli/YGOProSharp.Cli.csproj -c Release -- server RootPath=. MultiRoom=true EnableWeb=true Port=7911 WebPort=7912
```

使用 Docker Compose 一键部署（复制 `.env.example` 为 `.env` 后按需修改）：

```bash
# 单机轻量模式（SQLite WAL + 进程内 L1 缓存）
docker compose up -d

# 生产高并发模式（启用内网隔离的 PostgreSQL 17 + Redis 7）
docker compose --profile prod up -d
```

启动 WindBot 客户端：

```bash
dotnet run --project YGOProSharp.Cli/YGOProSharp.Cli.csproj -c Release -- windbot DbPath=cards.cdb Host=127.0.0.1 Port=7911 Deck=AI_Yubel
```

运行时通常还需要 YGOPro 数据文件：

- `cards.cdb`
- `script/`
- `lflist.conf`
- WindBot 的 `Decks/`、`Dialogs/` 或等效运行时资源

## 日志

CLI 默认使用结构化单行 console 日志，默认级别是 `Information`。

```bash
dotnet run --project YGOProSharp.Cli -c Release -- server LogLevel=Debug StandardStreamProtocol=true
dotnet run --project YGOProSharp.Cli -c Release -- windbot LogLevel=Debug DbPath=cards.cdb
```

`LogLevel` 支持 `Trace`、`Debug`、`Information`、`Warning`、`Error`、`Critical`。生命周期事件在 `Information` 输出，包级和消息级细节在 `Debug` 输出，payload 短预览只在 `Trace` 输出。

## WindBot

`YGOProSharp.WindBot` 是独立协议 Bot 客户端库，通过 `YGOProSharp.Protocol` 连接服务端。它不是服务端内置 AI，不嵌入房间状态机，也不引用 `YGOProSharp.Server` / `YGOProSharp.NativeApi`。

当前适配目标是能编译、能按协议连接 YGOProSharp、能发送基础 CTOS 响应，不保证 AI 对局完整可玩。`Decks/` 和 `Dialogs/` 不纳入仓库；运行 Bot 时请自行提供卡组和对话资源。

更多说明见 [YGOProSharp.WindBot/README.md](YGOProSharp.WindBot/README.md) 和 [docs/windbot.md](docs/windbot.md)。

## 文档索引

- [项目思路](docs/project-thinking.md)
- [架构说明](docs/architecture.md)
- [调用流程](docs/call-flow.md)
- [服务端拆分说明](docs/server-migration.md)
- [WindBot 适配说明](docs/windbot.md)
- [AI MCP、Web 控制台与 DIY 上传规划](docs/ai-mcp-web-diy-plan.md)
- [AI 决斗智能体 MCP v2、赛后复盘、卡组六维评分与双打协同规划](docs/ai-mcp-review-rating-tagforce-plan.md)
- [开发代理规范](AGENTS.md)
- [参考项目索引](docs/reference-projects.md)
- [参考项目审计](docs/reference-audit.md)
- [NativeApi 文档](YGOProSharp.NativeApi/README.md)
- [Native runtime 文档](YGOProSharp.Native/README.md)

## License

本仓库以 MIT License 发布。第三方来源和 notice 见 [LICENSE](LICENSE)。
