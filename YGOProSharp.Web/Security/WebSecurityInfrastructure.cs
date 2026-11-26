using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using YGOProSharp.Persistence;
using YGOProSharp.Persistence.Security;

namespace YGOProSharp.Web.Security;

public static class WebAuthSchemes
{
    public const string AdminPanel = "YgoAdminPanelSession";
    public const string UserPortal = "YgoUserPortalSession";
}

public static class WebRateLimitPolicies
{
    public const string PortalRoomCreate = "portal_room_create";
    public const string PortalVpass = "portal_vpass";
    public const string PortalTournamentDeck = "portal_tournament_deck";
    public const string PortalDiyProposal = "portal_diy_proposal";
}

public sealed class PanelSessionAuthOptions : AuthenticationSchemeOptions
{
    public string SessionCookieName { get; set; } = "ygp_panel_sid";
}

internal sealed record SessionAuthTicketContext(
    AuthenticatedPrincipal Principal,
    string RawToken,
    bool IsBearer);

/// <summary>
/// 标准 ASP.NET Core <see cref="AuthenticationHandler{TOptions}"/>：
/// 统一解析 <c>Authorization: Bearer</c> 或指定会话 Cookie，校验数据库会话并构造 <see cref="ClaimsPrincipal"/>。
/// </summary>
public sealed class PanelSessionAuthenticationHandler(
    IOptionsMonitor<PanelSessionAuthOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    OperationalStore store)
    : AuthenticationHandler<PanelSessionAuthOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        (string? rawToken, bool isBearer) = WebSecurityExtensions.ExtractSessionToken(Context, Options.SessionCookieName);
        if (string.IsNullOrWhiteSpace(rawToken))
            return AuthenticateResult.NoResult();

        AuthenticatedPrincipal? principal = await store.ValidateSessionAsync(rawToken, Context.RequestAborted).ConfigureAwait(false);
        if (principal is null)
            return AuthenticateResult.Fail("session_expired");

        Context.Items[GetItemKey(Scheme.Name)] = new SessionAuthTicketContext(principal, rawToken, isBearer);

        List<Claim> claims =
        [
            new(ClaimTypes.NameIdentifier, principal.UserId.ToString()),
            new(ClaimTypes.Name, principal.Username),
            new("display_name", principal.DisplayName),
            new("session_id", principal.SessionId.ToString())
        ];
        foreach (string role in principal.RoleCodes)
            claims.Add(new Claim(ClaimTypes.Role, role));
        foreach (string perm in principal.EffectivePermissions)
            claims.Add(new Claim("perm", perm));

        ClaimsIdentity identity = new(claims, Scheme.Name);
        AuthenticationTicket ticket = new(new ClaimsPrincipal(identity), Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }

    internal static string GetItemKey(string schemeName) => $"__ygo_auth_ctx:{schemeName}";
}

/// <summary>
/// Web 控制面与前台玩家门户共享的 ASP.NET Core 认证、CSRF、RBAC 端点过滤器及标准滑动窗口限流扩展。
/// </summary>
public static class WebSecurityExtensions
{
    private const string CurrentPrincipalItemKey = "__ygo_active_principal";

    public static void AddYgoWebSecurityAndRateLimiting(
        this WebApplicationBuilder builder,
        OperationalStore store,
        string adminCookieName = "ygp_panel_sid",
        string portalCookieName = "ygp_portal_sid")
    {
        builder.Services.AddSingleton(store);

        builder.Services
            .AddAuthentication(defaultScheme: WebAuthSchemes.AdminPanel)
            .AddScheme<PanelSessionAuthOptions, PanelSessionAuthenticationHandler>(
                WebAuthSchemes.AdminPanel,
                opts => opts.SessionCookieName = adminCookieName)
            .AddScheme<PanelSessionAuthOptions, PanelSessionAuthenticationHandler>(
                WebAuthSchemes.UserPortal,
                opts => opts.SessionCookieName = portalCookieName);

        builder.Services.AddAuthorization();

        builder.Services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = async (ctx, ct) =>
            {
                ctx.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await ctx.HttpContext.Response.WriteAsJsonAsync(
                    new { error = "rate_limited", message = "操作过于频繁，请稍后再试。" },
                    cancellationToken: ct).ConfigureAwait(false);
            };

            limiter.AddPolicy(WebRateLimitPolicies.PortalRoomCreate, http =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: $"room:{GetRemoteIpNormalized(http) ?? "local"}:{http.Request.Cookies[portalCookieName] ?? http.Request.Headers.Authorization.ToString()}",
                    factory: static _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 8,
                        Window = TimeSpan.FromMinutes(3),
                        SegmentsPerWindow = 3,
                        QueueLimit = 0
                    }));

            limiter.AddPolicy(WebRateLimitPolicies.PortalVpass, http =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: $"vpass:{GetRemoteIpNormalized(http) ?? "local"}",
                    factory: static _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 10,
                        Window = TimeSpan.FromMinutes(5),
                        SegmentsPerWindow = 5,
                        QueueLimit = 0
                    }));

            limiter.AddPolicy(WebRateLimitPolicies.PortalTournamentDeck, http =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: $"tdeck:{GetRemoteIpNormalized(http) ?? "local"}",
                    factory: static _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 8,
                        Window = TimeSpan.FromMinutes(5),
                        SegmentsPerWindow = 5,
                        QueueLimit = 0
                    }));

            limiter.AddPolicy(WebRateLimitPolicies.PortalDiyProposal, http =>
                RateLimitPartition.GetSlidingWindowLimiter(
                    partitionKey: $"prop:{GetRemoteIpNormalized(http) ?? "local"}",
                    factory: static _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 5,
                        Window = TimeSpan.FromMinutes(10),
                        SegmentsPerWindow = 5,
                        QueueLimit = 0
                    }));
        });
    }

    public static void UseYgoWebSecurityAndRateLimiting(this WebApplication app)
    {
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
    }

    public static RouteHandlerBuilder RequireAdminAuth(
        this RouteHandlerBuilder builder,
        params string[] anyOfPermissions) =>
        RequireSchemeAuthCore(builder, WebAuthSchemes.AdminPanel, isPortalSurface: false, allowWhenMustChangePassword: false, anyOfPermissions);

    public static RouteHandlerBuilder RequireAdminAuthAllowMustChange(this RouteHandlerBuilder builder) =>
        RequireSchemeAuthCore(builder, WebAuthSchemes.AdminPanel, isPortalSurface: false, allowWhenMustChangePassword: true, []);

    public static RouteHandlerBuilder RequirePortalAuth(
        this RouteHandlerBuilder builder,
        bool allowWhenMustChangePassword = false) =>
        RequireSchemeAuthCore(builder, WebAuthSchemes.UserPortal, isPortalSurface: true, allowWhenMustChangePassword, []);

    private static RouteHandlerBuilder RequireSchemeAuthCore(
        RouteHandlerBuilder builder,
        string schemeName,
        bool isPortalSurface,
        bool allowWhenMustChangePassword,
        string[] anyOfPermissions)
    {
        return builder.AddEndpointFilter(async (invocationContext, next) =>
        {
            HttpContext http = invocationContext.HttpContext;
            AuthenticateResult authResult = await http.AuthenticateAsync(schemeName).ConfigureAwait(false);
            if (!authResult.Succeeded ||
                !http.Items.TryGetValue(PanelSessionAuthenticationHandler.GetItemKey(schemeName), out object? rawCtx) ||
                rawCtx is not SessionAuthTicketContext ctx)
            {
                bool expired = string.Equals(authResult.Failure?.Message, "session_expired", StringComparison.Ordinal);
                return Results.Json(
                    new
                    {
                        error = expired ? "session_expired" : (isPortalSurface ? "unauthenticated" : "unauthorized"),
                        message = isPortalSurface ? "请先注册或登录玩家账号。" : "请先登录管理面板。"
                    },
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            if (!ctx.IsBearer && IsMutatingMethod(http.Request.Method))
            {
                string csrfHeader = http.Request.Headers["X-CSRF-Token"].ToString();
                if (string.IsNullOrWhiteSpace(csrfHeader) || !TokenCrypto.FixedTimeEqualsString(csrfHeader, ctx.Principal.CsrfToken))
                {
                    return Results.Json(
                        new { error = "csrf_invalid", message = "CSRF 令牌缺失或校验失败。" },
                        statusCode: StatusCodes.Status403Forbidden);
                }
            }

            if (!allowWhenMustChangePassword && ctx.Principal.MustChangePassword)
            {
                return Results.Json(
                    new
                    {
                        error = "must_change_password",
                        message = "出于安全要求，首次登录或重置密码后必须先完成修改密码。"
                    },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            if (anyOfPermissions.Length > 0 && !RbacPolicyEvaluator.HasAnyPermission(ctx.Principal.EffectivePermissions, anyOfPermissions))
            {
                OperationalStore store = http.RequestServices.GetRequiredService<OperationalStore>();
                string joined = string.Join("|", anyOfPermissions);
                await store.AppendAuditAsync(
                    "panel_user",
                    ctx.Principal.UserId.ToString(),
                    ctx.Principal.Username,
                    http.Request.Path,
                    joined,
                    null,
                    GetRemoteIpNormalized(http),
                    "permission_denied",
                    null,
                    http.RequestAborted).ConfigureAwait(false);

                return Results.Json(
                    new { error = "permission_denied", message = $"缺少所需权限：{joined}" },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            http.Items[CurrentPrincipalItemKey] = ctx.Principal;
            return await next(invocationContext).ConfigureAwait(false);
        });
    }

    public static AuthenticatedPrincipal GetRequiredPrincipal(this HttpContext http) =>
        http.Items.TryGetValue(CurrentPrincipalItemKey, out object? p) && p is AuthenticatedPrincipal principal
            ? principal
            : throw new InvalidOperationException("No authenticated principal attached to HttpContext.");

    public static (string? RawToken, bool IsBearer) ExtractSessionToken(HttpContext http, string cookieName)
    {
        string authHeader = http.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return (authHeader["Bearer ".Length..].Trim(), true);

        if (http.Request.Cookies.TryGetValue(cookieName, out string? cookieVal) && !string.IsNullOrWhiteSpace(cookieVal))
            return (cookieVal.Trim(), false);

        return (null, false);
    }

    public static void AppendSessionCookie(HttpContext http, string cookieName, string rawToken, DateTime? expiresAt, bool requireHttps)
    {
        http.Response.Cookies.Append(cookieName, rawToken, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Secure = requireHttps || http.Request.IsHttps,
            Expires = expiresAt,
            Path = "/"
        });
    }

    public static string? GetRemoteIpNormalized(this HttpContext http)
    {
        IPAddress? addr = http.Connection.RemoteIpAddress;
        if (addr is null)
            return null;
        return addr.IsIPv4MappedToIPv6 ? addr.MapToIPv4().ToString() : addr.ToString();
    }

    public static string? ResolveStaticFilesDirectory(string? configuredRoot)
    {
        string[] candidates =
        [
            configuredRoot ?? string.Empty,
            Path.Combine(AppContext.BaseDirectory, "wwwroot"),
            Path.Combine(Directory.GetCurrentDirectory(), "YGOProSharp.Web", "wwwroot"),
            Path.Combine(Directory.GetCurrentDirectory(), "web", "dist"),
            Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")
        ];

        foreach (string candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                continue;
            string full = Path.GetFullPath(candidate);
            if (Directory.Exists(full) && File.Exists(Path.Combine(full, "index.html")))
                return full;
        }

        return null;
    }

    private static bool IsMutatingMethod(string method) =>
        HttpMethods.IsPost(method) ||
        HttpMethods.IsPut(method) ||
        HttpMethods.IsPatch(method) ||
        HttpMethods.IsDelete(method);
}
