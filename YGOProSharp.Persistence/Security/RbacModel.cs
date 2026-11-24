using System.Text.Json;

namespace YGOProSharp.Persistence.Security;

/// <summary>
/// 管理面板细粒度权限码常量（<c>resource:action</c>）。
/// </summary>
public static class RbacPermissions
{
    public const string Wildcard = "*";

    public const string DashboardView = "dashboard:view";
    public const string UserManage = "user:manage";
    public const string RoleManage = "role:manage";
    public const string ServerStop = "server:stop";
    public const string SystemCacheFlush = "system:cache_flush";
    public const string AuditViewAll = "audit:view_all";

    public const string RoomView = "room:view";
    public const string RoomViewIp = "room:view_ip";
    public const string RoomCreate = "room:create";
    public const string RoomCreateOwn = "room:create_own";
    public const string RoomEditRules = "room:edit_rules";
    public const string RoomKick = "room:kick";
    public const string RoomClose = "room:close";
    public const string RoomStartDeath = "room:start_death";
    public const string RoomShout = "room:shout";

    public const string BanManage = "ban:manage";
    public const string TournamentDeckRead = "tournament_deck:read";
    public const string TournamentDeckWrite = "tournament_deck:write";
    public const string DuelLogView = "duel_log:view";
    public const string DuelLogViewOwn = "duel_log:view_own";
    public const string ReplayDownload = "replay:download";
    public const string BotSpawn = "bot:spawn";

    public const string DiyView = "diy:view";
    public const string DiyUpload = "diy:upload";
    public const string DiyEdit = "diy:edit";
    public const string DiySandboxTest = "diy:sandbox_test";
    public const string DiyReviewProposal = "diy:review_proposal";
    public const string DiyReload = "diy:reload";
    public const string DeckPoolView = "deck_pool:view";
    public const string DeckPoolManage = "deck_pool:manage";
    public const string WindBotEditLua = "windbot:edit_lua";
    public const string WindBotSimulate = "windbot:simulate";
    public const string CardSearch = "card:search";

    public const string McpTokenIssueSelf = "mcp_token:issue_self";
    public const string McpTokenManageAll = "mcp_token:manage_all";

    public static IReadOnlyList<string> AllExplicitPermissions { get; } =
    [
        DashboardView,
        UserManage,
        RoleManage,
        ServerStop,
        SystemCacheFlush,
        AuditViewAll,
        RoomView,
        RoomViewIp,
        RoomCreate,
        RoomCreateOwn,
        RoomEditRules,
        RoomKick,
        RoomClose,
        RoomStartDeath,
        RoomShout,
        BanManage,
        TournamentDeckRead,
        TournamentDeckWrite,
        DuelLogView,
        DuelLogViewOwn,
        ReplayDownload,
        BotSpawn,
        DiyView,
        DiyUpload,
        DiyEdit,
        DiySandboxTest,
        DiyReviewProposal,
        DiyReload,
        DeckPoolView,
        DeckPoolManage,
        WindBotEditLua,
        WindBotSimulate,
        CardSearch,
        McpTokenIssueSelf,
        McpTokenManageAll
    ];
}

/// <summary>
/// 五级预设角色编码常量。
/// </summary>
public static class BuiltInRoleCodes
{
    public const string SuperAdmin = "super_admin";
    public const string Judge = "judge";
    public const string DiyMaintainer = "diy_maintainer";
    public const string AiOperator = "ai_operator";
    public const string Streamer = "streamer";
}

/// <summary>
/// MCP 四级角色编码常量。
/// </summary>
public static class McpRoleCodes
{
    public const string Spectator = "spectator";
    public const string Player = "player";
    public const string DiyCreator = "diy_creator";
    public const string Admin = "admin";

    public static IReadOnlyList<string> All { get; } = [Spectator, Player, DiyCreator, Admin];
}

public sealed record BuiltInRoleTemplate(
    string Code,
    string Name,
    string Description,
    IReadOnlyList<string> Permissions);

public sealed record UserPermissionsOverride(
    IReadOnlyList<string> Grant,
    IReadOnlyList<string> Revoke)
{
    public static UserPermissionsOverride Empty { get; } = new([], []);
}

/// <summary>
/// RBAC 权限求值与「权限不放大铁律」校验器。
/// </summary>
public static class RbacPolicyEvaluator
{
    public static IReadOnlyList<BuiltInRoleTemplate> BuiltInRoles { get; } =
    [
        new BuiltInRoleTemplate(
            BuiltInRoleCodes.SuperAdmin,
            "超级管理员 (Super Admin)",
            "拥有全部系统管理、用户权限、缓存刷新、房间裁判与 DIY 热重载权限。",
            [RbacPermissions.Wildcard]),
        new BuiltInRoleTemplate(
            BuiltInRoleCodes.Judge,
            "赛事裁判 / 房管 (Judge)",
            "巡房、查看选手 IP、建房改规、开启额外回合猝死 (ED)、黄字喊话、封禁违规与核验比赛交牌。",
            [
                RbacPermissions.DashboardView,
                RbacPermissions.RoomView,
                RbacPermissions.RoomViewIp,
                RbacPermissions.RoomCreate,
                RbacPermissions.RoomEditRules,
                RbacPermissions.RoomKick,
                RbacPermissions.RoomClose,
                RbacPermissions.RoomStartDeath,
                RbacPermissions.RoomShout,
                RbacPermissions.BanManage,
                RbacPermissions.TournamentDeckRead,
                RbacPermissions.TournamentDeckWrite,
                RbacPermissions.DuelLogView,
                RbacPermissions.ReplayDownload,
                RbacPermissions.BotSpawn,
                RbacPermissions.CardSearch,
                RbacPermissions.DeckPoolView,
                RbacPermissions.McpTokenIssueSelf
            ]),
        new BuiltInRoleTemplate(
            BuiltInRoleCodes.DiyMaintainer,
            "DIY 与脚本维护 (DIY Maintainer)",
            "上传编辑 DIY 包、单卡沙盒试跑、审批 AI 提案、管理盲盒卡组池与 WindBot Lua 脚本并触发热重载。",
            [
                RbacPermissions.DashboardView,
                RbacPermissions.RoomView,
                RbacPermissions.DiyView,
                RbacPermissions.DiyUpload,
                RbacPermissions.DiyEdit,
                RbacPermissions.DiySandboxTest,
                RbacPermissions.DiyReviewProposal,
                RbacPermissions.DiyReload,
                RbacPermissions.DeckPoolView,
                RbacPermissions.DeckPoolManage,
                RbacPermissions.WindBotEditLua,
                RbacPermissions.WindBotSimulate,
                RbacPermissions.CardSearch,
                RbacPermissions.DuelLogView,
                RbacPermissions.ReplayDownload,
                RbacPermissions.McpTokenIssueSelf
            ]),
        new BuiltInRoleTemplate(
            BuiltInRoleCodes.AiOperator,
            "斗蛐蛐 / 玩家成员 (AI Operator)",
            "查看大厅、创建自用对局/斗蛐蛐房、签发归属本人的 player / spectator MCP Token、下载录像。",
            [
                RbacPermissions.DashboardView,
                RbacPermissions.RoomView,
                RbacPermissions.RoomCreateOwn,
                RbacPermissions.DuelLogViewOwn,
                RbacPermissions.ReplayDownload,
                RbacPermissions.CardSearch,
                RbacPermissions.DeckPoolView,
                RbacPermissions.McpTokenIssueSelf
            ]),
        new BuiltInRoleTemplate(
            BuiltInRoleCodes.Streamer,
            "解说主播 / 只读观察 (Streamer)",
            "直播投屏安全只读视图：强制剥离所有玩家 IP 与私密房间密码，最高仅可签发 spectator MCP Token。",
            [
                RbacPermissions.DashboardView,
                RbacPermissions.RoomView,
                RbacPermissions.DuelLogView,
                RbacPermissions.ReplayDownload,
                RbacPermissions.CardSearch,
                RbacPermissions.DeckPoolView,
                RbacPermissions.McpTokenIssueSelf
            ])
    ];

    public static IReadOnlyList<string> ParsePermissionsJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        try
        {
            string[]? parsed = JsonSerializer.Deserialize<string[]>(json);
            return parsed is null
                ? []
                : parsed.Where(static s => !string.IsNullOrWhiteSpace(s)).Select(static s => s.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static string SerializePermissions(IEnumerable<string> permissions)
    {
        string[] normalized = permissions
            .Where(static p => !string.IsNullOrWhiteSpace(p))
            .Select(static p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return JsonSerializer.Serialize(normalized);
    }

    public static UserPermissionsOverride ParseOverrideJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return UserPermissionsOverride.Empty;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            List<string> grant = new();
            List<string> revoke = new();

            if (doc.RootElement.TryGetProperty("grant", out JsonElement grantEl) && grantEl.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in grantEl.EnumerateArray())
                {
                    if (item.GetString() is { } s && !string.IsNullOrWhiteSpace(s))
                        grant.Add(s.Trim());
                }
            }

            if (doc.RootElement.TryGetProperty("revoke", out JsonElement revokeEl) && revokeEl.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in revokeEl.EnumerateArray())
                {
                    if (item.GetString() is { } s && !string.IsNullOrWhiteSpace(s))
                        revoke.Add(s.Trim());
                }
            }

            return new UserPermissionsOverride(grant, revoke);
        }
        catch (JsonException)
        {
            return UserPermissionsOverride.Empty;
        }
    }

    public static string? SerializeOverride(UserPermissionsOverride? @override)
    {
        if (@override is null || (@override.Grant.Count == 0 && @override.Revoke.Count == 0))
            return null;

        return JsonSerializer.Serialize(new
        {
            grant = @override.Grant.Where(static s => !string.IsNullOrWhiteSpace(s)).Select(static s => s.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            revoke = @override.Revoke.Where(static s => !string.IsNullOrWhiteSpace(s)).Select(static s => s.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        });
    }

    public static HashSet<string> ComputeEffectivePermissions(
        IEnumerable<IEnumerable<string>> rolePermissionsList,
        UserPermissionsOverride? userOverride = null)
    {
        HashSet<string> effective = new(StringComparer.OrdinalIgnoreCase);
        foreach (IEnumerable<string> rolePerms in rolePermissionsList)
        {
            foreach (string perm in rolePerms)
            {
                if (!string.IsNullOrWhiteSpace(perm))
                    effective.Add(perm.Trim());
            }
        }

        if (userOverride is not null)
        {
            foreach (string g in userOverride.Grant)
            {
                if (!string.IsNullOrWhiteSpace(g))
                    effective.Add(g.Trim());
            }

            // 若未持有通配符 *，直接移除被撤销项；若持有 * 且显式撤销了某项，展开显式权限集后再移除该项
            if (userOverride.Revoke.Count > 0)
            {
                if (effective.Contains(RbacPermissions.Wildcard))
                {
                    effective.Remove(RbacPermissions.Wildcard);
                    foreach (string explicitPerm in RbacPermissions.AllExplicitPermissions)
                        effective.Add(explicitPerm);
                }

                foreach (string r in userOverride.Revoke)
                {
                    if (!string.IsNullOrWhiteSpace(r))
                        effective.Remove(r.Trim());
                }
            }
        }

        return effective;
    }

    public static bool HasPermission(IReadOnlySet<string> effectivePermissions, string requiredPermission)
    {
        if (string.IsNullOrWhiteSpace(requiredPermission))
            return true;

        if (effectivePermissions.Contains(RbacPermissions.Wildcard))
            return true;

        return effectivePermissions.Contains(requiredPermission.Trim());
    }

    public static bool HasAnyPermission(IReadOnlySet<string> effectivePermissions, params ReadOnlySpan<string> candidates)
    {
        if (effectivePermissions.Contains(RbacPermissions.Wildcard))
            return true;

        foreach (string candidate in candidates)
        {
            if (effectivePermissions.Contains(candidate))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 权限不放大铁律校验：操作者在创建/修改用户或编辑角色时，目标权限集合必须是操作者有效权限集的子集。
    /// </summary>
    public static bool IsSubsetOfActorPermissions(
        IReadOnlySet<string> actorEffectivePermissions,
        IEnumerable<string> targetPermissions)
    {
        if (actorEffectivePermissions.Contains(RbacPermissions.Wildcard))
            return true;

        foreach (string target in targetPermissions)
        {
            string trimmed = target.Trim();
            if (string.IsNullOrEmpty(trimmed))
                continue;

            // 非通配符持有者绝不可授予 *
            if (string.Equals(trimmed, RbacPermissions.Wildcard, StringComparison.Ordinal))
                return false;

            if (!actorEffectivePermissions.Contains(trimmed))
                return false;
        }

        return true;
    }

    /// <summary>
    /// 计算给定面板用户允许签发的 MCP Token 角色列表（严格遵循权限不放大原则与角色上限矩阵）。
    /// </summary>
    public static IReadOnlyList<string> GetAllowedMcpRolesToIssue(
        IReadOnlyCollection<string> actorRoleCodes,
        IReadOnlySet<string> actorEffectivePermissions)
    {
        if (!HasAnyPermission(actorEffectivePermissions, RbacPermissions.McpTokenIssueSelf, RbacPermissions.McpTokenManageAll))
            return [];

        if (actorEffectivePermissions.Contains(RbacPermissions.Wildcard))
            return [McpRoleCodes.Player, McpRoleCodes.Spectator, McpRoleCodes.DiyCreator, McpRoleCodes.Admin];

        // Streamer 角色如果仅具有 streamer 级只读权限，强制最高只能签发 spectator
        bool isPureStreamer = actorRoleCodes.Count > 0 &&
                              actorRoleCodes.All(static c => string.Equals(c, BuiltInRoleCodes.Streamer, StringComparison.OrdinalIgnoreCase)) &&
                              !HasAnyPermission(actorEffectivePermissions, RbacPermissions.RoomCreate, RbacPermissions.RoomCreateOwn, RbacPermissions.DiyEdit);
        if (isPureStreamer)
            return [McpRoleCodes.Spectator];

        List<string> allowed = new();
        if (HasPermission(actorEffectivePermissions, RbacPermissions.RoomView))
            allowed.Add(McpRoleCodes.Spectator);

        // player 需要至少拥有建房/自建房或裁判权限（高于纯只读主播）
        if (HasAnyPermission(actorEffectivePermissions, RbacPermissions.RoomCreate, RbacPermissions.RoomCreateOwn, RbacPermissions.RoomKick))
            allowed.Add(McpRoleCodes.Player);

        // diy_creator 需要完整 DIY 编辑 + 热重载 + WindBot Lua 权限
        if (HasPermission(actorEffectivePermissions, RbacPermissions.DiyEdit) &&
            HasPermission(actorEffectivePermissions, RbacPermissions.DiyReload) &&
            HasPermission(actorEffectivePermissions, RbacPermissions.WindBotEditLua))
        {
            allowed.Add(McpRoleCodes.DiyCreator);
        }

        // admin MCP 仅允许同时拥有房间管理、DIY 管理与用户/全局管理权限的管理员签发
        if (HasPermission(actorEffectivePermissions, RbacPermissions.RoomCreate) &&
            HasPermission(actorEffectivePermissions, RbacPermissions.RoomKick) &&
            HasPermission(actorEffectivePermissions, RbacPermissions.DiyEdit) &&
            HasPermission(actorEffectivePermissions, RbacPermissions.ServerStop))
        {
            allowed.Add(McpRoleCodes.Admin);
        }

        return allowed;
    }

    public static bool CanIssueMcpRole(
        IReadOnlyCollection<string> actorRoleCodes,
        IReadOnlySet<string> actorEffectivePermissions,
        string requestedMcpRole)
    {
        if (string.IsNullOrWhiteSpace(requestedMcpRole))
            return false;

        IReadOnlyList<string> allowed = GetAllowedMcpRolesToIssue(actorRoleCodes, actorEffectivePermissions);
        return allowed.Any(r => string.Equals(r, requestedMcpRole.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
