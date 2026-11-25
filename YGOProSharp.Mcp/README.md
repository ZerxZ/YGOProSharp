# YGOProSharp.Mcp

`YGOProSharp.Mcp` 是零内置大模型 SDK 的标准 **Model Context Protocol (MCP)** 服务端实现，提供 **Streamable HTTP（`/mcp`）** 与 **本地 `stdio` 转发（`McpStdioRelay`）** 两种传输模式。

## 角色与权限分级

- **`spectator`**：只读查询房间列表、公开观战局面、卡库与禁限卡表。
- **`player`**：在 `spectator` 基础上开放自建房间与召唤陪练（`room_create`、`room_spawn_windbot`，受 `AllowedRooms` 白名单约束）、禁限自适应选卡组（`duel_list_preset_decks`、`duel_set_deck`、`duel_get_deck_list`）、实时结算对局操作（`duel_join_room`、`duel_set_ready`、`duel_wait_for_turn`、`duel_get_observation`、`duel_submit_action`）、批量连招与自动跳过连锁（`duel_submit_batch`、`duel_auto_play`、`duel_configure_policy`）以及战术记忆与 2v2 搭档协同（`duel_update_plan`、`duel_sync_partner_tactic`）。支持 `detail = "none" | "minimal" | "standard" | "full"` 多级紧凑快照。
- **`diy_creator`**：在后台控制端口开放 DIY 单卡沙盒测试、DIY 提案/写入、卡组效文巡检、WindBot Lua 沙箱脚本保存与进程内闪电自博弈模拟（`windbot_simulate_match`）。
- **`admin`**：在后台控制端口开放服务状态查询、强制关房与踢人裁判操作（同样严禁透视进行中对局暗牌）。

## 双端口挂载隔离

- 前台玩家门户端口（默认 `:7913/mcp`）：严格限制仅允许 `player` 与 `spectator` 角色令牌访问。
- 后台管理控制端口（默认 `:7912/mcp`）：支持全角色令牌访问。
- 同时兼容 `Authorization: Bearer <token>` 请求头、`?key=<token>` 查询参数（日志自动脱敏掩码）及 `YGOProSharp.Cli mcp` 本地 `stdio` 转发。
