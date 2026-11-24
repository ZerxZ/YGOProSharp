# 参考项目索引

本页记录 YGOProSharp 阅读和对比过的外部项目。它们用于理解协议、房间状态机、数据格式、Bot 客户端和线程模型，不表示当前仓库直接依赖这些项目。

本轮实现状态记录见 [参考项目审计](reference-audit.md)。如果后续从某个项目复制代码、资源、配置或数据文件，必须单独检查 license，并更新根目录 [LICENSE](../LICENSE) 的 Third-Party Notices。只作为阅读参考时，不加入 notice。

## 服务端与房间状态机

- [purerosefallen/srvpro2](https://github.com/purerosefallen/srvpro2)：现代化 srvpro 服务端结构、房间状态机、HostInfo 和房间列表思路。
- [purerosefallen/srvpro](https://github.com/purerosefallen/srvpro)：旧版 srvpro 行为和协议兼容参考。
- [mycard/srvpro](https://github.com/mycard/srvpro)：MyCard 生态下的 srvpro 分支参考。
- [diangogav/EDOpro-server-ts](https://github.com/diangogav/EDOpro-server-ts)：TypeScript 服务端实现，可用于对照网络流程和房间生命周期。

## 协议与核心流程

- [purerosefallen/koishipro-core.js](https://github.com/purerosefallen/koishipro-core.js)：JavaScript 侧核心流程、协议处理和数据组织参考。

## WindBot 来源与 Bot 客户端

- [IceYGO/windbot](https://github.com/IceYGO/windbot)：WindBot 原始项目参考。
- [nanahira/windbot](https://code.moenext.com/nanahira/windbot)：MoeNext 上的 WindBot 参考版本，主要用于 server mode host 对照。
- [sherry_chaos/MDPro3](https://code.moenext.com/sherry_chaos/MDPro3)：当前导入 WindBot 适配代码的来源版本，仅作为来源说明，不在 `.external/` 中 clone。

WindBot 在本仓库中保持外部协议 Bot 客户端定位，不嵌入 `YGOProSharp.Server`，也不引用 `YGOProSharp.NativeApi`。

## 数据与编码工具

- [purerosefallen/ygopro-yrp-encode](https://github.com/purerosefallen/ygopro-yrp-encode)：YRP replay 编码/解码参考。
- [purerosefallen/ygopro-msg-encode](https://github.com/purerosefallen/ygopro-msg-encode)：OCG/YGOPro message 编码参考，本轮用于补齐部分 CTOS/STOC 枚举和 packet helper。
- [purerosefallen/ygopro-cdb-encode](https://github.com/purerosefallen/ygopro-cdb-encode)：CDB 数据格式编码参考。
- [purerosefallen/ygopro-lflist-encode](https://github.com/purerosefallen/ygopro-lflist-encode)：禁限表 lflist 编码参考。

## 并发与线程模型

- [purerosefallen/yuzuthread](https://github.com/purerosefallen/yuzuthread)：线程模型、任务调度或并发设计参考。

## DIY 制卡、CDB 编辑与脚本工作台

- [YYY-ZSJ/YGODIY](https://github.com/YYY-ZSJ/YGODIY)（本地对照目录：`.external/YGODIY`）：游戏王 DIY 工作台参考，重点参考其结构化效果模型（`blocks/` 将条件/代价/取对象/处理双向映射为规范中文效果文本与 Lua 脚本、`TYPE_SPSUMMON` 等卡表标志位联动）、`lua_guard` 脚本静态检查、`ygopro-core` 单卡情景测试沙盒（`sandbox/`）以及 `.ypk` / `.cdb` / `strings.conf` 扩展包组织与回收站恢复思路。
- [kkr223/DataEditorY](https://github.com/kkr223/DataEditorY)（本地对照目录：`.external/DataEditorY`）：跨平台 `.cdb` 数据库 IDE 参考，重点参考其 `Draft -> Working Copy -> Atomic Save` 临时工作副本事务模型、旁路工作区元数据（不污染标准 `.cdb`）、左侧虚拟化卡表 + 规则表达式筛选 + 右侧 `Card / Script / Image / Proposal` 多工作面联动、Monaco + `aux.Stringid` 脚本提示、多 `.cdb` 优先级合并冲突预分析以及可审计提案（Proposal）审阅流。

## 使用原则

- 优先阅读参考项目理解行为，再在 YGOProSharp 内按当前分层重新实现。
- 不把参考项目的旧依赖、旧命名空间或旧构建系统带回当前仓库。
- 不因为参考项目存在某种结构，就打破 `Core / Protocol / Server / NativeApi / WindBot` 的依赖方向。
- 文档引用参考项目时，应明确“参考”而不是“兼容”或“支持”。
