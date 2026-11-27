using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using YGOProSharp.Core;
using YGOProSharp.Persistence;
using YGOProSharp.Persistence.Entities;
using YGOProSharp.Persistence.Security;
using YGOProSharp.Server;
using YGOProSharp.Web.Routing;
using YGOProSharp.Web.Security;

namespace YGOProSharp.Web;

public sealed partial class UserPortalHost
{
    [HttpGet("/api/portal/overview", Group = "PortalCore")]
    private IResult GetPortalOverview()
    {
        CoreServer srv = _serverHost.Server;
        IReadOnlyList<DiyPackageManifest> enabledDiy = _diyManager.ListPackages(enabledOnly: true);

        return Results.Ok(new
        {
            uptimeSeconds = (long)(DateTime.UtcNow - _startedAtUtc).TotalSeconds,
            isRunning = srv.IsRunning,
            maintenanceMode = srv.IsMaintenanceMode,
            maintenanceReason = srv.MaintenanceReason,
            gamePort = srv.Options.Port,
            publicGameHost = _options.PublicGameHost,
            multiRoom = srv.IsMultiRoom,
            clientCount = srv.ClientCount,
            roomCount = srv.RoomCount,
            catalogVersion = srv.ReloadableCards.CatalogVersion,
            presetDeckCount = srv.PresetDecks.Count,
            enabledDiyPackageCount = enabledDiy.Count,
            banlists = BanlistManager.Banlists.Select(static (b, idx) => new { index = idx, b.Name, b.WhitelistOnly }).ToArray(),
            ruleGuides = new object[]
            {
                new { prefix = "M#房间名", title = "三局两胜比赛 (Match)", desc = "标准 BO3 备牌局" },
                new { prefix = "2V2#房间名", title = "双打模式 (Tag Duel)", desc = "4 人 16000 LP 组队轮转，大厅可输入 /ai fill 召唤 AI 队友" },
                new { prefix = "RD#房间名", title = "随机盲盒对决", desc = "无需组卡直接准备，服务端从盲盒池随机派发主题卡组" },
                new { prefix = "RDM#房间名", title = "镜像盲盒对决", desc = "双方（或 2v2 两队）使用完全对称的随机卡组同台竞技" },
                new { prefix = "CHAOS#房间名", title = "平衡混沌抽卡", desc = "按黄金怪魔陷配比从全卡库实时生成 40+15 随机卡组" },
                new { prefix = "DIYRD#房间名", title = "DIY 主题随机", desc = "优先抽取含本服 DIY 新卡的主题预组" },
                new { prefix = "R#录像编号", title = "云录像直连回放", desc = "在客户端房间密码框输入 R#1 即可免下载直接观看历史回放" }
            }
        });
    }

    [HttpPost("/api/portal/auth/register", Group = "PortalCore")]
    private async Task<IResult> RegisterPortalAccountAsync(
        HttpContext http,
        PortalRegisterRequestDto body,
        CancellationToken ct)
    {
        string? ip = http.GetRemoteIpNormalized();
        string? ua = http.Request.Headers.UserAgent.ToString();
        LoginResult res = await _store.RegisterPlayerAccountAsync(
            body.Username ?? string.Empty,
            body.Password ?? string.Empty,
            body.DisplayName,
            ip,
            ua,
            ct).ConfigureAwait(false);

        if (!res.Success || res.SessionToken is null || res.Principal is null)
        {
            int status = res.StatusCode == "rate_limited" ? StatusCodes.Status429TooManyRequests : StatusCodes.Status400BadRequest;
            return Results.Json(new { success = false, error = res.StatusCode, message = res.Message }, statusCode: status);
        }

        WebSecurityExtensions.AppendSessionCookie(http, _options.SessionCookieName, res.SessionToken, res.ExpiresAt, _options.RequireHttpsCookie);
        return Results.Ok(new
        {
            success = true,
            message = res.Message,
            accessToken = res.SessionToken,
            csrfToken = res.CsrfToken,
            expiresAt = res.ExpiresAt,
            user = BuildPortalUserDto(res.Principal)
        });
    }

    [HttpPost("/api/portal/auth/login", Group = "PortalCore")]
    private async Task<IResult> LoginPortalAccountAsync(
        HttpContext http,
        LoginRequestDto body,
        CancellationToken ct)
    {
        string? ip = http.GetRemoteIpNormalized();
        string? ua = http.Request.Headers.UserAgent.ToString();
        LoginResult res = await _store.LoginAsync(
            body.Username ?? string.Empty,
            body.Password ?? string.Empty,
            ip,
            ua,
            ct,
            tokenPrefixTag: "ygp_usr_sess").ConfigureAwait(false);

        if (!res.Success || res.SessionToken is null || res.Principal is null)
        {
            int code = res.StatusCode switch
            {
                "rate_limited" => StatusCodes.Status429TooManyRequests,
                "locked" or "account_locked" or "account_disabled" => StatusCodes.Status423Locked,
                _ => StatusCodes.Status401Unauthorized
            };
            return Results.Json(new { success = false, error = res.StatusCode, message = res.Message }, statusCode: code);
        }

        WebSecurityExtensions.AppendSessionCookie(http, _options.SessionCookieName, res.SessionToken, res.ExpiresAt, _options.RequireHttpsCookie);
        return Results.Ok(new
        {
            success = true,
            message = res.Message,
            accessToken = res.SessionToken,
            csrfToken = res.CsrfToken,
            expiresAt = res.ExpiresAt,
            user = BuildPortalUserDto(res.Principal)
        });
    }

    [HttpPost("/api/portal/auth/logout", Group = "PortalCore")]
    private async Task<IResult> LogoutPortalAccountAsync(HttpContext http, CancellationToken ct)
    {
        (string? rawToken, _) = WebSecurityExtensions.ExtractSessionToken(http, _options.SessionCookieName);
        await _store.LogoutSessionAsync(rawToken, http.GetRemoteIpNormalized(), ct).ConfigureAwait(false);
        http.Response.Cookies.Delete(_options.SessionCookieName, new CookieOptions { Path = "/" });
        return Results.Ok(new { success = true });
    }

    [HttpGet("/api/portal/auth/me", Group = "PortalCore")]
    private async Task<IResult> GetPortalMeAsync(HttpContext http, CancellationToken ct)
    {
        (string? rawToken, _) = WebSecurityExtensions.ExtractSessionToken(http, _options.SessionCookieName);
        if (string.IsNullOrWhiteSpace(rawToken))
            return Results.Ok(new { authenticated = false, user = (object?)null });

        AuthenticatedPrincipal? principal = await _store.ValidateSessionAsync(rawToken, ct).ConfigureAwait(false);
        if (principal is null)
            return Results.Ok(new { authenticated = false, user = (object?)null });

        return Results.Ok(new { authenticated = true, user = BuildPortalUserDto(principal) });
    }

    [HttpPost("/api/portal/auth/change-password", Group = "PortalCore")]
    [PortalAuth(AllowWhenMustChangePassword = true)]
    private async Task<IResult> ChangePortalPasswordAsync(
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
        return Results.Ok(new { success = true, message = msg, user = refreshed is not null ? BuildPortalUserDto(refreshed) : null });
    }

    [HttpGet("/api/portal/mcp/tokens", Group = "PortalCore")]
    [PortalAuth]
    private async Task<IResult> ListPortalMcpTokensAsync(AuthenticatedPrincipal principal, CancellationToken ct)
    {
        await using OperationalDbContext db = _store.Database.CreateDbContext();
        List<McpTokenEntity> owned = await db.McpTokens
            .Where(t => t.OwnerUserId == principal.UserId)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        string[] allowedRoles = RbacPolicyEvaluator.GetAllowedMcpRolesToIssue(principal.RoleCodes, principal.EffectivePermissions)
            .Where(static r => r is McpRoleCodes.Player or McpRoleCodes.Spectator)
            .ToArray();

        return Results.Ok(new
        {
            allowedRolesToIssue = allowedRoles,
            mcpPortHint = _options.McpPortHint,
            tokens = owned.Select(static t => new
            {
                t.Id,
                t.Name,
                t.PreferredPlayerName,
                t.PersonaStyle,
                t.TokenPrefix,
                t.McpRole,
                allowedRooms = string.IsNullOrWhiteSpace(t.AllowedRoomsJson) ? [] : (JsonSerializer.Deserialize<string[]>(t.AllowedRoomsJson) ?? []),
                t.MaxSessions,
                t.ExpiresAt,
                t.RevokedAt,
                t.CreatedAt
            })
        });
    }

    [HttpPost("/api/portal/mcp/tokens", Group = "PortalCore")]
    [PortalAuth]
    private async Task<IResult> IssuePortalMcpTokenAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        IssueMcpTokenRequestDto body,
        CancellationToken ct)
    {
        string requestedRole = string.IsNullOrWhiteSpace(body.McpRole) ? McpRoleCodes.Player : body.McpRole.Trim().ToLowerInvariant();
        if (requestedRole is not (McpRoleCodes.Player or McpRoleCodes.Spectator))
            return Results.Json(new { message = "前台玩家面板仅允许签发 player（对战 AI）或 spectator（观战 AI）令牌。" }, statusCode: StatusCodes.Status403Forbidden);

        string? preferredName = !string.IsNullOrWhiteSpace(body.PreferredPlayerName)
            ? body.PreferredPlayerName.Trim()
            : (!string.IsNullOrWhiteSpace(body.Name) ? body.Name.Trim() : null);

        (bool ok, string msg, IssuedMcpTokenResult? issued) = await _store.IssueMcpTokenAsync(
            principal,
            body.Name ?? string.Empty,
            requestedRole,
            body.AllowedRooms,
            Math.Clamp(body.MaxSessions ?? 1, 1, 4),
            requireApproval: true,
            expiresInHours: Math.Clamp(body.ExpiresInHours ?? 48, 1, 720),
            ip: http.GetRemoteIpNormalized(),
            cancellationToken: ct,
            preferredPlayerName: preferredName).ConfigureAwait(false);

        if (!ok || issued is null)
            return Results.BadRequest(new { success = false, message = msg });

        if (!string.IsNullOrWhiteSpace(body.PersonaStyle))
            await _store.BindMcpTokenProfileAsync(issued.Entity.Id, null, body.PersonaStyle, ct).ConfigureAwait(false);

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
                personaStyle = body.PersonaStyle?.Trim(),
                issued.Entity.McpRole,
                issued.Entity.TokenPrefix,
                issued.Entity.ExpiresAt
            }
        });
    }

    [HttpPatch("/api/portal/mcp/tokens/{id:guid}", Group = "PortalCore")]
    [PortalAuth]
    private async Task<IResult> UpdatePortalMcpTokenAsync(
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

        if (!ok)
            return Results.BadRequest(new { success = false, message = msg });

        string? updatedPersona = null;
        if (body.PersonaStyle is not null)
            (_, _, _, updatedPersona) = await _store.BindMcpTokenProfileAsync(id, null, body.PersonaStyle, ct).ConfigureAwait(false);

        return Results.Ok(new { success = true, message = msg, preferredPlayerName = updatedName, personaStyle = updatedPersona });
    }

    [HttpDelete("/api/portal/mcp/tokens/{id:guid}", Group = "PortalCore")]
    [PortalAuth]
    private async Task<IResult> RevokePortalMcpTokenAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        Guid id,
        CancellationToken ct)
    {
        bool revoked = await _store.RevokeMcpTokenAsync(principal, id, http.GetRemoteIpNormalized(), ct).ConfigureAwait(false);
        if (revoked)
            _coordinator.RevokeMcpTokenSessions(id);
        return revoked ? Results.Ok(new { success = true }) : Results.NotFound(new { message = "未找到属于您的有效 AI Token。" });
    }

    private static object BuildPortalUserDto(AuthenticatedPrincipal p) =>
        new
        {
            p.UserId,
            p.Username,
            p.DisplayName,
            p.MustChangePassword,
            p.CsrfToken,
            roleCodes = p.RoleCodes,
            allowedMcpRolesToIssue = RbacPolicyEvaluator.GetAllowedMcpRolesToIssue(p.RoleCodes, p.EffectivePermissions)
                .Where(static r => r is McpRoleCodes.Player or McpRoleCodes.Spectator)
                .ToArray()
        };
}
