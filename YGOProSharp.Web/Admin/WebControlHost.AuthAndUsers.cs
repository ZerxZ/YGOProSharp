using System.Text.Json;
using Microsoft.AspNetCore.Http;
using YGOProSharp.Persistence;
using YGOProSharp.Persistence.Entities;
using YGOProSharp.Persistence.Security;
using YGOProSharp.Web.Routing;
using YGOProSharp.Web.Security;

namespace YGOProSharp.Web;

public sealed partial class WebControlHost
{
    [HttpPost("/api/v1/auth/login")]
    private async Task<IResult> LoginAdminAsync(HttpContext http, LoginRequestDto body, CancellationToken ct)
    {
        string? ip = http.GetRemoteIpNormalized();
        string? ua = http.Request.Headers.UserAgent.ToString();
        LoginResult res = await _store.LoginAsync(body.Username ?? string.Empty, body.Password ?? string.Empty, ip, ua, ct).ConfigureAwait(false);

        if (!res.Success || res.SessionToken is null || res.Principal is null)
        {
            int code = res.StatusCode switch
            {
                "rate_limited" => StatusCodes.Status429TooManyRequests,
                "locked" or "account_locked" or "account_disabled" => StatusCodes.Status423Locked,
                _ => StatusCodes.Status401Unauthorized
            };
            return Results.Json(new
            {
                success = false,
                error = res.StatusCode,
                message = res.Message,
                remainingAttempts = res.RemainingAttempts,
                lockedSeconds = res.LockedSeconds
            }, statusCode: code);
        }

        WebSecurityExtensions.AppendSessionCookie(http, _options.SessionCookieName, res.SessionToken, res.ExpiresAt, _options.RequireHttpsCookie);

        return Results.Ok(new
        {
            success = true,
            message = res.Message,
            accessToken = res.SessionToken,
            csrfToken = res.CsrfToken,
            expiresAt = res.ExpiresAt,
            user = BuildUserSelfDto(res.Principal)
        });
    }

    [HttpPost("/api/v1/auth/logout")]
    private async Task<IResult> LogoutAdminAsync(HttpContext http, CancellationToken ct)
    {
        (string? rawToken, _) = WebSecurityExtensions.ExtractSessionToken(http, _options.SessionCookieName);
        await _store.LogoutSessionAsync(rawToken, http.GetRemoteIpNormalized(), ct).ConfigureAwait(false);
        http.Response.Cookies.Delete(_options.SessionCookieName, new CookieOptions { Path = "/" });
        return Results.Ok(new { success = true });
    }

    [HttpGet("/api/v1/auth/me")]
    [AdminAuth(AllowWhenMustChangePassword = true)]
    private IResult GetAdminMe(AuthenticatedPrincipal principal) =>
        Results.Ok(new { user = BuildUserSelfDto(principal) });

    [HttpPost("/api/v1/auth/change-password")]
    [AdminAuth(AllowWhenMustChangePassword = true)]
    private async Task<IResult> ChangeAdminPasswordAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        ChangePasswordRequestDto body,
        CancellationToken ct)
    {
        (bool ok, string msg) = await _store.ChangeOwnPasswordAsync(
            principal.UserId,
            principal.SessionId,
            body.CurrentPassword ?? string.Empty,
            body.NewPassword,
            http.GetRemoteIpNormalized(),
            ct,
            newUsername: body.NewUsername,
            newDisplayName: body.DisplayName).ConfigureAwait(false);

        if (!ok)
            return Results.BadRequest(new { success = false, message = msg });

        (string? rawToken, _) = WebSecurityExtensions.ExtractSessionToken(http, _options.SessionCookieName);
        AuthenticatedPrincipal? refreshed = await _store.ValidateSessionAsync(rawToken, ct).ConfigureAwait(false);
        return Results.Ok(new
        {
            success = true,
            message = msg,
            user = refreshed is not null ? BuildUserSelfDto(refreshed) : null
        });
    }

    [HttpPost("/api/v1/auth/ws-ticket")]
    [AdminAuth]
    private async Task<IResult> IssueWsTicketAsync(HttpContext http, CancellationToken ct)
    {
        (string? rawSessionToken, _) = WebSecurityExtensions.ExtractSessionToken(http, _options.SessionCookieName);
        string nonce = await _store.Cache.IssueOneTimeTicketAsync("ws", rawSessionToken!, TimeSpan.FromSeconds(45), ct).ConfigureAwait(false);
        return Results.Ok(new { ticket = nonce, expiresInSeconds = 45 });
    }

    [HttpGet("/api/v1/auth/sessions")]
    [AdminAuth]
    private async Task<IResult> ListAdminSessionsAsync(AuthenticatedPrincipal principal, CancellationToken ct)
    {
        IReadOnlyList<PanelSessionEntity> sessions = await _store.ListSessionsAsync(principal.UserId, ct).ConfigureAwait(false);
        return Results.Ok(new
        {
            currentSessionId = principal.SessionId,
            sessions = sessions.Select(s => new
            {
                s.Id,
                s.TokenPrefix,
                ip = principal.IsStreamerOnly ? null : s.Ip,
                s.UserAgent,
                s.CreatedAt,
                s.LastSeenAt,
                s.ExpiresAt,
                isCurrent = s.Id == principal.SessionId
            })
        });
    }

    [HttpDelete("/api/v1/auth/sessions/{id:guid}")]
    [AdminAuth]
    private async Task<IResult> RevokeAdminSessionAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        Guid id,
        CancellationToken ct)
    {
        bool ok = await _store.RevokeSessionByIdAsync(principal, id, http.GetRemoteIpNormalized(), ct).ConfigureAwait(false);
        return ok ? Results.Ok(new { success = true }) : Results.NotFound(new { success = false, message = "会话不存在或无权注销。" });
    }

    [HttpGet("/api/v1/permissions")]
    [AdminAuth(RbacPermissions.DashboardView)]
    private IResult GetPermissionMatrix(AuthenticatedPrincipal principal) =>
        Results.Ok(new
        {
            explicitPermissions = RbacPermissions.AllExplicitPermissions,
            builtInTemplates = RbacPolicyEvaluator.BuiltInRoles,
            allowedMcpRolesToIssue = RbacPolicyEvaluator.GetAllowedMcpRolesToIssue(principal.RoleCodes, principal.EffectivePermissions)
        });

    [HttpGet("/api/v1/roles")]
    [AdminAuth(RbacPermissions.RoleManage, RbacPermissions.UserManage, RbacPermissions.DashboardView)]
    private async Task<IResult> ListRolesAsync(CancellationToken ct)
    {
        IReadOnlyList<PanelRoleEntity> roles = await _store.ListRolesAsync(ct).ConfigureAwait(false);
        return Results.Ok(new
        {
            roles = roles.Select(static r => new
            {
                r.Id,
                r.Code,
                r.Name,
                r.Description,
                r.IsBuiltIn,
                permissions = RbacPolicyEvaluator.ParsePermissionsJson(r.PermissionsJson)
            })
        });
    }

    [HttpPut("/api/v1/roles/{code}")]
    [AdminAuth(RbacPermissions.RoleManage)]
    private async Task<IResult> UpsertRoleAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        string code,
        UpsertRoleRequestDto body,
        CancellationToken ct)
    {
        (bool ok, string msg, PanelRoleEntity? role) = await _store.UpsertRoleAsync(
            principal,
            code,
            body.Name ?? code,
            body.Description ?? string.Empty,
            body.Permissions ?? [],
            http.GetRemoteIpNormalized(),
            ct).ConfigureAwait(false);

        return ok ? Results.Ok(new { success = true, message = msg, role }) : Results.BadRequest(new { success = false, message = msg });
    }

    [HttpGet("/api/v1/users")]
    [AdminAuth(RbacPermissions.UserManage)]
    private async Task<IResult> ListUsersAsync(CancellationToken ct)
    {
        IReadOnlyList<PanelUserEntity> users = await _store.ListUsersAsync(ct).ConfigureAwait(false);
        return Results.Ok(new
        {
            users = users.Select(static u => new
            {
                u.Id,
                u.Username,
                u.DisplayName,
                u.Enabled,
                u.MustChangePassword,
                u.FailedLoginCount,
                u.LockedUntil,
                u.LastLoginAt,
                u.LastLoginIp,
                u.CreatedAt,
                roles = u.UserRoles.Where(static ur => ur.Role is not null).Select(static ur => ur.Role.Code).ToArray(),
                permissionsOverride = RbacPolicyEvaluator.ParseOverrideJson(u.PermissionsOverrideJson)
            })
        });
    }

    [HttpPost("/api/v1/users")]
    [AdminAuth(RbacPermissions.UserManage)]
    private async Task<IResult> CreateUserAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        CreateUserRequestDto body,
        CancellationToken ct)
    {
        UserPermissionsOverride? over = body.GrantPermissions is not null || body.RevokePermissions is not null
            ? new UserPermissionsOverride(body.GrantPermissions ?? [], body.RevokePermissions ?? [])
            : null;

        bool defaultMustChange = string.IsNullOrWhiteSpace(body.InitialPassword);
        (bool ok, string msg, PanelUserEntity? created, string? generatedPw) = await _store.CreateUserAsync(
            principal,
            body.Username ?? string.Empty,
            body.DisplayName ?? string.Empty,
            body.InitialPassword,
            body.RoleCodes ?? [BuiltInRoleCodes.AiOperator],
            over,
            body.MustChangePassword ?? defaultMustChange,
            http.GetRemoteIpNormalized(),
            ct).ConfigureAwait(false);

        if (!ok || created is null)
            return Results.BadRequest(new { success = false, message = msg });

        return Results.Ok(new
        {
            success = true,
            message = msg,
            oneTimePassword = generatedPw,
            user = new
            {
                created.Id,
                created.Username,
                created.DisplayName,
                created.Enabled,
                created.MustChangePassword
            }
        });
    }

    [HttpPatch("/api/v1/users/{id:guid}")]
    [AdminAuth(RbacPermissions.UserManage)]
    private async Task<IResult> UpdateUserAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        Guid id,
        UpdateUserRequestDto body,
        CancellationToken ct)
    {
        bool hasOverride = body.GrantPermissions is not null || body.RevokePermissions is not null;
        UserPermissionsOverride? over = hasOverride
            ? new UserPermissionsOverride(body.GrantPermissions ?? [], body.RevokePermissions ?? [])
            : null;

        (bool ok, string msg) = await _store.UpdateUserAsync(
            principal,
            id,
            body.DisplayName,
            body.Enabled,
            body.UnlockAccount == true,
            body.RoleCodes,
            over,
            hasOverride,
            http.GetRemoteIpNormalized(),
            ct,
            newUsername: body.Username).ConfigureAwait(false);

        return ok ? Results.Ok(new { success = true, message = msg }) : Results.BadRequest(new { success = false, message = msg });
    }

    [HttpPost("/api/v1/users/{id:guid}/reset-password")]
    [AdminAuth(RbacPermissions.UserManage)]
    private async Task<IResult> ResetUserPasswordAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        Guid id,
        ResetUserPasswordRequestDto? body,
        CancellationToken ct)
    {
        (bool ok, string msg, string? tempPw) = await _store.ResetUserPasswordAsync(
            principal,
            id,
            http.GetRemoteIpNormalized(),
            ct,
            customPassword: body?.CustomPassword,
            mustChangePassword: body?.MustChangePassword).ConfigureAwait(false);
        return ok ? Results.Ok(new { success = true, message = msg, oneTimePassword = tempPw }) : Results.BadRequest(new { success = false, message = msg });
    }

    [HttpGet("/api/v1/mcp/tokens")]
    [AdminAuth(RbacPermissions.McpTokenIssueSelf, RbacPermissions.McpTokenManageAll, RbacPermissions.UserManage)]
    private async Task<IResult> ListMcpTokensAsync(AuthenticatedPrincipal principal, CancellationToken ct)
    {
        IReadOnlyList<McpTokenEntity> tokens = await _store.ListMcpTokensAsync(principal, ct).ConfigureAwait(false);
        return Results.Ok(new
        {
            allowedRolesToIssue = RbacPolicyEvaluator.GetAllowedMcpRolesToIssue(principal.RoleCodes, principal.EffectivePermissions),
            tokens = tokens.Select(static t => new
            {
                t.Id,
                t.Name,
                t.PreferredPlayerName,
                t.OwnerUsername,
                t.TokenPrefix,
                t.McpRole,
                allowedRooms = string.IsNullOrWhiteSpace(t.AllowedRoomsJson) ? [] : (JsonSerializer.Deserialize<string[]>(t.AllowedRoomsJson) ?? []),
                t.MaxSessions,
                t.RequireApproval,
                t.ExpiresAt,
                t.RevokedAt,
                t.LastUsedAt,
                t.CreatedAt
            })
        });
    }

    [HttpPost("/api/v1/mcp/tokens")]
    [AdminAuth(RbacPermissions.McpTokenIssueSelf, RbacPermissions.McpTokenManageAll)]
    private async Task<IResult> IssueMcpTokenAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        IssueMcpTokenRequestDto body,
        CancellationToken ct)
    {
        string? preferredName = !string.IsNullOrWhiteSpace(body.PreferredPlayerName)
            ? body.PreferredPlayerName.Trim()
            : (!string.IsNullOrWhiteSpace(body.Name) ? body.Name.Trim() : null);

        (bool ok, string msg, IssuedMcpTokenResult? issued) = await _store.IssueMcpTokenAsync(
            principal,
            body.Name ?? string.Empty,
            body.McpRole ?? McpRoleCodes.Player,
            body.AllowedRooms,
            body.MaxSessions ?? 1,
            body.RequireApproval ?? true,
            body.ExpiresInHours,
            http.GetRemoteIpNormalized(),
            ct,
            preferredPlayerName: preferredName).ConfigureAwait(false);

        if (!ok || issued is null)
            return Results.Json(new { success = false, message = msg }, statusCode: StatusCodes.Status403Forbidden);

        return Results.Ok(new
        {
            success = true,
            message = msg,
            rawToken = issued.RawToken,
            token = new
            {
                issued.Entity.Id,
                issued.Entity.Name,
                issued.Entity.PreferredPlayerName,
                issued.Entity.TokenPrefix,
                issued.Entity.McpRole,
                issued.Entity.MaxSessions,
                issued.Entity.RequireApproval,
                issued.Entity.ExpiresAt
            }
        });
    }

    [HttpPatch("/api/v1/mcp/tokens/{id:guid}")]
    [AdminAuth(RbacPermissions.McpTokenIssueSelf, RbacPermissions.McpTokenManageAll, RbacPermissions.UserManage)]
    private async Task<IResult> UpdateMcpTokenNicknameAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        Guid id,
        UpdateMcpTokenNicknameDto body,
        CancellationToken ct)
    {
        (bool ok, string msg, string? updatedName) = await _store.UpdateMcpTokenNicknameAsync(
            principal,
            id,
            body.PreferredPlayerName,
            http.GetRemoteIpNormalized(),
            ct).ConfigureAwait(false);

        return ok
            ? Results.Ok(new { success = true, message = msg, preferredPlayerName = updatedName })
            : Results.BadRequest(new { success = false, message = msg });
    }

    [HttpDelete("/api/v1/mcp/tokens/{id:guid}")]
    [AdminAuth(RbacPermissions.McpTokenIssueSelf, RbacPermissions.McpTokenManageAll, RbacPermissions.UserManage)]
    private async Task<IResult> RevokeMcpTokenAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        Guid id,
        CancellationToken ct)
    {
        bool ok = await _store.RevokeMcpTokenAsync(principal, id, http.GetRemoteIpNormalized(), ct).ConfigureAwait(false);
        if (ok)
            _coordinator.RevokeMcpTokenSessions(id);
        return ok ? Results.Ok(new { success = true }) : Results.NotFound(new { success = false, message = "Token 不存在或无权吊销。" });
    }

    [HttpGet("/api/v1/audit-logs")]
    [AdminAuth(RbacPermissions.AuditViewAll)]
    private async Task<IResult> QueryAuditLogsAsync(
        int? limit,
        string? actor,
        string? action,
        string? result,
        CancellationToken ct)
    {
        IReadOnlyList<AuditLogEntity> logs = await _store.QueryAuditLogsAsync(limit ?? 100, actor, action, result, ct).ConfigureAwait(false);
        return Results.Ok(new { logs });
    }

    private static object BuildUserSelfDto(AuthenticatedPrincipal p) =>
        new
        {
            p.SessionId,
            p.UserId,
            p.Username,
            p.DisplayName,
            p.MustChangePassword,
            p.CsrfToken,
            p.IsStreamerOnly,
            p.CanViewIp,
            roleCodes = p.RoleCodes,
            permissions = p.EffectivePermissions,
            allowedMcpRolesToIssue = RbacPolicyEvaluator.GetAllowedMcpRolesToIssue(p.RoleCodes, p.EffectivePermissions)
        };
}
