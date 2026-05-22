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
