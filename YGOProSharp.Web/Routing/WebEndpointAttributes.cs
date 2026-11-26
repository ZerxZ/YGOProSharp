namespace YGOProSharp.Web.Routing;

/// <summary>
/// 声明式 Web 端点路由基类特性（由 <c>YGOProSharp.Generators.WebEndpointMappingGenerator</c> 在编译期生成强类型 Minimal API 映射代码，零运行时反射）。
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public abstract class WebRouteAttribute(string httpMethod, string pattern) : Attribute
{
    public string HttpMethod { get; } = httpMethod;
    public string Pattern { get; } = pattern;

    /// <summary>
    /// 可选路由分组名（如 <c>"PortalCore"</c> 将生成独立的 <c>MapGeneratedPortalCoreEndpoints</c>，供单端口融合宿主按需复用）。
    /// </summary>
    public string? Group { get; init; }
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class HttpGetAttribute(string pattern) : WebRouteAttribute("GET", pattern);

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class HttpPostAttribute(string pattern) : WebRouteAttribute("POST", pattern);

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class HttpPutAttribute(string pattern) : WebRouteAttribute("PUT", pattern);

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class HttpPatchAttribute(string pattern) : WebRouteAttribute("PATCH", pattern);

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class HttpDeleteAttribute(string pattern) : WebRouteAttribute("DELETE", pattern);

/// <summary>
/// 映射全方法/WebSocket 升级端点（生成 <c>app.Map(pattern, ...)</c>）。
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class MapEndpointAttribute(string pattern) : WebRouteAttribute("ANY", pattern);

/// <summary>
/// 声明管理后台会话认证、CSRF 校验与可选 RBAC 权限要求（支持在端点方法签名中直接声明 <c>AuthenticatedPrincipal</c> 参数由生成器自动注入）。
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class AdminAuthAttribute(params string[] anyOfPermissions) : Attribute
{
    public string[] AnyOfPermissions { get; } = anyOfPermissions;
    public bool AllowWhenMustChangePassword { get; init; }
}

/// <summary>
/// 声明前台玩家门户会话认证与 CSRF 校验要求（支持在端点方法签名中直接声明 <c>AuthenticatedPrincipal</c> 参数由生成器自动注入）。
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class PortalAuthAttribute : Attribute
{
    public bool AllowWhenMustChangePassword { get; init; }
}

/// <summary>
/// 声明该端点绑定的 ASP.NET Core <c>RateLimiter</c> 策略名称。
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class RateLimitAttribute(string policyName) : Attribute
{
    public string PolicyName { get; } = policyName;
}
