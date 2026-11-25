# 参考项目审计

本页记录“看了什么、吸收什么、不吸收什么、当前做到哪一步”。本轮没有直接复制 `.external/` 中的代码或资源，所有改动都按当前项目边界重新实现。

## 已吸收

### 协议基础

参考来源：

- `purerosefallen/ygopro-msg-encode`
- `purerosefallen/srvpro2`
- `purerosefallen/srvpro`

落地内容：

- 补齐 `CtosMessage.RequestField = 0x30`。
- 补齐 `StocMessage.DeckCount = 0x09`、`FieldFinish = 0x30`、`SrvproRoomlist = 0x31`。
- 修复 UTF-16 固定字符串读取：首字符就是 `\0` 时返回空字符串。
- 增加固定长度 UTF-8 读写 helper，给后续 `srvpro-roomlist` 兼容预留基础能力。

不吸收内容：

- 不引入参考项目的完整 room list 协议实现。
- 不改变当前 packet framing。

### 禁限表与 CDB 映射

参考来源：

- `purerosefallen/ygopro-lflist-encode`
- `purerosefallen/ygopro-cdb-encode`

落地内容：

- `Banlist` 增加可选 `Name`。
- `BanlistManager.ParseText(string)` 支持从文本解析多个列表、注释、`!name` 和 `$whitelist`。
- `Init(string fileName)` 复用同一 parser。
- 增加 CDB 映射测试：link 卡 `def -> link_marker`、灵摆刻度、多数据库 named card 加载。

不吸收内容：

- 不替换当前 hash 计算。
- 不改变默认禁限行为。

### 服务端安全补强

参考来源：

- `srvpro2`
- `EDOpro-server-ts`

落地内容：

- 新增 `HostInfo` record，集中表达 CreateGame payload 中的房间规则。
- `Game.SetRules` 先解析 `HostInfo` 再应用到现有字段，减少重复解析。
- `GamePacketFactory` 增加 `CreateDeckCount(...)` 和 `CreateFieldFinish()`。
- `Player` 支持 `RequestField`；未进入 duel 时只记录 debug 并忽略，duel 中按现有观战同步路径补场面。

不吸收内容：

- 不承诺完整 srvpro2 / EDOPro server feature parity。
- 不改变默认 duel 流程和 native 调用顺序。

### WindBot host

参考来源：

- `nanahira/windbot`
- `IceYGO/windbot`

落地内容：

- 将 `Program.RunAsServer` 抽为 `WindBotServerModeHost`。
- 保持 HTTP query 参数兼容。
- 增加可选 `BotListPath` JSON 读取；缺失文件只输出清晰日志，不崩溃。

不吸收内容：

- 不复制 `Decks/`、`Dialogs/`。
- 不恢复 `YGOSharp.*`、`MDPro3` 依赖。
- WindBot 仍不引用 `YGOProSharp.Server` / `YGOProSharp.NativeApi`。

## 后续候选

- `SrvproRoomlist` 的完整编码和发送策略。
- replay/YRP 的读写工具化。
- 更完整的 CDB/lflist 文档和导入校验。
- 服务端房间列表、房间发现、观战字段同步的兼容性测试。
- WindBot server mode 的 bot pool、限流和更清晰的错误响应。

## Phase 5 专项调研审计：AI 决斗代理、复盘评分与双打协同

详细演进规划见 [AI 决斗智能体 MCP v2、赛后复盘、卡组六维评分与「卡片力量」双打协同规划](ai-mcp-review-rating-tagforce-plan.md)。

参考来源：

- `Noctfom/Galatea-Link`（`GPL-3.0`）
- `Noctfom/Galatea-Core`（`GPL-3.0`）
- `dynamder/ygo-ai`（`MIT`）

计划吸收的设计思想（全部在 YGOProSharp 既有分层内洁净室重新实现）：

- **单次事件票据与往返压缩**（参考 `ygo-ai`）：单调递增 `promptId` 票据校验（越界错误不烧票）、`duel_submit_action` 内联返回下一待决提示 + 只追加增量事件 `newEvents` + 盘面差异 `boardChanges`、唯一合法项 `[fast]` 快路径与同阶段重复连锁 `echo` 折叠。
- **游戏内聊天防注入与双通道吐槽**（参考 `Galatea-Link`）：拆分独立有界聊天流 `chatContext` 并标注 `untrusted_player_chat`，支持出牌内联 `chatLine`（UTF-16LE 长度裁剪 + 重复与频率节流）与人设风格绑定。
- **多时间尺度长期规划与卡组构筑画像分离**（参考 `Galatea-Core`）：分离稳定初始构筑画像与动态无序剩余卡组计数；在会话内维护 `GamePlan / TurnPlan / PhaseIntent` 三层战略规划黑板并在每次观测中回显，搭配跨局 `ai_duelist_memories` 经验库。
- **本仓原创扩展**：2v2 双打显式敌我/队友顺位拓扑（`TagTeamContext`）、《游戏王：卡片力量》风格搭档战术指令系统（`/partner` + `duel_tag_sync` + 网页战术轮盘）、赛后全视角复盘打分（`duel_reviews`）及六维 `0-100` 卡组量化评分体系（`burst_power`、`resilience`、`stun_toxicity`、`fun_factor`、`complexity`、`meta_adaptability`）。

明确不吸收内容：

- **绝不复制 `Galatea-Link` / `Galatea-Core` 的任何 GPL-3.0 源码、脚本或模型制品**，保持本仓库 MIT 许可证纯净性。
- **不引入内置大模型客户端或模型密钥托管**（不吸收 `Galatea-Link` 直连 OpenAI Compatible API 及 `link_secrets.json` 机制，严守全仓零内置 LLM SDK 约束）。
- **不在服务端引入 Python / PyTorch / ONNX Runtime 重型训练推理栈**。
