# YGOProSharp.Persistence

`YGOProSharp.Persistence` 是服务端唯一承载**运营关系型数据库**与**多级缓存体系**的基础设施层，严格与 `YGOProSharp.Core` 中的只读卡片字典库（`cards.cdb`）物理隔离。

## 核心职责

- **双引擎关系库（`OperationalDbContext` / `OperationalDatabaseManager`）**：
  - 默认零配置 `SQLite`（自动启用 `WAL`、`foreign_keys=ON` 与 `busy_timeout=5000`）；
  - 可选切换 `PostgreSQL`（基于 `Npgsql.EntityFrameworkCore.PostgreSQL`）；
  - 全表统一维护 `CreatedAt`、`UpdatedAt` 与 `DeletedAt` 全局软删除过滤。
- **密码学与五级 RBAC（`Argon2PasswordHasher` / `TokenCrypto` / `RbacPolicyEvaluator`）**：
  - 面板账号密码统一采用 **Argon2id** 单向哈希，首次建库无超管时自动生成 24 位高熵一次性随机密码并强制首次登录改密；
  - API 会话与 MCP Token 仅存储单向 SHA-256 摘要，采用 `CryptographicOperations.FixedTimeEquals` 常量时间比对；
  - 内置五级预设角色（`super_admin`、`judge`、`diy_maintainer`、`ai_operator`、`streamer`）与单用户细粒度覆盖（`grant`/`revoke`），严格执行**权限不放大原则**与 `streamer` IP 脱敏。
- **多级缓存与限流票据（`OperationalCacheService`）**：
  - 基于 `.NET HybridCache`（L1 `IMemoryCache` + 可选 L2 `StackExchange.Redis` + Redis Pub/Sub 跨节点失效广播），未配置 Redis 时自动退化为纯内存模式；
  - 内置精确的 `L1 Hits` / `L2 Redis Hits` / `Misses` / 命中率遥测、一次性原子阅后即焚票据（`GETDEL`）与滑动窗口限流。
