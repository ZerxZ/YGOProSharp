# 项目思路

YGOProSharp 是一个工程整理实验，不是完整游戏产品。它尝试把 YGOPro 相关能力拆成更清晰的 .NET 项目边界，让 native interop、核心领域、客户端协议、服务端状态机和 Bot 客户端各自独立。

## 项目定位

本仓库服务于 AI code vibing / AI 编程体验：

- 用 AI 辅助理解旧代码和上游项目。
- 用 AI 辅助迁移、拆分、封装和补文档。
- 观察 AI 在真实工程约束下能否持续推进结构化改造。

因此它优先追求“边界清楚、能编译、能验证局部能力”，而不是马上追求完整可玩。

## 核心目标

- 把 `ocgcore` 原生能力隔离到 `Native` / `NativeApi`。
- 把卡片、卡组、禁限表、脚本 provider 留在 `Core`。
- 把 CTOS/STOC、packet framing、socket 通信放到 `Protocol`。
- 把房间、玩家、Game 状态机、Replay、Addon 放到 `Server`。
- 让 CLI 只作为组合根。
- 让 WindBot 作为外部协议客户端，而不是服务端内置 AI。

## 非目标

- 不承诺当前版本可玩。
- 不承诺兼容任意 YGOPro 客户端、数据库或脚本版本。
- 不把 WindBot AI 嵌入服务端。
- 不把 raw native API 泄漏给业务层。
- 不为旧 `YGOProSharp` 主库保留兼容 facade。

## 分层思路

项目拆分的核心是“谁拥有状态，谁只提供能力”：

- `Abstractions` 只放跨项目契约和日志入口。
- `Core` 提供可复用领域能力，不关心网络和房间。
- `Protocol` 提供网络协议能力，不关心卡库和对局规则。
- `NativeApi` 提供托管 native wrapper，不关心服务端配置。
- `Server` 组合所有能力，持有运行时状态机。
- `Cli` 配置依赖并启动服务。
- `WindBot` 作为外部客户端复用 Protocol 和 Core。

## 当前风险

- 上游代码来源复杂，部分逻辑来自旧 YGOSharp / WindBot / MDPro3 生态。
- 数据库、脚本、客户端版本不匹配时可能无法开局或运行。
- Native runtime 依赖当前 RID 的 `ocgcore` 文件，缺失时会启动失败。
- WindBot 缺少运行资源时需要用户自行提供 `Decks/`、`Dialogs/` 或等效配置。

## 后续方向

- 继续扩大架构边界测试。
- 为 Protocol 增加更多 loopback / packet 测试。
- 为 NativeApi 增加更多安全 wrapper 测试。
- 为 WindBot 增加最小可连接 smoke 测试。
- 在不改变边界的前提下，逐步补足实际运行所需的数据和脚本说明。

## 参考项目使用方式

外部项目用于帮助理解 YGOPro 生态里的服务端、协议、数据格式、Bot 和线程模型。集中索引见 [参考项目索引](reference-projects.md)。

这些项目只作为参考，不自动成为当前仓库的依赖或代码来源。后续如果复制代码、资源或配置，必须单独检查 license，并更新根目录 `LICENSE` 的 Third-Party Notices。
