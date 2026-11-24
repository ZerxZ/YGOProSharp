using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using YGOProSharp.Persistence;
using YGOProSharp.Persistence.Security;

namespace YGOProSharp.Mcp;

/// <summary>
/// ASP.NET Core Streamable HTTP MCP 路由挂载器（支持 <c>POST /mcp</c>、<c>GET /mcp</c>、<c>DELETE /mcp</c>）。
/// 兼容标准 <c>Authorization: Bearer &lt;token&gt;</c> 请求头与无 Header 框客户端的 <c>?key=&lt;token&gt;</c> 查询参数（日志全脱敏）。
/// </summary>
public static class McpHttpEndpoints
{
    private static readonly ConcurrentDictionary<string, Guid> ActiveHttpSessions = new(StringComparer.Ordinal);

    public static void MapMcpStreamableHttp(
        this IEndpointRouteBuilder endpoints,
        McpServerEngine engine,
        OperationalStore store,
        IReadOnlyCollection<string>? allowedSurfaceRoles = null,
        string routePattern = "/mcp")
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(store);

        // CORS 预检支持（供桌面/Web MCP 宿主连接）
        endpoints.MapMethods(routePattern, ["OPTIONS"], (HttpContext http) =>
        {
            ApplyMcpCorsHeaders(http);
            http.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });

        // Streamable HTTP 核心 JSON-RPC POST 入口
        endpoints.MapPost(routePattern, async (HttpContext http, CancellationToken ct) =>
        {
            ApplyMcpCorsHeaders(http);
            (ValidatedMcpToken? token, IResult? authErr) = await AuthenticateMcpRequestAsync(http, store, allowedSurfaceRoles, ct).ConfigureAwait(false);
            if (authErr is not null)
                return authErr;

            string sessionId = http.Request.Headers["Mcp-Session-Id"].ToString();
            if (string.IsNullOrWhiteSpace(sessionId))
                sessionId = $"mcp_{Guid.NewGuid():N}";
            else if (ActiveHttpSessions.TryGetValue(sessionId, out Guid boundTokenId) && boundTokenId != token!.TokenId)
                return Results.Json(McpServerEngine.BuildJsonRpcError(null, -32001, "Mcp-Session-Id belongs to another token."), statusCode: StatusCodes.Status403Forbidden);

            ActiveHttpSessions[sessionId] = token!.TokenId;
            http.Response.Headers["Mcp-Session-Id"] = sessionId;

            JsonNode? requestNode;
            try
            {
                requestNode = await JsonNode.ParseAsync(http.Request.Body, cancellationToken: ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return Results.Json(McpServerEngine.BuildJsonRpcError(null, -32700, $"Parse error: {ex.Message}"), statusCode: StatusCodes.Status400BadRequest);
            }

            JsonNode? responseNode = await engine.ProcessPayloadAsync(requestNode, token, GetRemoteIp(http), ct).ConfigureAwait(false);
            if (responseNode is null)
            {
                // Notification（如 notifications/initialized）按 Streamable HTTP 规范返回 202 Accepted
                http.Response.StatusCode = StatusCodes.Status202Accepted;
                return Results.Empty;
            }

            http.Response.ContentType = "application/json; charset=utf-8";
            await http.Response.WriteAsync(responseNode.ToJsonString(), Encoding.UTF8, ct).ConfigureAwait(false);
            return Results.Empty;
        });

        // Streamable HTTP 可选 GET SSE 通知流（保持心跳，兼容开启长连监听的 MCP 客户端）
        endpoints.MapGet(routePattern, async (HttpContext http, CancellationToken ct) =>
        {
            ApplyMcpCorsHeaders(http);
            (ValidatedMcpToken? token, IResult? authErr) = await AuthenticateMcpRequestAsync(http, store, allowedSurfaceRoles, ct).ConfigureAwait(false);
            if (authErr is not null)
            {
                await authErr.ExecuteAsync(http).ConfigureAwait(false);
                return;
            }

            string sessionId = http.Request.Headers["Mcp-Session-Id"].ToString();
            if (string.IsNullOrWhiteSpace(sessionId))
                sessionId = $"mcp_{Guid.NewGuid():N}";
            ActiveHttpSessions[sessionId] = token!.TokenId;

            http.Response.Headers["Mcp-Session-Id"] = sessionId;
            http.Response.Headers.CacheControl = "no-cache";
            http.Response.Headers["X-Accel-Buffering"] = "no";
            http.Response.ContentType = "text/event-stream; charset=utf-8";

            await http.Response.WriteAsync(": ygoprosharp-mcp stream ready\n\n", ct).ConfigureAwait(false);
            await http.Response.Body.FlushAsync(ct).ConfigureAwait(false);

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
                    await http.Response.WriteAsync(": keepalive\n\n", ct).ConfigureAwait(false);
                    await http.Response.Body.FlushAsync(ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
            }
        });

        // Streamable HTTP 会话显式释放
        endpoints.MapDelete(routePattern, async (HttpContext http, CancellationToken ct) =>
        {
            ApplyMcpCorsHeaders(http);
            (ValidatedMcpToken? token, IResult? authErr) = await AuthenticateMcpRequestAsync(http, store, allowedSurfaceRoles, ct).ConfigureAwait(false);
            if (authErr is not null)
                return authErr;

            string sessionId = http.Request.Headers["Mcp-Session-Id"].ToString();
            if (!string.IsNullOrWhiteSpace(sessionId) &&
                ActiveHttpSessions.TryGetValue(sessionId, out Guid ownerId) &&
                ownerId == token!.TokenId)
            {
                ActiveHttpSessions.TryRemove(sessionId, out _);
            }

            return Results.Ok(new { closed = true });
        });
    }

    private static async Task<(ValidatedMcpToken? Token, IResult? Error)> AuthenticateMcpRequestAsync(
        HttpContext http,
        OperationalStore store,
        IReadOnlyCollection<string>? allowedSurfaceRoles,
        CancellationToken ct)
    {
        string? rawToken = ExtractMcpToken(http);
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return (null, Results.Json(
                McpServerEngine.BuildJsonRpcError(null, -32001, "Missing MCP token. Pass 'Authorization: Bearer <token>' header or '?key=<token>' query parameter."),
                statusCode: StatusCodes.Status401Unauthorized));
        }

        string ip = GetRemoteIp(http) ?? "unknown";
        var rl = await store.Cache.CheckSlidingWindowAsync($"mcp_http_rl:{ip}", 240, TimeSpan.FromMinutes(1), ct).ConfigureAwait(false);
        if (!rl.Allowed)
        {
            return (null, Results.Json(
                McpServerEngine.BuildJsonRpcError(null, -32002, "Rate limit exceeded on /mcp endpoint."),
                statusCode: StatusCodes.Status429TooManyRequests));
        }

        ValidatedMcpToken? validated = await store.ValidateMcpTokenAsync(rawToken, ct).ConfigureAwait(false);
        if (validated is null)
        {
            string masked = MaskToken(rawToken);
            await store.AppendAuditAsync("mcp_token", null, masked, "mcp.auth", null, null, ip, "invalid_token", null, ct).ConfigureAwait(false);
            return (null, Results.Json(
                McpServerEngine.BuildJsonRpcError(null, -32001, "Invalid, expired, or revoked MCP token."),
                statusCode: StatusCodes.Status401Unauthorized));
        }

        if (allowedSurfaceRoles is { Count: > 0 } &&
            !allowedSurfaceRoles.Contains(validated.McpRole, StringComparer.OrdinalIgnoreCase))
        {
            return (null, Results.Json(
                McpServerEngine.BuildJsonRpcError(null, -32003, $"Role '{validated.McpRole}' is not permitted on this port surface (allowed: {string.Join(", ", allowedSurfaceRoles)}). Use the Admin control port instead."),
                statusCode: StatusCodes.Status403Forbidden));
        }

        return (validated, null);
    }

    public static string? ExtractMcpToken(HttpContext http)
    {
        string auth = http.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(auth) && auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return auth["Bearer ".Length..].Trim();

        string headerToken = http.Request.Headers["X-MCP-Token"].ToString();
        if (!string.IsNullOrWhiteSpace(headerToken))
            return headerToken.Trim();

        string apiKeyHeader = http.Request.Headers["X-API-Key"].ToString();
        if (!string.IsNullOrWhiteSpace(apiKeyHeader))
            return apiKeyHeader.Trim();

        // 兼容不提供自定义 HTTP Header 输入框的远端 Streamable HTTP MCP 宿主（如 ?key=ygp_mcp_... 或 ?token=...）
        if (http.Request.Query.TryGetValue("key", out var keyVal) && !string.IsNullOrWhiteSpace(keyVal))
            return keyVal.ToString().Trim();

        if (http.Request.Query.TryGetValue("token", out var tokenVal) && !string.IsNullOrWhiteSpace(tokenVal))
            return tokenVal.ToString().Trim();

        return null;
    }

    public static string MaskToken(string? rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            return "<empty>";
        string trimmed = rawToken.Trim();
        return trimmed.Length <= 12 ? "****" : $"{trimmed[..12]}****";
    }

    private static void ApplyMcpCorsHeaders(HttpContext http)
    {
        http.Response.Headers["Access-Control-Allow-Origin"] = "*";
        http.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, DELETE, OPTIONS";
        http.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type, Accept, Authorization, X-MCP-Token, X-API-Key, Mcp-Session-Id, MCP-Protocol-Version";
        http.Response.Headers["Access-Control-Expose-Headers"] = "Mcp-Session-Id";
    }

    private static string? GetRemoteIp(HttpContext http)
    {
        IPAddress? addr = http.Connection.RemoteIpAddress;
        if (addr is null)
            return null;
        return addr.IsIPv4MappedToIPv6 ? addr.MapToIPv4().ToString() : addr.ToString();
    }
}
