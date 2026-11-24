namespace YGOProSharp.Persistence;

/// <summary>
/// 服务端运营关系型数据库引擎类型（与只读卡片库 <c>cards.cdb</c> 物理分离）。
/// </summary>
public enum OperationalDbProvider
{
    /// <summary>
    /// 默认零配置本地 SQLite（自动启用 WAL、外键约束与 BusyTimeout）。
    /// </summary>
    Sqlite = 0,

    /// <summary>
    /// 生产/多实例共享 PostgreSQL 数据库。
    /// </summary>
    Postgres = 1
}

/// <summary>
/// 运营数据持久化与多级缓存配置项。
/// </summary>
public sealed record PersistenceOptions
{
    public OperationalDbProvider DbProvider { get; init; } = OperationalDbProvider.Sqlite;

    public string ConnectionString { get; init; } = "Data Source=data/db/ygoprosharp.db";

    /// <summary>
    /// 可选 Redis 连接串（如 <c>127.0.0.1:6379</c> 或 <c>redis://redis:6379</c>）；为空时自动退化为单机 L1 内存缓存与内存限流。
    /// </summary>
    public string? RedisUrl { get; init; }

    /// <summary>
    /// 缓存与限流键统一命名空间前缀。
    /// </summary>
    public string CacheKeyPrefix { get; init; } = "ygp:";

    /// <summary>
    /// 云录像 <c>.yrp</c> 与观战回放流落盘目录。
    /// </summary>
    public string ReplayDirectory { get; init; } = "data/replays";

    /// <summary>
    /// 首次建库无超级管理员时的初始用户名（为空时读取环境变量 <c>YGOPRO_BOOTSTRAP_ADMIN_USER</c>，默认 <c>admin</c>）。
    /// </summary>
    public string? BootstrapAdminUser { get; init; }

    /// <summary>
    /// 首次建库无超级管理员时的初始密码（为空时读取 <c>YGOPRO_BOOTSTRAP_ADMIN_PASSWORD</c>；仍为空则自动生成 24 位高熵随机密码并要求首次登录改密）。
    /// </summary>
    public string? BootstrapAdminPassword { get; init; }

    /// <summary>
    /// 连续密码错误触发账号锁定的阈值（默认 5 次）。
    /// </summary>
    public int MaxLoginFailures { get; init; } = 5;

    /// <summary>
    /// 触发防爆破后的账号锁定时长（默认 15 分钟）。
    /// </summary>
    public int LockoutMinutes { get; init; } = 15;

    /// <summary>
    /// 面板登录会话有效期（默认 24 小时）。
    /// </summary>
    public int SessionTtlHours { get; init; } = 24;

    /// <summary>
    /// 登录接口每 IP 滑动窗口最大尝试次数（默认 5 分钟内最多 10 次请求，配合账号级 5 次失败锁定）。
    /// </summary>
    public int LoginRateLimitCount { get; init; } = 10;

    public int LoginRateLimitWindowSeconds { get; init; } = 300;

    /// <summary>
    /// Argon2id 内存开销（KiB，默认 19456 KiB = 19 MiB，符合 OWASP 推荐基线）。
    /// </summary>
    public int Argon2MemoryKb { get; init; } = 19456;

    public int Argon2Iterations { get; init; } = 2;

    public int Argon2Parallelism { get; init; } = 1;

    public static OperationalDbProvider ParseProvider(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return OperationalDbProvider.Sqlite;

        return raw.Trim().ToLowerInvariant() switch
        {
            "postgres" or "postgresql" or "pg" or "npgsql" => OperationalDbProvider.Postgres,
            _ => OperationalDbProvider.Sqlite
        };
    }
}
