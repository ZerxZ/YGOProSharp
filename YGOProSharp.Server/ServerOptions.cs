using YGOProSharp.Abstractions.Ocg;
using YGOProSharp.Core;
using YGOProSharp.Core.Cards;
using YGOProSharp.Server.Addons;

namespace YGOProSharp.Server;

public sealed record ServerOptions
{
    public string RootPath { get; init; } = ".";
    public string ScriptDirectory { get; init; } = "script";
    public string DatabaseFile { get; init; } = "cards.cdb";
    public string BanlistFile { get; init; } = "lflist.conf";
    public int Port { get; init; } = CoreServer.DefaultPort;

    /// <summary>
    /// 是否开启 TCP 监听端口。设为 <c>false</c> 时仅以进程内嵌入式模式启动，供 C# 代码通过内存通道直连。
    /// </summary>
    public bool EnableNetworkListener { get; init; } = true;
    public uint ClientVersion { get; init; } = YGOProSharpServer.DefaultClientVersion;
    public bool StandardStreamProtocol { get; init; }

    /// <summary>
    /// 是否自动扫描 <c>&lt;RootPath&gt;/expansions</c> 与 <c>&lt;RootPath&gt;/diy</c> 目录下的扩展/DIY 卡库、脚本、压缩包与禁限表。
    /// </summary>
    public bool AutoLoadExpansions { get; init; } = true;

    /// <summary>
    /// 额外的扩展/DIY 根目录列表（按顺序加载，越靠后优先级越高；每个目录可包含 <c>*.cdb</c>、<c>script/</c>、<c>*.ypk</c>/<c>*.zip</c>、<c>lflist.conf</c>）。
    /// </summary>
    public IReadOnlyList<string> ExpansionDirectories { get; init; } = [];

    /// <summary>
    /// 额外叠加的卡片数据库（<c>.cdb</c>）文件路径列表（按顺序覆盖基础卡库）。
    /// </summary>
    public IReadOnlyList<string> ExtraDatabases { get; init; } = [];

    /// <summary>
    /// 额外叠加的 Lua 卡脚本目录（优先级高于基础 <see cref="ScriptDirectory"/>）。
    /// </summary>
    public IReadOnlyList<string> ExtraScriptDirectories { get; init; } = [];

    /// <summary>
    /// 额外叠加的 <c>.ypk</c>/<c>.zip</c> 脚本压缩包路径列表。
    /// </summary>
    public IReadOnlyList<string> ExtraScriptArchives { get; init; } = [];

    /// <summary>
    /// 额外叠加的禁限表文件列表（将前置到基础禁限表之前）。
    /// </summary>
    public IReadOnlyList<string> ExtraBanlistFiles { get; init; } = [];

    /// <summary>
    /// 代码级叠加的自定义卡片数据源（优先级高于磁盘 <c>.cdb</c>，适合纯代码 DIY 卡片）。
    /// </summary>
    public IReadOnlyList<ICardRepository> AdditionalCardRepositories { get; init; } = [];

    /// <summary>
    /// 代码级叠加的自定义脚本数据源（优先级高于磁盘脚本目录，适合纯代码 DIY 卡脚本或全局规则脚本）。
    /// </summary>
    public IReadOnlyList<IScriptProvider> AdditionalScriptProviders { get; init; } = [];

    /// <summary>
    /// 是否启用多房间模式。设为 <c>false</c>（默认）时保持单房间兼容；设为 <c>true</c> 时按房间密码/名称动态路由多个并发房间。
    /// </summary>
    public bool EnableMultiRoom { get; init; }

    /// <summary>
    /// 多房间模式下的最大同时活动房间数。
    /// </summary>
    public int MaxRooms { get; init; } = 128;

    /// <summary>
    /// 单房间模式下，首个玩家在空大厅通过带规则前缀的密码（如 <c>M,DIY,LP4000#</c>）加入时是否应用该规则覆写。
    /// </summary>
    public bool ApplyPasswordRulesInSingleRoom { get; init; } = true;

    /// <summary>
    /// 是否启用内置房间聊天命令插件（<c>/help</c>、<c>/rules</c>、<c>/room</c>、<c>/rooms</c>、<c>/surrender</c>）。
    /// </summary>
    public bool EnableChatCommands { get; init; } = true;

    /// <summary>
    /// 额外注册到房间聊天命令插件的自定义命令列表。
    /// </summary>
    public IReadOnlyList<ChatCommandDefinition> ChatCommands { get; init; } = [];

    /// <summary>
    /// 自定义房间级插件工厂（每个房间创建时都会依次构造并挂载，房间结束时自动 Dispose）。
    /// </summary>
    public IReadOnlyList<Func<Game, AddonBase>> AddonFactories { get; init; } = [];

    /// <summary>
    /// 自定义房间密码规则解析器；为空时使用 <see cref="RoomPasswordParser.Default"/>。
    /// </summary>
    public RoomPasswordParser? RoomPasswordParser { get; init; }

    /// <summary>
    /// 额外扫描的服务端预设随机盲盒 <c>.ydk</c> 目录列表（默认自动扫描 <c>&lt;RootPath&gt;/decks/random</c> 与启用的 DIY 包 <c>decks/</c>）。
    /// </summary>
    public IReadOnlyList<string> RandomDeckDirectories { get; init; } = [];

    /// <summary>
    /// 可选外部注入的预设盲盒卡组池实例；为空时由服务端自动创建并管理。
    /// </summary>
    public PresetDeckPool? PresetDeckPool { get; init; }

    /// <summary>
    /// 在 Match（BO3/BO5）模式下启用随机卡组时，是否每小局重新随机抽取新卡组（默认 <c>true</c>；设为 <c>false</c> 则整场 Match 沿用首局随机卡组）。
    /// </summary>
    public bool RandomDeckRerollPerMatchDuel { get; init; } = true;

    public GameOptions Game { get; init; } = new();
}

public sealed record GameOptions
{
    public int Mode { get; init; }
    public int Region { get; init; }
    public int MasterRule { get; init; } = 5;
    public int Banlist { get; init; }
    public int StartLp { get; init; } = Game.DEFAULT_LIFEPOINTS;
    public int StartHand { get; init; } = Game.DEFAULT_START_HAND;
    public int DrawCount { get; init; } = Game.DEFAULT_DRAW_COUNT;
    public int GameTimer { get; init; } = Game.DEFAULT_TIMER;
    public bool EnablePriority { get; init; }
    public bool NoCheckDeck { get; init; }
    public bool NoShuffleDeck { get; init; }

    /// <summary>
    /// 房间强制服务端随机卡组模式（默认 <see cref="RandomDeckMode.None"/> 关闭）。
    /// </summary>
    public RandomDeckMode RandomDeck { get; init; } = RandomDeckMode.None;

    /// <summary>
    /// 混沌随机模式（<c>CHAOS</c> / <c>CHAOS60</c>）的目标主卡组张数（默认 40）。
    /// </summary>
    public int ChaosMainDeckSize { get; init; } = 40;

    /// <summary>
    /// 是否默认开启额外回合猝死规则（<c>ED</c>）。
    /// </summary>
    public bool SuddenDeath { get; init; }

    /// <summary>
    /// Match 模式下获胜所需的单局胜场数（默认 2 即 BO3，配合 <c>BO5</c> 可设为 3）。
    /// </summary>
    public int MatchWins { get; init; } = 2;

    public DeckRules DeckRules { get; init; } = DeckRules.Default;

    /// <summary>
    /// 自定义卡组校验器；为空时使用 <see cref="DefaultDeckValidator.Instance"/>。
    /// </summary>
    public IDeckValidator? DeckValidator { get; init; }

    /// <summary>
    /// 对局启动前自动通过 <see cref="IDuelSession.PreloadScript"/> 预加载的额外 Lua 脚本（常用于 DIY 全局规则/技能）。
    /// </summary>
    public IReadOnlyList<string> ExtraScripts { get; init; } = [];

    /// <summary>
    /// 自定义扩展标签（可由房间密码自定义 token 或配置注入，供 addon / DIY 规则读取）。
    /// </summary>
    public IReadOnlyDictionary<string, string>? CustomTags { get; init; }
}
