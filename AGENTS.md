# YGOProSharp 开发代理规范

本文件是给 AI / Agent / 自动化开发助手使用的仓库级开发规范。除非用户明确覆盖，本仓库的修改都应遵守这里的边界和验证要求。

## 项目定位

YGOProSharp 是一个实验性的 C# / .NET 工程整理项目，用于 **AI code vibing / AI 编程体验**。目标是把 YGOPro / OCGCore 生态里的 native runtime、核心领域、客户端协议、服务端状态机和 Bot 客户端拆成更清楚的工程边界。

当前项目不保证可玩，不保证能稳定联机、开局或完整跑完对局，也不建议直接作为正式服务器使用。实现时优先保持边界清晰、可编译、可测试、可继续演进。

## 项目边界

- `YGOProSharp.Abstractions`：跨项目契约、OCG DTO、provider 接口、全局日志入口 `AppLog`。不放具体业务状态机。
- `YGOProSharp.Core`：卡片、卡组、禁限表、`.ydk` 序列化、预设盲盒卡组池与随机组卡引擎、可热重载资源包装、受管 DIY 包目录校验、OCG message reader、SQLite card DB manager。不能引用 Protocol、Server、NativeApi、SevenZip 或服务端运营库 ORM/Redis。
- `YGOProSharp.Protocol`：YGO 客户端协议、CTOS/STOC、packet framing、socket/WebSocket/内存传输 client/server。不能引用 Core、Server、NativeApi 或 SQLite。
- `YGOProSharp.Server`：服务端配置、房间（含 1v1 / Match / 2v2 Tag Duel）、玩家、Game 状态机、随机卡组注入、猝死规则（ED）、Replay、Addon。可以组合 Core、Protocol、NativeApi，不反向依赖 Web / MCP / Persistence。
- `YGOProSharp.Native`：只放 `ocgcore` runtime assets、LuaLS API 声明和 native 构建相关内容，不提供托管业务 API。
- `YGOProSharp.NativeApi`：唯一直接封装 `ocgapi.h` raw binding、native handle、unsafe buffer 和 callback pinning 的项目。
- `YGOProSharp.Cli`：命令行 host 和组合根，负责配置日志、创建 runtime、加固脚本沙箱、按启动顺序装配 Server / WindBot / 控制面。
- `YGOProSharp.WindBot`：独立协议 Bot 客户端（含 C# 与泛用 Lua 执行器），只复用 Core / Protocol / Abstractions，不引用 Server / NativeApi。
- `YGOProSharp.Persistence`（演进模块）：唯一承载服务端运营数据库（SQLite WAL / PostgreSQL）与多级缓存（`HybridCache` L1 + Redis L2、一次性 Ticket、滑动窗口限流）的基础设施层；严格保持只读卡片字典库（`cards.cdb`）与运营关系库物理分离，不泄漏到 Core、Protocol、Server、NativeApi 或 WindBot。
- `YGOProSharp.AgentBridge`（演进模块）：受限视角协议决斗代理，仅通过标准协议流桥接外部 MCP 对局会话或 WindBot 双脑慢思考，不直接读写 Server 内部状态或调用 NativeApi。
- `YGOProSharp.Mcp`（演进模块）：标准 MCP Server（Streamable HTTP / `stdio`），负责按角色最小暴露 Tool / Resource / Prompt 并执行会话隔离。
- `YGOProSharp.Web` & `web/`（演进模块）：ASP.NET Core 控制面、认证与细粒度 RBAC、WebSocket 事件流及前端管理面板；只单向组合下层模块，不污染核心层。
- `YGOProSharp.Tests`：行为测试、架构边界测试、回归测试。

## 开发约束

- **零内置大模型客户端**：全仓库禁止引入任何外部大模型 SDK（如 OpenAI、Gemini、Anthropic、DeepSeek 等），服务端不发起任何出站 LLM 请求、不托管大模型 API Key；所有外部 AI 统一作为受限权限的外部 MCP Client 接入。
- **Native 隔离**：业务层禁止直接使用 `LibraryImport`、`DllImport`、`IntPtr`、`byte*` 或 `OcgCoreImports` 访问 native。
- **日志与 stdio 纯净性**：禁止 `Console.WriteLine`、`Console.Error.WriteLine`、`Console.Write`、`Console.Error.Write` 回流；日志统一使用 `AppLog` / `ILogger`（尤其在 MCP `stdio` 模式下，`stdout` 必须独占用于协议帧）。
- **双库隔离**：卡片 SQLite（`Microsoft.Data.Sqlite`）只能出现在 `YGOProSharp.Core` 的 card DB manager 边界内；服务端运营数据库与 Redis 类型只能出现在 `YGOProSharp.Persistence` 内，不能泄漏到 `Card`、`Deck`、`Game`、Protocol、NativeApi 或 WindBot；不在 `Game` / `Player` 内堆砌业务持久化字段，统一通过轻量房间事件解耦。
- **领域纯粹性**：`Card`、`NamedCard`、`Deck` 等核心模型应保持领域模型职责，不重新引入静态全局查询；业务 API 签名不直接暴露 `ILogger` 参数。
- **协议与 Bot 边界**：WindBot 不能引用 `YGOProSharp.Server` 或 `YGOProSharp.NativeApi`，也不能恢复旧 `YGOSharp.*` / `MDPro3` 代码依赖；Protocol 只处理网络协议和包边界，不读取卡库、不调用 native、不持有服务端房间状态。
- **战争迷雾与 1v1 / 2v2 对局公平性**：服务端消息分发、`AgentBridge` 及任何角色（含 `admin`）的 MCP / Web 接口严禁透视进行中对局的对手暗牌、2v2（Tag Duel）非执子队友的未公开手牌/里侧额外卡组或牌堆洗牌顺序。
- **认证、RBAC 与直播脱敏**：禁止明文存储密码或通过 URL Query 传递凭证；面板密码使用 Argon2id 哈希，API / MCP Token 仅存储单向 SHA-256 摘要并采用 `CryptographicOperations.FixedTimeEquals` 常量时间比对；签发 Token 必须遵守**权限不放大原则**；直播只读角色（`streamer`）响应层强制剥离玩家 IP 与私密房口令。
- **DIY 上传与 Lua 沙箱安全**：所有上传文件、包名与压缩包条目必须校验路径穿越与 ZIP Slip，遵循 `Working Copy -> 预检 -> 原子替换`；NLua 脚本运行时禁用 `os`、`io`、`package`、`debug`、`dofile`、`loadfile`、`luanet` 等危险全局能力。
- **语言规范**：文档以中文为主，必要技术名词保留英文；代码注释也采用中文为主、英文为辅。

## 工作方式

- 先阅读当前代码和文档，再做最小必要改动。
- 保留用户已有未提交修改，不要回滚与当前任务无关的文件。
- 优先小步提交式改动，避免顺手重构。
- 功能变更需要同步或新增测试；文档变更需要保持链接可用、术语一致。
- 引入任何第三方代码或资源前，必须先检查 license，并同步更新 `LICENSE` 的 Third-Party Notices。
- 参考项目只作为设计和行为参考，不能默认视为可复制代码来源。

## 常用验证

文档或代码改动完成后，至少运行：

```bash
git diff --check
dotnet build YGOProSharp.slnx -c Release --no-incremental
```

涉及测试行为时继续运行：

```bash
dotnet run --project YGOProSharp.Tests/YGOProSharp.Tests.csproj -c Release --no-build
```

涉及文档编码时，扫描明显乱码：

```bash
rg -n "<mojibake-patterns>" -g "*.md" AGENTS.md LICENSE
```

## 参考项目

集中索引见 [docs/reference-projects.md](docs/reference-projects.md)。这些项目用于理解服务端、协议、WindBot、数据编码和线程模型，不等同于当前仓库的直接依赖或许可来源。
