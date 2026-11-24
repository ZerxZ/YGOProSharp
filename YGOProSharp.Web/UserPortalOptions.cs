using YGOProSharp.Core.Cards;

namespace YGOProSharp.Web;

/// <summary>
/// 面向普通玩家与社区访客的前台用户面板配置项（与管理后台独立端口监听、独立路由表物理隔离）。
/// </summary>
public sealed record UserPortalOptions
{
    /// <summary>
    /// 前台用户面板监听地址（默认 <c>127.0.0.1</c>，公网开放给玩家时可设为 <c>0.0.0.0</c>）。
    /// </summary>
    public string Host { get; init; } = "127.0.0.1";

    /// <summary>
    /// 前台用户面板独立端口（默认 <c>7913</c>，与游戏服 <c>7911</c>、管理后台 <c>7912</c> 互不共用；测试中可设为 <c>0</c>）。
    /// </summary>
    public int Port { get; init; } = 7913;

    /// <summary>
    /// 可选：在玩家面板展示的联机服务器地址提示（为空时前端自动取当前访问域名）。
    /// </summary>
    public string? PublicGameHost { get; init; }

    /// <summary>
    /// 前端静态构建产物目录（为空时自动探测 <c>wwwroot</c>）。
    /// </summary>
    public string? StaticFilesRoot { get; init; }

    /// <summary>
    /// 可选：提供带名称与效果文本的卡片查询仓库（用于玩家卡查与 DIY 图鉴展示）。
    /// </summary>
    public Func<INamedCardRepository>? NamedCardRepositoryProvider { get; init; }
}
