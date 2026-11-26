using YGOProSharp.Abstractions.Ocg;
using YGOProSharp.Core;
using YGOProSharp.Protocol.Enums;

namespace YGOProSharp.Server;

public interface IGame
{
    event Action<object, EventArgs>? OnNetworkReady;
    event Action<object, EventArgs>? OnNetworkEnd;
    event Action<object, EventArgs>? OnGameStart;
    event Action<object, EventArgs>? OnGameEnd;
    event Action<object, EventArgs>? OnDuelEnd;
    event Action<object, PlayerEventArgs>? OnPlayerJoin;
    event Action<object, PlayerEventArgs>? OnPlayerLeave;
    event Action<object, PlayerMoveEventArgs>? OnPlayerMove;
    event Action<object, PlayerEventArgs>? OnPlayerReady;
    event Action<object, PlayerChatInterceptEventArgs>? OnBeforePlayerChat;
    event Action<object, PlayerChatEventArgs>? OnPlayerChat;
    event Action<object, DuelStartContext>? OnBeforeDuelStart;
    event Action<object, DuelPublicActionEventArgs>? OnPublicDuelEvent;
}

/// <summary>
/// 100% 公开可见的对局动作与基本分变动事件参数（严格遵守战争迷雾：绝不包含抽卡卡密、里侧盖卡卡密、手牌或牌堆顺序）。
/// </summary>
public sealed class DuelPublicActionEventArgs : EventArgs
{
    public string Kind { get; init; } = string.Empty;
    public int TurnCount { get; init; }
    public int DuelSide { get; init; }
    public int LobbyTeamId { get; init; }
    public string? ActorName { get; init; }
    public int CardCode { get; init; }
    public int Location { get; init; }
    public int Sequence { get; init; }
    public int Value { get; init; }
    public int Team0Lp { get; init; }
    public int Team1Lp { get; init; }
}

public class PlayerEventArgs : EventArgs
{
    public object Player { get; }

    public PlayerEventArgs(object player)
    {
        Player = player;
    }
}

public sealed class PlayerMoveEventArgs : PlayerEventArgs
{
    public int FromType { get; }

    public PlayerMoveEventArgs(object player, int fromType)
        : base(player)
    {
        FromType = fromType;
    }
}

public sealed class PlayerChatEventArgs : PlayerEventArgs
{
    public string Message { get; }

    public PlayerChatEventArgs(object player, string message)
        : base(player)
    {
        Message = message;
    }
}

/// <summary>
/// 聊天广播前拦截事件参数；addon 或命令处理器可将 <see cref="Handled"/> 置为 <c>true</c> 以阻止原样广播，
/// 或改写 <see cref="Message"/>。
/// </summary>
public sealed class PlayerChatInterceptEventArgs : PlayerEventArgs
{
    public string Message { get; set; }

    public bool Handled { get; set; }

    public PlayerChatInterceptEventArgs(object player, string message)
        : base(player)
    {
        Message = message;
    }
}

/// <summary>
/// 对局即将启动（卡组已注入、<see cref="IDuelSession.Start"/> 调用前）的扩展上下文。
/// 可用于 DIY 规则预加载脚本（<see cref="IDuelSession.PreloadScript"/>）、注入场地/技能卡或调整选项位。
/// </summary>
public sealed class DuelStartContext : EventArgs
{
    public Game Game { get; }

    public IDuelSession Session { get; }

    public int DuelOptions { get; set; }

    public uint Seed { get; }

    public DuelStartContext(Game game, IDuelSession session, int duelOptions, uint seed)
    {
        Game = game;
        Session = session;
        DuelOptions = duelOptions;
        Seed = seed;
    }
}

/// <summary>
/// 卡组校验上下文，供自定义 <see cref="IDeckValidator"/> 实现 DIY 赛制、单卡限制或主题限定等规则。
/// </summary>
public sealed class DeckValidationContext
{
    public Game Game { get; }

    public Player Player { get; }

    public Deck Deck { get; }

    public Banlist? Banlist { get; }

    public int Region { get; }

    public bool NoCheckDeck { get; }

    public DeckRules DeckRules { get; }

    public DeckValidationContext(
        Game game,
        Player player,
        Deck deck,
        Banlist? banlist,
        int region,
        bool noCheckDeck,
        DeckRules deckRules)
    {
        Game = game;
        Player = player;
        Deck = deck;
        Banlist = banlist;
        Region = region;
        NoCheckDeck = noCheckDeck;
        DeckRules = deckRules;
    }
}

/// <summary>
/// 可插拔卡组校验接口。返回 <c>0</c> 表示合法，返回非零值（如违规卡密或 <c>1</c>）将按协议发送 DeckError。
/// </summary>
public interface IDeckValidator
{
    int Validate(DeckValidationContext context);
}

/// <summary>
/// 默认卡组校验器：按 Region 映射 OCG/TCG/Custom(DIY) 权限并委托给 <see cref="Deck.Check(Banlist?, bool, bool, bool, DeckRules)"/>。
/// </summary>
public sealed class DefaultDeckValidator : IDeckValidator
{
    public static DefaultDeckValidator Instance { get; } = new();

    private DefaultDeckValidator()
    {
    }

    public int Validate(DeckValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.NoCheckDeck)
            return 0;

        (bool ocg, bool tcg, bool allowCustom) = ResolveRegionFlags(context.Region);
        return context.Deck.Check(context.Banlist, ocg, tcg, allowCustom, context.DeckRules);
    }

    /// <summary>
    /// 将协议 Region 值映射为卡片可用域：
    /// 0=OCG, 1=TCG, 2=OCG/TCG, 3=DIY/Custom(+OCG/TCG), 4=No Exclusive(仅 ot=3), 5+=全部。
    /// </summary>
    public static (bool Ocg, bool Tcg, bool AllowCustom) ResolveRegionFlags(int region)
    {
        return region switch
        {
            0 => (true, false, false),
            1 => (false, true, false),
            2 => (true, true, false),
            3 => (true, true, true),
            4 => (false, false, false),
            _ => (true, true, true)
        };
    }
}

/// <summary>
/// 房间运行状态只读快照，供大厅列表、聊天命令或外部监控查询。
/// </summary>
public sealed record RoomSnapshot(
    string RoomName,
    GameState State,
    int Mode,
    int Region,
    int MasterRule,
    int StartLp,
    int DuelistCount,
    int MaxDuelists,
    int ObserverCount,
    string? HostPlayerName,
    IReadOnlyList<string> PlayerNames,
    RandomDeckMode RandomDeckMode = RandomDeckMode.None,
    bool IsSuddenDeath = false,
    int TurnCount = 0,
    int Team0Lp = 0,
    int Team1Lp = 0);

/// <summary>
/// 服务端热重载执行结果摘要。
/// </summary>
public sealed record ServerReloadResult(
    long CatalogVersion,
    long ScriptVersion,
    int DatabaseCount,
    int BanlistCount,
    int PresetDeckCount);

/// <summary>
/// 玩家入房/建房前置准入检查上下文（用于解耦对接外部 IP/昵称封禁表、vpass 校验或云录像回放拦截）。
/// </summary>
public sealed record PlayerJoinAccessContext(
    Player Player,
    string RoomPassword,
    bool IsCreateGame);

/// <summary>
/// 玩家入房准入检查决策。当 <see cref="HandledCustomSession"/> 为 <c>true</c> 时（如云录像直接下发完成），不再进入常规房间。
/// </summary>
public sealed record PlayerJoinAccessDecision(
    bool Allowed,
    string? RejectionMessage = null,
    bool HandledCustomSession = false);
