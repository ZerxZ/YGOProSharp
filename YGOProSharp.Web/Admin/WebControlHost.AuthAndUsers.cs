using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using YGOProSharp.Persistence;
using YGOProSharp.Persistence.Entities;
using YGOProSharp.Persistence.Security;
using YGOProSharp.Web.Security;

namespace YGOProSharp.Web;

public sealed partial class WebControlHost
{
    private void MapAdminAuthAndUserEndpoints(WebApplication app)
    {
        app.MapPost("/api/v1/auth/login", async (HttpContext http, LoginRequestDto body, CancellationToken ct) =>
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
        });

        app.MapPost("/api/v1/auth/logout", async (HttpContext http, CancellationToken ct) =>
        {
            (string? rawToken, _) = WebSecurityExtensions.ExtractSessionToken(http, _options.SessionCookieName);
            await _store.LogoutSessionAsync(rawToken, http.GetRemoteIpNormalized(), ct).ConfigureAwait(false);
            http.Response.Cookies.Delete(_options.SessionCookieName, new CookieOptions { Path = "/" });
            return Results.Ok(new { success = true });
        });

        app.MapGet("/api/v1/auth/me", (HttpContext http) =>
        {
            AuthenticatedPrincipal principal = http.GetRequiredPrincipal();
            return Results.Ok(new { user = BuildUserSelfDto(principal) });
        }).RequireAdminAuthAllowMustChange();

        app.MapPost("/api/v1/auth/change-password", async (HttpContext http, ChangePasswordRequestDto body, CancellationToken ct) =>
        {
            AuthenticatedPrincipal principal = http.GetRequiredPrincipal();
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
        }).RequireAdminAuthAllowMustChange();

        app.MapPost("/api/v1/auth/ws-ticket", async (HttpContext http, CancellationToken ct) =>
        {
            _ = http.GetRequiredPrincipal();
            (string? rawSessionToken, _) = WebSecurityExtensions.ExtractSessionToken(http, _options.SessionCookieName);
            string nonce = await _store.Cache.IssueOneTimeTicketAsync("ws", rawSessionToken!, TimeSpan.FromSeconds(45), ct).ConfigureAwait(false);
            return Results.Ok(new { ticket = nonce, expiresInSeconds = 45 });
        }).RequireAdminAuth();

        app.MapGet("/api/v1/auth/sessions", async (HttpContext http, CancellationToken ct) =>
        {
            AuthenticatedPrincipal principal = http.GetRequiredPrincipal();
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
        }).RequireAdminAuth();

        app.MapDelete("/api/v1/auth/sessions/{id:guid}", async (HttpContext http, Guid id, CancellationToken ct) =>
        {
            AuthenticatedPrincipal principal = http.GetRequiredPrincipal();
            bool ok = await _store.RevokeSessionByIdAsync(principal, id, http.GetRemoteIpNormalized(), ct).ConfigureAwait(false);
            return ok ? Results.Ok(new { success = true }) : Results.NotFound(new { success = false, message = "会话不存在或无权注销。" });
        }).RequireAdminAuth();

        app.MapGet("/api/v1/permissions", (HttpContext http) =>
        {
            AuthenticatedPrincipal principal = http.GetRequiredPrincipal();
            return Results.Ok(new
            {
                explicitPermissions = RbacPermissions.AllExplicitPermissions,
                builtInTemplates = RbacPolicyEvaluator.BuiltInRoles,
                allowedMcpRolesToIssue = RbacPolicyEvaluator.GetAllowedMcpRolesToIssue(principal.RoleCodes, principal.EffectivePermissions)
            });
        }).RequireAdminAuth(RbacPermissions.DashboardView);

        app.MapGet("/api/v1/roles", async (CancellationToken ct) =>
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
        }).RequireAdminAuth(RbacPermissions.RoleManage, RbacPermissions.UserManage, RbacPermissions.DashboardView);

        app.MapPut("/api/v1/roles/{code}", async (HttpContext http, string code, UpsertRoleRequestDto body, CancellationToken ct) =>
        {
            AuthenticatedPrincipal principal = http.GetRequiredPrincipal();
            (bool ok, string msg, PanelRoleEntity? role) = await _store.UpsertRoleAsync(
                principal,
                code,
                body.Name ?? code,
                body.Description ?? string.Empty,
                body.Permissions ?? [],
                http.GetRemoteIpNormalized(),
                ct).ConfigureAwait(false);

            return ok ? Results.Ok(new { success = true, message = msg, role }) : Results.BadRequest(new { success = false, message = msg });
        }).RequireAdminAuth(RbacPermissions.RoleManage);

        app.MapGet("/api/v1/users", async (CancellationToken ct) =>
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
        }).RequireAdminAuth(RbacPermissions.UserManage);

        app.MapPost("/api/v1/users", async (HttpContext http, CreateUserRequestDto body, CancellationToken ct) =>
        {
            AuthenticatedPrincipal principal = http.GetRequiredPrincipal();
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
        }).RequireAdminAuth(RbacPermissions.UserManage);

        app.MapPatch("/api/v1/users/{id:guid}", async (HttpContext http, Guid id, UpdateUserRequestDto body, CancellationToken ct) =>
        {
            AuthenticatedPrincipal principal = http.GetRequiredPrincipal();
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
        }).RequireAdminAuth(RbacPermissions.UserManage);

        app.MapPost("/api/v1/users/{id:guid}/reset-password", async (HttpContext http, Guid id, ResetUserPasswordRequestDto? body, CancellationToken ct) =>
        {
            AuthenticatedPrincipal principal = http.GetRequiredPrincipal();
            (bool ok, string msg, string? tempPw) = await _store.ResetUserPasswordAsync(
                principal,
                id,
                http.GetRemoteIpNormalized(),
                ct,
                customPassword: body?.CustomPassword,
                mustChangePassword: body?.MustChangePassword).ConfigureAwait(false);
            return ok ? Results.Ok(new { success = true, message = msg, oneTimePassword = tempPw }) : Results.BadRequest(new { success = false, message = msg });
        }).RequireAdminAuth(RbacPermissions.UserManage);

        app.MapGet("/api/v1/mcp/tokens", async (HttpContext http, CancellationToken ct) =>
        {
            AuthenticatedPrincipal principal = http.GetRequiredPrincipal();
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
        }).RequireAdminAuth(RbacPermissions.McpTokenIssueSelf, RbacPermissions.McpTokenManageAll, RbacPermissions.UserManage);

        app.MapPost("/api/v1/mcp/tokens", async (HttpContext http, IssueMcpTokenRequestDto body, CancellationToken ct) =>
        {
            AuthenticatedPrincipal principal = http.GetRequiredPrincipal();
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
        }).RequireAdminAuth(RbacPermissions.McpTokenIssueSelf, RbacPermissions.McpTokenManageAll);

        app.MapPatch("/api/v1/mcp/tokens/{id:guid}", async (HttpContext http, Guid id, UpdateMcpTokenNicknameDto body, CancellationToken ct) =>
        {
            AuthenticatedPrincipal principal = http.GetRequiredPrincipal();
            (bool ok, string msg, string? updatedName) = await _store.UpdateMcpTokenNicknameAsync(
                principal,
                id,
                body.PreferredPlayerName,
                http.GetRemoteIpNormalized(),
                ct).ConfigureAwait(false);

            return ok
                ? Results.Ok(new { success = true, message = msg, preferredPlayerName = updatedName })
                : Results.BadRequest(new { success = false, message = msg });
        }).RequireAdminAuth(RbacPermissions.McpTokenIssueSelf, RbacPermissions.McpTokenManageAll, RbacPermissions.UserManage);

        app.MapDelete("/api/v1/mcp/tokens/{id:guid}", async (HttpContext http, Guid id, CancellationToken ct) =>
        {
            AuthenticatedPrincipal principal = http.GetRequiredPrincipal();
            bool ok = await _store.RevokeMcpTokenAsync(principal, id, http.GetRemoteIpNormalized(), ct).ConfigureAwait(false);
            if (ok)
                _coordinator.RevokeMcpTokenSessions(id);
            return ok ? Results.Ok(new { success = true }) : Results.NotFound(new { success = false, message = "Token 不存在或无权吊销。" });
        }).RequireAdminAuth(RbacPermissions.McpTokenIssueSelf, RbacPermissions.McpTokenManageAll, RbacPermissions.UserManage);

        app.MapGet("/api/v1/audit-logs", async (int? limit, string? actor, string? action, string? result, CancellationToken ct) =>
        {
            IReadOnlyList<AuditLogEntity> logs = await _store.QueryAuditLogsAsync(limit ?? 100, actor, action, result, ct).ConfigureAwait(false);
            return Results.Ok(new { logs });
        }).RequireAdminAuth(RbacPermissions.AuditViewAll);
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
