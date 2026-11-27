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

    public static void UseYgoAntiBypassHardening(this WebApplication app, string configuredBindHost)
    {
        bool isLoopbackBind = IsLoopbackHostName(configuredBindHost);

        app.Use(async (http, next) =>
        {
            // 1. 注入基础安全响应头（禁 iframe 劫持、禁 MIME 嗅探、零 Referer 外泄以防 Token 随外链泄露）
            IHeaderDictionary headers = http.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            headers["Content-Security-Policy"] = "frame-ancestors 'none'; base-uri 'self'; object-src 'none'";

            // 2. 阻断 URL 编码路径穿越与敏感运营数据库/配置文件探测越狱
            string rawTarget = http.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpRequestFeature>()?.RawTarget
                               ?? http.Request.Path.Value
                               ?? string.Empty;
            int queryIdx = rawTarget.IndexOf('?');
            string rawPathOnly = queryIdx >= 0 ? rawTarget[..queryIdx] : rawTarget;
            if (ContainsPathTraversalOrNullByte(rawPathOnly) || IsForbiddenSensitiveFileProbe(http.Request.Path.Value))
            {
                http.Response.StatusCode = StatusCodes.Status403Forbidden;
                await http.Response.WriteAsJsonAsync(
                    new { error = "path_traversal_blocked", message = "非法请求路径已被服务器安全边界拦截。" },
                    http.RequestAborted).ConfigureAwait(false);
                return;
            }

            // 3. 本地回环监听下的 DNS Rebinding 越狱防护（拒绝恶意外部网页伪造域名指向 127.0.0.1 访问控制面）
            if (isLoopbackBind)
            {
                string reqHost = http.Request.Host.Host;
                if (!string.IsNullOrWhiteSpace(reqHost) && !IsLoopbackHostName(reqHost))
                {
                    http.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await http.Response.WriteAsJsonAsync(
                        new { error = "dns_rebinding_blocked", message = "拒绝非回环 Host 头访问本地受保护服务。" },
                        http.RequestAborted).ConfigureAwait(false);
                    return;
                }
            }

            // 4. 阻断跨站 WebSocket 劫持 (CSWSH) 与跨源控制面/门户写操作（不影响标准 /mcp JSON-RPC 跨域宿主）
            string originHeader = http.Request.Headers.Origin.ToString();
            if (!string.IsNullOrWhiteSpace(originHeader))
            {
                PathString reqPath = http.Request.Path;
                bool isProtectedWs = reqPath.StartsWithSegments("/ws");
                bool isProtectedApiMutation = IsMutatingMethod(http.Request.Method) &&
                                              (reqPath.StartsWithSegments("/api/v1") || reqPath.StartsWithSegments("/api/portal"));
                if ((isProtectedWs || isProtectedApiMutation) && !IsSameOriginAuthority(originHeader, http.Request.Host))
                {
                    http.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await http.Response.WriteAsJsonAsync(
                        new { error = "cross_origin_blocked", message = "拒绝跨源请求访问受保护的控制面或实时通道。" },
                        http.RequestAborted).ConfigureAwait(false);
                    return;
                }
            }

            await next().ConfigureAwait(false);
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

    public static RouteHandlerBuilder RequireAdminAuth(
        this RouteHandlerBuilder builder,
        bool allowWhenMustChangePassword,
        params string[] anyOfPermissions) =>
        RequireSchemeAuthCore(builder, WebAuthSchemes.AdminPanel, isPortalSurface: false, allowWhenMustChangePassword, anyOfPermissions);

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

            if (!isPortalSurface && ctx.Principal.IsPortalSessionOnly)
            {
                OperationalStore store = http.RequestServices.GetRequiredService<OperationalStore>();
                await store.AppendAuditAsync(
                    "panel_user",
                    ctx.Principal.UserId.ToString(),
                    ctx.Principal.Username,
                    http.Request.Path,
                    "admin_surface_isolation",
                    null,
                    GetRemoteIpNormalized(http),
                    "cross_surface_token_rejected",
                    null,
                    http.RequestAborted).ConfigureAwait(false);

                return Results.Json(
                    new
                    {
                        error = "cross_surface_token_rejected",
                        message = "前台玩家门户会话令牌禁止跨端访问管理后台接口，请通过管理后台重新登录。"
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

    private static bool IsLoopbackHostName(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
            return true;

        string trimmed = host.Trim().Trim('[', ']');
        if (string.Equals(trimmed, "localhost", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmed, "127.0.0.1", StringComparison.Ordinal) ||
            string.Equals(trimmed, "::1", StringComparison.Ordinal))
        {
            return true;
        }

        return IPAddress.TryParse(trimmed, out IPAddress? ip) && IPAddress.IsLoopback(ip);
    }

    private static bool IsSameOriginAuthority(string originHeader, HostString requestHost)
    {
        if (!requestHost.HasValue)
            return true;

        if (!Uri.TryCreate(originHeader.Trim(), UriKind.Absolute, out Uri? originUri))
            return false;

        if (!string.Equals(originUri.Host, requestHost.Host, StringComparison.OrdinalIgnoreCase))
            return false;

        if (requestHost.Port.HasValue && !originUri.IsDefaultPort && originUri.Port != requestHost.Port.Value)
            return false;

        return true;
    }

    private static bool ContainsPathTraversalOrNullByte(string rawPath)
    {
        if (string.IsNullOrEmpty(rawPath))
            return false;

        if (rawPath.Contains('\\') ||
            rawPath.Contains('\0') ||
            rawPath.Contains("%00", StringComparison.OrdinalIgnoreCase) ||
            rawPath.Contains("%2e", StringComparison.OrdinalIgnoreCase) ||
            rawPath.Contains("%5c", StringComparison.OrdinalIgnoreCase) ||
            rawPath.Contains("%2f", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string[] segments = rawPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (string seg in segments)
        {
            if (seg == "..")
                return true;
        }

        return false;
    }

    private static bool IsForbiddenSensitiveFileProbe(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            return false;

        string lower = path.ToLowerInvariant();
        return lower.EndsWith(".db", StringComparison.Ordinal) ||
               lower.EndsWith(".db-wal", StringComparison.Ordinal) ||
               lower.EndsWith(".db-shm", StringComparison.Ordinal) ||
               lower.EndsWith(".sqlite", StringComparison.Ordinal) ||
               lower.EndsWith(".sqlite3", StringComparison.Ordinal) ||
               lower.EndsWith(".cdb", StringComparison.Ordinal) ||
               lower.EndsWith(".env", StringComparison.Ordinal) ||
               lower.Contains("/.git/", StringComparison.Ordinal) ||
               lower.EndsWith("/.git", StringComparison.Ordinal);
    }
}
