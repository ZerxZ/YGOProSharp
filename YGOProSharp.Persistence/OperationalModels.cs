using YGOProSharp.Persistence.Entities;
using YGOProSharp.Persistence.Security;

namespace YGOProSharp.Persistence;

public sealed record AuthenticatedPrincipal(
    Guid SessionId,
    Guid UserId,
    string Username,
    string DisplayName,
    bool MustChangePassword,
    string CsrfToken,
    IReadOnlyList<string> RoleCodes,
    IReadOnlySet<string> EffectivePermissions,
    string SessionTokenPrefix = "")
{
    public bool HasPermission(string permission) => RbacPolicyEvaluator.HasPermission(EffectivePermissions, permission);

    public bool IsPortalSessionOnly =>
        !string.IsNullOrWhiteSpace(SessionTokenPrefix) &&
        SessionTokenPrefix.StartsWith("ygp_usr_sess", StringComparison.OrdinalIgnoreCase);

    public bool IsStreamerOnly =>
        RoleCodes.Count > 0 &&
        RoleCodes.All(static r => string.Equals(r, BuiltInRoleCodes.Streamer, StringComparison.OrdinalIgnoreCase)) &&
        !HasPermission(RbacPermissions.RoomViewIp);

    public bool CanViewIp => HasPermission(RbacPermissions.RoomViewIp) && !IsStreamerOnly;

    public bool CanViewSystemLogs =>
        !IsStreamerOnly &&
        (HasPermission(RbacPermissions.AuditViewAll) ||
         HasPermission(RbacPermissions.ServerStop) ||
         HasPermission(RbacPermissions.RoomViewIp));

    public bool CanViewPrivateRoomSecrets(string? ownerUsername) =>
        !IsStreamerOnly &&
        (CanViewIp ||
         HasPermission(RbacPermissions.RoomEditRules) ||
         HasPermission(RbacPermissions.RoomClose) ||
         (!string.IsNullOrWhiteSpace(ownerUsername) &&
          string.Equals(Username, ownerUsername, StringComparison.OrdinalIgnoreCase)));
}

public sealed record LocalConsoleAdminResetResult(
    string Username,
    string EffectivePassword,
    bool MustChangePassword,
    int RevokedSessionCount);

public sealed record LoginResult(
    bool Success,
    string StatusCode,
    string Message,
    string? SessionToken,
    string? CsrfToken,
    DateTime? ExpiresAt,
    int? RemainingAttempts,
    int? LockedSeconds,
    AuthenticatedPrincipal? Principal);

public sealed record ValidatedMcpToken(
    Guid TokenId,
    Guid OwnerUserId,
    string OwnerUsername,
    string Name,
    string McpRole,
    IReadOnlyList<string> AllowedRooms,
    int MaxSessions,
    bool RequireApproval,
    string? PreferredPlayerName = null,
    string? PersonaStyle = null);

public sealed record IssuedMcpTokenResult(
    McpTokenEntity Entity,
    string RawToken);

public sealed record ActiveBanRule(
    Guid Id,
    string TargetType,
    string TargetValue,
    string Reason,
    DateTime? ExpiresAt,
    string CreatedByName,
    DateTime CreatedAt);

public sealed record SaveDuelRecordRequest(
    string RoomName,
    string RoomIdentifier,
    int Mode,
    int Region,
    int MasterRule,
    int StartLp,
    string HostInfoJson,
    string RandomDeckMode,
    bool IsSuddenDeath,
    int DuelCount,
    int TurnCount,
    int WinnerTeam,
    int WinReason,
    uint Seed,
    DateTime StartedAt,
    DateTime EndedAt,
    byte[]? YrpBytes,
    byte[]? ObserverPacketFrames,
    IReadOnlyList<SaveDuelPlayerSlot> Players,
    string? DecisionTimelineJson = null);

public sealed record SaveDuelPlayerSlot(
    int SeatIndex,
    int TeamId,
    string PlayerName,
    string RealName,
    string SeatType,
    string? ClientIp,
    string? StartDeckYdk,
    string? IngameDeckYdk,
    bool IsWinner);

/// <summary>
