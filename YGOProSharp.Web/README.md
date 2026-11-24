# YGOProSharp.Web

`YGOProSharp.Web` 是基于 ASP.NET Core Minimal API 的服务端管理控制面。它单向组合 `YGOProSharp.Server`、`YGOProSharp.Core`、`YGOProSharp.Persistence` 与 `YGOProSharp.WindBot`，提供认证会话、五级 RBAC 权限中间件、IP 直播脱敏过滤、房间与裁判管理 API、缓存命中监控、WebSocket 实时事件推送以及前端 SPA 静态文件托管。

## 核心职责

- **认证与首次安全引导**：`/api/v1/auth/*`（`HttpOnly + SameSite=Strict` 会话 Cookie + CSRF 校验 + Bearer Token 兼容 + `must_change_password` 首次强制改密门禁）。
- **细粒度 RBAC 与脱敏**：按 `resource:action` 校验权限码；`streamer` 角色及无 `room:view_ip` 权限调用方自动剥离所有玩家 IP 与私密房口令；签发用户角色与 MCP Token 强制遵守**权限不放大原则**。
- **房间大厅、观众列表、可视化建房与云录像**：实时房间监控与观战观众列表（`observers`）、可视化建房参数选择与一键复制主机密码、在线改规、额外回合猝死（ED）、黄字广播、召唤内置 WindBot、踢出选手/观众、封禁与 `R#<id>` 云录像回放/下载。
- **独立端口前台玩家面板（`UserPortalHost`）**：物理独立端口监听（默认 `:7913`，独立会话 Cookie `ygp_portal_sid`），支持玩家自助注册与登录（固定分配受限 `ai_operator` 角色）、可视化选择配置创建自用房间、签发/管理个人名下 `player` / `spectator` 外部 AI Token、实时查看房间观众列表与一键复制主机密码、下载 `.ypk` 扩展包及投递社区 DIY 提案。
- **数据库与缓存可观测性**：实时汇报 SQLite WAL / PostgreSQL 连通性及 `HybridCache`（L1 内存 + L2 Redis）命中率与一键清缓存。
- **实时事件流**：`/ws/events`（支持一次性原子 `ws` Ticket 握手，严禁 URL Query 明文传密）。

## 边界

- 下层 `Core`、`Protocol`、`Server`、`NativeApi`、`WindBot`、`Persistence` 均不反向引用 `YGOProSharp.Web`。
- 不引入任何外部大模型 SDK，不发起任何出站 LLM 请求。
- 严格遵守对局战争迷雾：任何后台接口（含 `super_admin`）均不暴露进行中对局的暗手牌、里侧卡或洗牌顺序。
