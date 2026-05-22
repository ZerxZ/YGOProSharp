# YGOProSharp 开发代理规范

本文件是给 AI / Agent / 自动化开发助手使用的仓库级开发规范。除非用户明确覆盖，本仓库的修改都应遵守这里的边界和验证要求。

## 项目定位

YGOProSharp 是一个实验性的 C# / .NET 工程整理项目，用于 **AI code vibing / AI 编程体验**。目标是把 YGOPro / OCGCore 生态里的 native runtime、核心领域、客户端协议、服务端状态机和 Bot 客户端拆成更清楚的工程边界。

当前项目不保证可玩，不保证能稳定联机、开局或完整跑完对局，也不建议直接作为正式服务器使用。实现时优先保持边界清晰、可编译、可测试、可继续演进。

## 项目边界

- `YGOProSharp.Abstractions`：跨项目契约、OCG DTO、provider 接口、全局日志入口 `AppLog`。不放具体业务状态机。
- `YGOProSharp.Core`：卡片、卡组、禁限表、脚本 provider、OCG message reader、SQLite card DB manager。不能引用 Protocol、Server、NativeApi 或 SevenZip。
- `YGOProSharp.Protocol`：YGO 客户端协议、CTOS/STOC、packet framing、socket client/server。不能引用 Core、Server、NativeApi 或 SQLite。
- `YGOProSharp.Server`：服务端配置、房间、玩家、Game 状态机、Replay、Addon。可以组合 Core、Protocol、NativeApi。
- `YGOProSharp.Native`：只放 `ocgcore` runtime assets 和 native 构建相关内容，不提供托管业务 API。
- `YGOProSharp.NativeApi`：唯一直接封装 `ocgapi.h` raw binding、native handle、unsafe buffer 和 callback pinning 的项目。
- `YGOProSharp.Cli`：命令行 host 和组合根，负责配置日志、创建 runtime、启动 Server。
- `YGOProSharp.WindBot`：独立协议 Bot 客户端，只复用 Core / Protocol / Abstractions，不引用 Server / NativeApi。
- `YGOProSharp.Tests`：行为测试、架构边界测试、回归测试。

## 开发约束

- 业务层禁止直接使用 `LibraryImport`、`DllImport`、`IntPtr`、`byte*` 或 `OcgCoreImports` 访问 native。
- 禁止 `Console.WriteLine`、`Console.Error.WriteLine`、`Console.Write`、`Console.Error.Write` 回流；日志统一使用 `AppLog` / `ILogger`。
- SQLite 相关类型只能出现在 card DB manager 边界内，不能泄漏到 `Card`、`Deck`、`Game`、Protocol 或 Native provider。
- `Card`、`NamedCard`、`Deck` 等核心模型应保持领域模型职责，不重新引入静态全局查询。
- WindBot 不能引用 `YGOProSharp.Server` 或 `YGOProSharp.NativeApi`，也不能恢复旧 `YGOSharp.*` / `MDPro3` 代码依赖。
- Protocol 只处理网络协议和包边界，不读取卡库、不调用 native、不持有服务端房间状态。
- 文档以中文为主，必要技术名词保留英文；代码注释也采用中文为主、英文为辅。

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
