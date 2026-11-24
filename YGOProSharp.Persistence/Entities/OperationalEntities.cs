namespace YGOProSharp.Persistence.Entities;

/// <summary>
/// 统一审计与软删除基类（参考 srvpro2 软删除模型，全表保留 CreatedAt / UpdatedAt / DeletedAt）。
/// </summary>
public abstract class AuditedEntityBase
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? DeletedAt { get; set; }
}

/// <summary>
/// 管理面板账号表（<c>panel_users</c>）。密码强制使用 Argon2id 哈希存储。
/// </summary>
public sealed class PanelUserEntity : AuditedEntityBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Username { get; set; } = string.Empty;

    public string NormalizedUsername { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public bool MustChangePassword { get; set; }

    public int FailedLoginCount { get; set; }

    public DateTime? LockedUntil { get; set; }

    /// <summary>
    /// 单用户细粒度权限点增减 JSON（<c>{"grant":["..."],"revoke":["..."]}</c>）。
    /// </summary>
    public string? PermissionsOverrideJson { get; set; }

    public DateTime? LastLoginAt { get; set; }

    public string? LastLoginIp { get; set; }

    public List<PanelUserRoleEntity> UserRoles { get; set; } = new();
}

/// <summary>
/// 预设与自定义角色表（<c>panel_roles</c>）。
/// </summary>
public sealed class PanelRoleEntity : AuditedEntityBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsBuiltIn { get; set; }

    /// <summary>
    /// 权限码 JSON 数组（如 <c>["room:view","room:kick"]</c>）。
    /// </summary>
    public string PermissionsJson { get; set; } = "[]";

    public List<PanelUserRoleEntity> UserRoles { get; set; } = new();
}

/// <summary>
/// 用户与角色多对多关联表（<c>panel_user_roles</c>）。
/// </summary>
public sealed class PanelUserRoleEntity
{
    public Guid UserId { get; set; }

    public PanelUserEntity User { get; set; } = null!;

    public Guid RoleId { get; set; }

    public PanelRoleEntity Role { get; set; } = null!;

    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 面板登录会话表（<c>panel_sessions</c>），仅存单向 SHA-256 摘要，不存明文会话令牌。
/// </summary>
public sealed class PanelSessionEntity : AuditedEntityBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    public string TokenPrefix { get; set; } = string.Empty;

    public string TokenSha256 { get; set; } = string.Empty;

    public string CsrfToken { get; set; } = string.Empty;

    public string? Ip { get; set; }

    public string? UserAgent { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;

    public DateTime? RevokedAt { get; set; }
}

/// <summary>
/// 外部 AI MCP 接入令牌表（<c>mcp_tokens</c>），仅存单向 SHA-256 摘要，签发时严格执行权限不放大校验。
/// </summary>
public sealed class McpTokenEntity : AuditedEntityBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid OwnerUserId { get; set; }

    public string OwnerUsername { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string TokenPrefix { get; set; } = string.Empty;

    public string TokenSha256 { get; set; } = string.Empty;

    /// <summary>
    /// MCP 角色：<c>player</c> / <c>spectator</c> / <c>diy_creator</c> / <c>admin</c>。
    /// </summary>
    public string McpRole { get; set; } = "player";

    public string? AllowedRoomsJson { get; set; }

    public int MaxSessions { get; set; } = 1;

    public bool RequireApproval { get; set; } = true;

    public DateTime? ExpiresAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public DateTime? LastUsedAt { get; set; }
}

/// <summary>
/// 游戏客户端玩家档案表（<c>player_profiles</c>，兼容 <c>昵称$密码</c> vpass 身份核验与战绩累计）。
/// </summary>
public sealed class PlayerProfileEntity : AuditedEntityBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string PlayerName { get; set; } = string.Empty;

    public string NormalizedName { get; set; } = string.Empty;

    public string? VpassHash { get; set; }

    public string? ClientKey { get; set; }

    public int ChatColor { get; set; }

    public int WinCount { get; set; }

    public int LoseCount { get; set; }

    public int FleeCount { get; set; }

    public int WinCombo { get; set; }
}

/// <summary>
/// 对局战报与云录像主表（<c>duel_records</c>，自增主键直接对应客户端 <c>R#&lt;id&gt;</c> 回放口令）。
/// </summary>
public sealed class DuelRecordEntity : AuditedEntityBase
{
    public long Id { get; set; }

    public string RoomName { get; set; } = string.Empty;

    public string RoomIdentifier { get; set; } = string.Empty;

    /// <summary>
    /// 0 = Single, 1 = Match, 2 = 2v2 Tag Duel。
    /// </summary>
    public int Mode { get; set; }

    public int Region { get; set; }

    public int MasterRule { get; set; } = 5;

    public int StartLp { get; set; } = 8000;

    public string HostInfoJson { get; set; } = "{}";

    public string RandomDeckMode { get; set; } = "None";

    public bool IsSuddenDeath { get; set; }

    public int DuelCount { get; set; } = 1;

    public int TurnCount { get; set; }

    public int WinnerTeam { get; set; } = -1;

    public int WinReason { get; set; }

    public uint Seed { get; set; }

    public string? YrpPath { get; set; }

    public string? ObserverFramesPath { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime EndedAt { get; set; } = DateTime.UtcNow;

    public List<DuelRecordPlayerEntity> Players { get; set; } = new();
}

/// <summary>
/// 对局战报席位明细表（<c>duel_record_players</c>，覆盖 1v1 两席与 2v2 Tag 四席）。
/// </summary>
public sealed class DuelRecordPlayerEntity : AuditedEntityBase
{
    public long Id { get; set; }

    public long DuelRecordId { get; set; }

    public DuelRecordEntity DuelRecord { get; set; } = null!;

    public int SeatIndex { get; set; }

    public int TeamId { get; set; }

    public string PlayerName { get; set; } = string.Empty;

    public string RealName { get; set; } = string.Empty;

    /// <summary>
    /// 席位类型：<c>human</c> / <c>windbot</c> / <c>mcp_ai</c>。
    /// </summary>
    public string SeatType { get; set; } = "human";

    public string? ClientIp { get; set; }

    public string? StartDeckYdk { get; set; }

    public string? IngameDeckYdk { get; set; }

    public bool IsWinner { get; set; }
}

/// <summary>
/// 玩家封禁表（<c>bans</c>，支持 <c>ip</c> / <c>cidr</c> / <c>name</c> / <c>client_key</c>）。
/// </summary>
public sealed class BanEntity : AuditedEntityBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TargetType { get; set; } = "ip";

    public string TargetValue { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public int FleeCount { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public string CreatedByName { get; set; } = "system";
}

/// <summary>
/// 赛事选手交牌表（<c>tournament_decks</c>）。
/// </summary>
public sealed class TournamentDeckEntity : AuditedEntityBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string TournamentTag { get; set; } = "default";

    public string PlayerName { get; set; } = string.Empty;

    public string NormalizedPlayerName { get; set; } = string.Empty;

    public string YdkContent { get; set; } = string.Empty;

    public string DeckHash { get; set; } = string.Empty;

    public int MainCount { get; set; }

    public int ExtraCount { get; set; }

    public int SideCount { get; set; }
}

/// <summary>
/// 外部 MCP AI 提交的待审阅 DIY 提案表（<c>diy_proposals</c>）。
/// </summary>
public sealed class DiyProposalEntity : AuditedEntityBase
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string PackageId { get; set; } = string.Empty;

    public Guid? ProposerTokenId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string DiffJson { get; set; } = "{}";

    /// <summary>
    /// <c>pending</c> / <c>approved</c> / <c>rejected</c>。
    /// </summary>
    public string Status { get; set; } = "pending";

    public Guid? ReviewerUserId { get; set; }

    public string? ReviewComment { get; set; }

    public DateTime? ReviewedAt { get; set; }
}

/// <summary>
/// 全链路安全与管服审计日志表（<c>audit_logs</c>）。
/// </summary>
public sealed class AuditLogEntity : AuditedEntityBase
{
    public long Id { get; set; }

    /// <summary>
    /// <c>panel_user</c> / <c>mcp_token</c> / <c>system</c> / <c>anonymous</c>。
    /// </summary>
    public string ActorType { get; set; } = "system";

    public string? ActorId { get; set; }

    public string ActorName { get; set; } = "system";

    public string Action { get; set; } = string.Empty;

    public string? PermissionChecked { get; set; }

    public string? Target { get; set; }

    public string? Ip { get; set; }

    /// <summary>
    /// <c>ok</c> / <c>bad_password</c> / <c>locked</c> / <c>permission_denied</c> / <c>privilege_escalation_blocked</c> / <c>rate_limited</c> / <c>error</c>。
    /// </summary>
    public string Result { get; set; } = "ok";

    public string? DetailJson { get; set; }
}
