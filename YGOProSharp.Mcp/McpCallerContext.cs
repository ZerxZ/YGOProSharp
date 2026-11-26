using System.Text.Json;
using YGOProSharp.AgentBridge;
using YGOProSharp.Persistence;

namespace YGOProSharp.Mcp;

/// <summary>
/// 标注单个 MCP 工具方法允许访问的角色集合（默认按最小权限裁剪）。
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class McpRolesAttribute(params string[] roles) : Attribute
{
    public IReadOnlyList<string> Roles { get; } = roles;
}

/// <summary>
/// 统一工具返回包装：区分业务成功与校验/执行失败（映射至 <c>CallToolResult.IsError</c>）。
/// </summary>
public readonly record struct ToolOutcome(bool IsError, object Payload)
{
    public static ToolOutcome Ok(object payload) => new(false, payload);
    public static ToolOutcome Fail(object payload) => new(true, payload);
    public static ToolOutcome From(bool ok, object payload) => new(!ok, payload);
}

/// <summary>
/// 基于 <see cref="AsyncLocal{T}"/> 传递当前 MCP 调用方令牌与来源 IP，避免污染自动生成的工具参数 Schema。
/// </summary>
public sealed record McpCallerScope(ValidatedMcpToken Caller, string? RemoteIp)
{
    private static readonly AsyncLocal<McpCallerScope?> CurrentScope = new();

    public static McpCallerScope Current =>
        CurrentScope.Value ?? throw new InvalidOperationException("No active MCP caller context.");

    public static IDisposable Push(ValidatedMcpToken caller, string? remoteIp)
    {
        McpCallerScope? previous = CurrentScope.Value;
        CurrentScope.Value = new McpCallerScope(caller, remoteIp);
        return new PopWhenDisposed(previous);
    }

    private sealed class PopWhenDisposed(McpCallerScope? previous) : IDisposable
    {
        public void Dispose() => CurrentScope.Value = previous;
    }
}

internal static class McpArgHelpers
{
    public static AgentObservationDetail ParseDetail(string? raw, AgentObservationDetail fallback)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return fallback;

        return raw.Trim().ToLowerInvariant() switch
        {
            "none" or "quiet" => AgentObservationDetail.None,
            "min" or "minimal" or "compact" => AgentObservationDetail.Minimal,
            "std" or "standard" => AgentObservationDetail.Standard,
            "full" or "verbose" => AgentObservationDetail.Full,
            _ => fallback
        };
    }

    public static bool TryParseDuelId(JsonElement? elem, out long duelId)
    {
        duelId = 0;
        if (elem is null)
            return false;

        JsonElement v = elem.Value;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long l) && l > 0)
        {
            duelId = l;
            return true;
        }

        if (v.ValueKind == JsonValueKind.String)
        {
            string? s = v.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(s))
                return false;
            if (s.StartsWith("R#", StringComparison.OrdinalIgnoreCase))
                s = s[2..].Trim();
            else if (s.StartsWith('#'))
                s = s[1..].Trim();

            if (long.TryParse(s, out long parsed) && parsed > 0)
            {
                duelId = parsed;
                return true;
            }
        }

        return false;
    }

    public static bool ValidateScoreRange(int? value, string fieldName, out int score, out string? errorMessage)
    {
        if (value is null)
        {
            score = -1;
            errorMessage = $"缺少必填评分字段 '{fieldName}'（需为 0 ~ 100 之间的整数）。";
            return false;
        }

        score = value.Value;
        if (score is < 0 or > 100)
        {
            errorMessage = $"评分字段 '{fieldName}' 超出范围：{score}（必须为 0 ~ 100 之间的整数）。";
            return false;
        }

        errorMessage = null;
        return true;
    }
}
