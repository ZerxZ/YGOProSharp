using YGOProSharp.Abstractions.Scripting;
using YGOProSharp.Core.Cards;
using YGOProSharp.Server;

namespace YGOProSharp.Web;

/// <summary>
/// Web 管理面板与控制面配置项。
/// </summary>
public sealed record WebControlOptions
{
    /// <summary>
    /// 监听绑定地址（默认仅绑定本机回环 <c>127.0.0.1</c> 防止裸奔暴露管理口）。
    /// </summary>
    public string Host { get; init; } = "127.0.0.1";

    /// <summary>
    /// 监听端口（默认 <c>7912</c>；在测试中可设为 <c>0</c> 自动分配随机空闲端口）。
    /// </summary>
    public int Port { get; init; } = 7912;

    /// <summary>
    /// 可选：关联展示的前台用户面板端口（如 <c>7913</c>），便于管理员从后台一键跳转预览前台用户面板。
    /// </summary>
    public int? PortalPort { get; init; }

    /// <summary>
    /// 会话 Cookie 名称。
    /// </summary>
    public string SessionCookieName { get; init; } = "ygp_panel_sid";

    /// <summary>
    /// 是否强制 Cookie 仅通过 HTTPS 传输（公网反向代理后建议开启）。
    /// </summary>
    public bool RequireHttpsCookie { get; init; }

    /// <summary>
    /// 前端静态构建产物目录（为空时自动探测 <c>wwwroot</c> 或 <c>web/dist</c>）。
    /// </summary>
    public string? StaticFilesRoot { get; init; }

    /// <summary>
    /// 内存日志环形缓冲区最大保留条数。
    /// </summary>
    public int LogRingBufferCapacity { get; init; } = 500;

    /// <summary>
    /// 可选：由 CLI 宿主注入的内置 WindBot 召唤回调（参数：目标房间、卡组名或 random、对话名、数量；返回召唤出的机器人昵称列表）。
    /// </summary>
    public Func<Game, string?, string?, int, IReadOnlyList<string>>? WindBotSpawner { get; init; }

    /// <summary>
    /// 可选：获取当前内置 WindBot 支持的卡组列表。
    /// </summary>
    public Func<IReadOnlyList<string>>? AvailableBotDecksProvider { get; init; }

    /// <summary>
    /// 可选：提供带名称与效果文本的卡片查询仓库（供管理面板卡片搜索与盲盒预览展示卡名，不污染 Server 核心层）。
    /// </summary>
    public Func<INamedCardRepository>? NamedCardRepositoryProvider { get; init; }

    /// <summary>
    /// 可选：提供硬化后的 Lua 沙箱运行时（供 MCP <c>windbot_write_deck_script</c> 进行 DSL 干跑预检）。
    /// </summary>
    public Func<ILuaRuntime>? LuaRuntimeProvider { get; init; }
}
