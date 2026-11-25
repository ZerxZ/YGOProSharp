using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using YGOProSharp.Core;
using YGOProSharp.Protocol.Enums;
using YGOProSharp.Protocol.Utils;

namespace YGOProSharp.Server.Addons;

/// <summary>
/// 房间内聊天命令执行上下文。
/// </summary>
public sealed class ChatCommandContext
{
    public Game Game { get; }
    public Player Player { get; }
    public string CommandName { get; }
    public string RawArguments { get; }
    public IReadOnlyList<string> Arguments { get; }
    public bool IsHost => Game.IsHost(Player);

    public ChatCommandContext(Game game, Player player, string commandName, string rawArguments, IReadOnlyList<string> arguments)
    {
        Game = game;
        Player = player;
        CommandName = commandName;
        RawArguments = rawArguments;
        Arguments = arguments;
    }

    public void Reply(string message) => Game.SendSystemMessage(Player, message);

    public void Broadcast(string message) => Game.BroadcastSystemMessage(message);
}

/// <summary>
/// 一条可注册的聊天命令定义（如 <c>/help</c>、<c>/rules</c>、<c>/ai</c> 或自定义 DIY 命令）。
/// </summary>
public sealed record ChatCommandDefinition(
    string Name,
    string Description,
    Action<ChatCommandContext> Handler,
    IReadOnlyList<string>? Aliases = null,
    string? Usage = null,
    string? Category = null);

/// <summary>
/// 内置房间聊天命令插件。拦截以 <see cref="CommandPrefix"/> 开头的消息，
/// 提供房间查询、玩家操作、娱乐骰子/硬币、房主实时改规则、DIY 脚本管理及自定义命令扩展。
/// </summary>
public sealed class ChatCommandAddon : AddonBase
{
    private readonly Dictionary<string, ChatCommandDefinition> _commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ChatCommandDefinition> _registered = new();
    private bool _disposed;

    public char CommandPrefix { get; }

    public ChatCommandAddon(
        Game game,
        IEnumerable<ChatCommandDefinition>? customCommands = null,
        char commandPrefix = '/')
        : base(game)
    {
        CommandPrefix = commandPrefix;
        RegisterBuiltInCommands();

        if (customCommands is not null)
        {
            foreach (ChatCommandDefinition command in customCommands)
                RegisterCommand(command);
        }

        Game.OnBeforePlayerChat += HandleBeforePlayerChat;
    }

    public IReadOnlyList<ChatCommandDefinition> RegisteredCommands => _registered;

    public void RegisterCommand(ChatCommandDefinition command)
    {
        ArgumentNullException.ThrowIfNull(command);
        string key = NormalizeName(command.Name);
        if (key.Length == 0)
            throw new ArgumentException("Command name cannot be empty.", nameof(command));

        _registered.RemoveAll(c => string.Equals(NormalizeName(c.Name), key, StringComparison.OrdinalIgnoreCase));
        _registered.Add(command);
        _commands[key] = command;

        if (command.Aliases is not null)
        {
            foreach (string alias in command.Aliases)
            {
                string aliasKey = NormalizeName(alias);
                if (aliasKey.Length > 0)
                    _commands[aliasKey] = command;
            }
        }
    }

    public void RegisterCommand(
        string name,
        string description,
        Action<ChatCommandContext> handler,
        string? usage = null,
        string? category = null,
        params string[] aliases)
    {
        RegisterCommand(new ChatCommandDefinition(name, description, handler, aliases, usage, category));
    }

    private void RegisterBuiltInCommands()
    {
        // --- 信息查询 ---
        RegisterCommand("help", "查看命令列表或指定命令用法", HandleHelp, "/help [命令名|分类]", "查询", "?");

        RegisterCommand("rules", "查看当前房间规则与 DIY 配置", ctx =>
        {
            Game g = ctx.Game;
            string banlistName = g.Banlist is null ? "None" : g.Banlist.Name;
            string modeName = FormatMode(g.Mode, g.MatchWins);
            string regionName = FormatRegion(g.Region);
            string randTag = g.RandomDeckMode != RandomDeckMode.None ? $", RandDeck={g.RandomDeckMode}({g.ChaosMainDeckSize})" : string.Empty;
            string deathTag = g.IsSuddenDeath ? ", SuddenDeath=ON" : string.Empty;
            string extra = g.ExtraScripts.Count > 0 ? $", ExtraScripts={string.Join(",", g.ExtraScripts)}" : string.Empty;
            ctx.Reply($"[Rules] Room={g.RoomName}, Mode={modeName}, Region={regionName}, MR={g.MasterRule}, LP={g.StartLp}, Hand={g.StartHand}, Draw={g.DrawCount}, Timer={g.Timer}s, Banlist={banlistName}, NoCheck={g.NoCheckDeck}, NoShuffle={g.NoShuffleDeck}{randTag}{deathTag}{extra}");
        }, "/rules", "查询", "rule");

        RegisterCommand("room", "查看当前房间玩家、准备状态与观战人数", ctx =>
        {
            Game g = ctx.Game;
            List<string> slots = new();
            for (int i = 0; i < g.Players.Length; i++)
            {
                Player p = g.Players[i];
                string teamTag = g.IsTag ? (i < 2 ? "[A队]" : "[B队]") : string.Empty;
                if (p is null)
                    slots.Add($"#{i}{teamTag}:<空>");
                else
                {
                    string hostTag = g.IsHost(p) ? "[房主]" : string.Empty;
                    string readyTag = g.State == GameState.Lobby ? (g.IsReady[i] ? "(已准备)" : "(未准备)") : string.Empty;
                    slots.Add($"#{i}{teamTag}:{p.Name}{hostTag}{readyTag}");
                }
            }
            ctx.Reply($"[Room] {g.RoomName} | State={g.State} | {string.Join(" , ", slots)} | 观战={g.Observers.Count}");
        }, "/room", "查询", "info");

        RegisterCommand("rooms", "查看当前服务端所有活动房间", ctx =>
        {
            IReadOnlyList<RoomSnapshot> rooms = ctx.Game.Server.GetRooms();
            if (rooms.Count == 0)
            {
                ctx.Reply("[Rooms] 当前没有活动房间。");
                return;
            }

            string list = string.Join(" ; ", rooms.Select(r => $"{r.RoomName}({r.State}, {r.DuelistCount}/{r.MaxDuelists})"));
            ctx.Reply($"[Rooms] 共 {rooms.Count} 个: {list}");
        }, "/rooms", "查询");

        RegisterCommand("banlists", "列出已加载的禁限表编号与名称", ctx =>
        {
            if (BanlistManager.Banlists.Count == 0)
            {
                ctx.Reply("[Banlists] 当前未加载任何禁限表。");
                return;
            }

            int page = 1;
            if (ctx.Arguments.Count > 0 && int.TryParse(ctx.Arguments[0], out int parsedPage) && parsedPage >= 1)
                page = parsedPage;

            const int pageSize = 8;
            int totalPages = (BanlistManager.Banlists.Count + pageSize - 1) / pageSize;
            page = Math.Clamp(page, 1, totalPages);
            int start = (page - 1) * pageSize;
            int end = Math.Min(start + pageSize, BanlistManager.Banlists.Count);

            List<string> items = new();
            for (int i = start; i < end; i++)
                items.Add($"{i + 1}:{BanlistManager.Banlists[i].Name}");

            ctx.Reply($"[Banlists {page}/{totalPages}] {string.Join(" | ", items)}（用 /banlist <编号|名称|none> 切换）");
        }, "/banlists [页码]", "查询", "lf", "lflist");

        // --- 玩家操作 & 娱乐 ---
        RegisterCommand("ready", "切换为已准备状态", ctx =>
        {
            if (!EnsureLobby(ctx))
                return;
            ctx.Game.SetReady(ctx.Player, true);
        }, "/ready", "玩家");

        RegisterCommand("unready", "取消准备状态", ctx =>
        {
            if (!EnsureLobby(ctx))
                return;
            ctx.Game.SetReady(ctx.Player, false);
        }, "/unready", "玩家");

        RegisterCommand("duelist", "在大厅切入决斗者席位", ctx =>
        {
            if (!EnsureLobby(ctx))
                return;
            ctx.Game.MoveToDuelist(ctx.Player);
        }, "/duelist", "玩家", "play");

        RegisterCommand("observer", "在大厅切入观战席位", ctx =>
        {
            if (!EnsureLobby(ctx))
                return;
            ctx.Game.MoveToObserver(ctx.Player);
        }, "/observer", "玩家", "obs", "watch");

        RegisterCommand("swap", "在 2v2 双打大厅切换空余席位/队伍", ctx =>
        {
            if (!EnsureLobby(ctx))
                return;
            if (!ctx.Game.IsTag)
            {
                ctx.Reply("[Swap] 当前并非 2v2 (Tag) 模式，无需切换队伍席位。");
                return;
            }
            if (ctx.Player.Type == (int)PlayerType.Observer)
            {
                ctx.Game.MoveToDuelist(ctx.Player);
                return;
            }
            if (ctx.Game.IsReady[ctx.Player.Type])
            {
                ctx.Reply("[Swap] 请先取消准备（/unready）后再切换席位。");
                return;
            }
            int before = ctx.Player.Type;
            ctx.Game.MoveToDuelist(ctx.Player);
            if (ctx.Player.Type == before)
                ctx.Reply("[Swap] 当前 4 个决斗席位已满，可先切观战（/observer）后互换。");
        }, "/swap", "玩家", "seat", "team");

        RegisterCommand("partner", "在 2v2 双打中仅向同队搭档发送《卡片力量》式战术方针（不向敌方公开）", ctx =>
        {
            if (!ctx.Game.IsTag)
            {
                ctx.Reply("[Partner] 当前并非 2v2 双打房间，仅双打模式支持同队战术沟通。");
                return;
            }
            if (ctx.Player.Type == (int)PlayerType.Observer || ctx.Player.Type < 0 || ctx.Player.Type >= 4)
            {
                ctx.Reply("[Partner] 仅在席决斗者可向搭档下达战术方针。");
                return;
            }

            string raw = ctx.RawArguments.Trim();
            if (string.IsNullOrEmpty(raw))
            {
                ctx.Reply("[Partner] 用法: /partner <agg|control|setup|otk|def|balanced> [补充叮嘱]（如 /partner agg 先踩光对面后场！）");
                return;
            }

            string firstToken = ctx.Arguments.Count > 0 ? ctx.Arguments[0] : raw;
            (string? stanceCode, string stanceLabel) = firstToken.ToLowerInvariant() switch
            {
                "agg" or "aggressive" or "attack" or "激进" or "强攻" => ("aggressive", "激进强攻"),
                "otk" or "allin" or "all_in_otk" or "斩杀" or "总攻" => ("all_in_otk", "本回合总攻斩杀"),
                "control" or "ctrl" or "steady" or "稳健" or "留康" => ("control", "稳健留康护航"),
                "setup" or "setup_partner" or "pass" or "铺垫" or "留场" => ("setup_partner", "为搭档铺垫留场"),
                "def" or "defensive" or "save" or "防守" or "保命" => ("defensive", "全员防守保命"),
                "bal" or "balanced" or "free" or "均衡" or "自由" => ("balanced", "自由均衡发挥"),
                _ => (null, "自由沟通")
            };

            string note = stanceCode is not null
                ? (raw.Length > firstToken.Length ? raw[firstToken.Length..].Trim() : string.Empty)
                : raw;
            string effectiveStance = stanceCode ?? "balanced";
            int team = ctx.Player.Type >= 2 ? 1 : 0;

            using var pkt = GamePacketFactory.Create(StocMessage.Chat);
            pkt.Write((short)PlayerType.Yellow);
            string formatted = string.IsNullOrEmpty(note)
                ? $"[PartnerTactic|{effectiveStance}|{ctx.Player.Name}] 搭档「{ctx.Player.Name}」切换双打方针为【{stanceLabel}】"
                : $"[PartnerTactic|{effectiveStance}|{ctx.Player.Name}] 搭档「{ctx.Player.Name}」指示【{stanceLabel}】：{note}";
            pkt.WriteUnicode(formatted, formatted.Length + 1);
            ctx.Game.SendToTeam(pkt, team);
        }, "/partner <agg|control|setup|otk|def|balanced> [备注]", "双打", "p", "tactic", "tag");

        RegisterCommand("randdeck", "在大厅为自己单独申请服务端随机卡组（免改本地 .ydk）", ctx =>
        {
            if (!EnsureLobby(ctx))
                return;
            if (ctx.Player.Type == (int)PlayerType.Observer)
            {
                ctx.Reply("[RandDeck] 观战席位无法申请随机卡组，请先切入决斗席位（/duelist）。");
                return;
            }

            string arg = ctx.Arguments.Count > 0 ? ctx.Arguments[0] : "preset";
            if (arg.Equals("off", StringComparison.OrdinalIgnoreCase) || arg.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                ctx.Game.SetPlayerPersonalRandomDeck(ctx.Player, RandomDeckMode.None);
                ctx.Reply("[RandDeck] 已关闭个人服务端随机卡组，将使用客户端提交的卡组。");
                return;
            }

            if (!TryParseRandomDeckMode(arg, out RandomDeckMode parsedMode, out int? chaosSize) || parsedMode == RandomDeckMode.None)
            {
                ctx.Reply("[RandDeck] 可选模式: preset(盲盒) | chaos(平衡混沌) | purechaos(纯真随机) | diy | diychaos | off");
                return;
            }

            if (chaosSize.HasValue)
                ctx.Game.ChaosMainDeckSize = chaosSize.Value;
            ctx.Game.SetPlayerPersonalRandomDeck(ctx.Player, parsedMode);
            ctx.Reply($"[RandDeck] 已为你挂载个人随机卡组模式：{parsedMode}，现在可直接点击准备！");
        }, "/randdeck [preset|chaos|purechaos|diy|diychaos|off]", "玩家", "myrand");

        RegisterCommand("surrender", "对局中主动投降", ctx =>
        {
            if (ctx.Game.State != GameState.Duel)
            {
                ctx.Reply("[Surrender] 当前未处于对局阶段。");
                return;
            }

            ctx.Game.Surrender(ctx.Player, 0);
        }, "/surrender", "玩家", "ff", "gg");

        RegisterCommand("roll", "公开掷骰子（默认 1~6，可指定上限）", ctx =>
        {
            int max = 6;
            if (ctx.Arguments.Count > 0 && int.TryParse(ctx.Arguments[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                max = Math.Clamp(parsed, 2, 1000);

            int value = Random.Shared.Next(1, max + 1);
            ctx.Broadcast($"[Roll] {ctx.Player.Name} 掷出了 {value}（1~{max}）");
        }, "/roll [上限]", "娱乐", "dice");

        RegisterCommand("coin", "公开抛硬币（正面 / 反面）", ctx =>
        {
            string side = Random.Shared.Next(2) == 0 ? "正面 (Heads)" : "反面 (Tails)";
            ctx.Broadcast($"[Coin] {ctx.Player.Name} 抛硬币结果：{side}");
        }, "/coin", "娱乐", "flip");

        // --- 房主大厅规则修改（实时同步到客户端面板） ---
        RegisterCommand("lp", "查看当前 LP 或修改初始 LP（房主）", ctx =>
        {
            if (ctx.Arguments.Count == 0)
            {
                if (ctx.Game.State == GameState.Duel)
                    ctx.Reply($"[LP] 当前 LP: P0={ctx.Game.LifePoints[0]} | P1={ctx.Game.LifePoints[1]}");
                else
                    ctx.Reply($"[LP] 当前初始 LP={ctx.Game.StartLp}（房主可用 /lp <数值> 修改）");
                return;
            }

            if (!EnsureHostLobby(ctx))
                return;

            if (!int.TryParse(ctx.Arguments[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int lp) || lp < 1 || lp > 999999)
            {
                ctx.Reply("[LP] 请输入 1~999999 之间的整数。");
                return;
            }

            ctx.Game.StartLp = lp;
            ctx.Game.LifePoints[0] = lp;
            ctx.Game.LifePoints[1] = lp;
            ctx.Game.SyncLobbyRulesToClients();
            ctx.Broadcast($"[Rules] 房主已将初始 LP 修改为 {lp}。");
        }, "/lp [1~999999]", "房主");

        RegisterCommand("mr", "修改大师规则 1~5（房主）", ctx =>
        {
            if (!EnsureHostLobby(ctx))
                return;

            if (ctx.Arguments.Count == 0 || !int.TryParse(ctx.Arguments[0], out int mr) || mr < 1 || mr > 5)
            {
                ctx.Reply($"[MR] 当前大师规则={ctx.Game.MasterRule}，用法: /mr <1-5>");
                return;
            }

            ctx.Game.MasterRule = mr;
            ctx.Game.SyncLobbyRulesToClients();
            ctx.Broadcast($"[Rules] 房主已将规则切换为大师规则 {mr}。");
        }, "/mr <1-5>", "房主", "masterrule");

        RegisterCommand("region", "修改卡片允许区域（ocg/tcg/ot/diy/nu/all）", ctx =>
        {
            if (!EnsureHostLobby(ctx))
                return;

            if (ctx.Arguments.Count == 0 || !TryParseRegion(ctx.Arguments[0], out int region))
            {
                ctx.Reply($"[Region] 当前区域={FormatRegion(ctx.Game.Region)}，可选: ocg(0), tcg(1), ot(2), diy(3), nu(4), all(5)");
                return;
            }

            ctx.Game.Region = region;
            ctx.Game.SyncLobbyRulesToClients();
            ctx.Broadcast($"[Rules] 房主已将卡片允许区域修改为 {FormatRegion(region)}。");
        }, "/region <ocg|tcg|ot|diy|nu|all>", "房主", "ot");

        RegisterCommand("banlist", "切换房间禁限表（编号、名称或 none）", ctx =>
        {
            if (!EnsureHostLobby(ctx))
                return;

            if (ctx.Arguments.Count == 0)
            {
                string current = ctx.Game.Banlist is null ? "None" : ctx.Game.Banlist.Name;
                ctx.Reply($"[Banlist] 当前禁限表={current}，用法: /banlist <编号|名称|none>（查看列表用 /banlists）");
                return;
            }

            string arg = ctx.RawArguments.Trim();
            if (arg.Equals("none", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("off", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("nf", StringComparison.OrdinalIgnoreCase) ||
                arg == "0" || arg == "-1")
            {
                ctx.Game.SetBanlist(null);
                ctx.Game.SyncLobbyRulesToClients();
                ctx.Broadcast("[Rules] 房主已关闭禁限表（无表）。");
                return;
            }

            Banlist? target = null;
            if (int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx) &&
                idx >= 1 && idx <= BanlistManager.Banlists.Count)
            {
                target = BanlistManager.Banlists[idx - 1];
            }
            else
            {
                target = BanlistManager.FindByName(arg)
                    ?? BanlistManager.Banlists.FirstOrDefault(b => b.Name.Contains(arg, StringComparison.OrdinalIgnoreCase));
            }

            if (target is null)
            {
                ctx.Reply($"[Banlist] 未找到匹配 '{arg}' 的禁限表，可用 /banlists 查看编号。");
                return;
            }

            ctx.Game.SetBanlist(target);
            ctx.Game.SyncLobbyRulesToClients();
            ctx.Broadcast($"[Rules] 房主已切换禁限表为 {target.Name}。");
        }, "/banlist <编号|名称|none>", "房主", "bl");

        RegisterCommand("nocheck", "开关卡组检查（房主）", ctx =>
        {
            if (!EnsureHostLobby(ctx))
                return;

            bool next = ParseToggle(ctx.Arguments, !ctx.Game.NoCheckDeck);
            ctx.Game.NoCheckDeck = next;
            ctx.Game.SyncLobbyRulesToClients();
            ctx.Broadcast($"[Rules] 房主已{(next ? "关闭卡组校验 (NoCheck=ON)" : "开启卡组校验 (NoCheck=OFF)")}。");
        }, "/nocheck [on|off]", "房主", "nc");

        RegisterCommand("noshuffle", "开关不洗牌（房主）", ctx =>
        {
            if (!EnsureHostLobby(ctx))
                return;

            bool next = ParseToggle(ctx.Arguments, !ctx.Game.NoShuffleDeck);
            ctx.Game.NoShuffleDeck = next;
            ctx.Game.SyncLobbyRulesToClients();
            ctx.Broadcast($"[Rules] 房主已{(next ? "开启不洗牌 (NoShuffle=ON)" : "恢复洗牌 (NoShuffle=OFF)")}。");
        }, "/noshuffle [on|off]", "房主", "ns");

        RegisterCommand("hand", "修改初始手牌数 0~40（房主）", ctx =>
        {
            if (!EnsureHostLobby(ctx))
                return;

            if (ctx.Arguments.Count == 0 || !int.TryParse(ctx.Arguments[0], out int hand) || hand < 0 || hand > 40)
            {
                ctx.Reply($"[Hand] 当前初始手牌={ctx.Game.StartHand}，用法: /hand <0-40>");
                return;
            }

            ctx.Game.StartHand = hand;
            ctx.Game.SyncLobbyRulesToClients();
            ctx.Broadcast($"[Rules] 房主已将初始手牌数修改为 {hand}。");
        }, "/hand <0-40>", "房主", "st");

        RegisterCommand("draw", "修改每回合抽卡数 0~35（房主）", ctx =>
        {
            if (!EnsureHostLobby(ctx))
                return;

            if (ctx.Arguments.Count == 0 || !int.TryParse(ctx.Arguments[0], out int draw) || draw < 0 || draw > 35)
            {
                ctx.Reply($"[Draw] 当前抽卡数={ctx.Game.DrawCount}，用法: /draw <0-35>");
                return;
            }

            ctx.Game.DrawCount = draw;
            ctx.Game.SyncLobbyRulesToClients();
            ctx.Broadcast($"[Rules] 房主已将每回合抽卡数修改为 {draw}。");
        }, "/draw <0-35>", "房主", "dr");

        RegisterCommand("timer", "查看或修改回合计时秒数（房主）", ctx =>
        {
            if (ctx.Arguments.Count == 0)
            {
                ctx.Reply($"[Timer] 当前每回合时间={ctx.Game.Timer}s（房主可用 /timer <秒数> 修改）");
                return;
            }

            if (!EnsureHostLobby(ctx))
                return;

            if (!int.TryParse(ctx.Arguments[0], out int seconds) || seconds < 0 || seconds > 9999)
            {
                ctx.Reply("[Timer] 请输入 0~9999 之间的秒数。");
                return;
            }

            ctx.Game.Timer = seconds;
            ctx.Game.SyncLobbyRulesToClients();
            ctx.Broadcast($"[Rules] 房主已将每回合时间修改为 {seconds}s。");
        }, "/timer [0~9999]", "房主", "time", "tm");

        RegisterCommand("mode", "切换单局/比赛/2v2双打模式（房主）", ctx =>
        {
            if (!EnsureHostLobby(ctx))
                return;

            if (ctx.Arguments.Count == 0 || !TryParseMode(ctx.Arguments[0], out int mode))
            {
                ctx.Reply($"[Mode] 当前模式={FormatMode(ctx.Game.Mode, ctx.Game.MatchWins)}，可选: single(0), match(1), tag/2v2(2)");
                return;
            }

            if (!ctx.Game.TrySetModeInLobby(mode))
            {
                ctx.Reply("[Mode] 无法切换模式：请先清空 Tag 额外席位。");
                return;
            }

            ctx.Game.SyncLobbyRulesToClients();
            ctx.Broadcast($"[Rules] 房主已将决斗模式切换为 {FormatMode(ctx.Game.Mode, ctx.Game.MatchWins)}（初始 LP={ctx.Game.StartLp}）。");
        }, "/mode <single|match|tag|2v2>", "房主");

        RegisterCommand("randmode", "设置全房强制服务端随机卡组模式（房主）", ctx =>
        {
            if (!EnsureHostLobby(ctx))
                return;

            if (ctx.Arguments.Count == 0 || !TryParseRandomDeckMode(ctx.Arguments[0], out RandomDeckMode mode, out int? chaosSize))
            {
                ctx.Reply($"[RandMode] 当前随机模式={ctx.Game.RandomDeckMode}，可选: off | preset(rd) | mirror(rdm) | chaos | chaos60 | purechaos | diy(diyrd) | diychaos");
                return;
            }

            ctx.Game.RandomDeckMode = mode;
            if (chaosSize.HasValue)
                ctx.Game.ChaosMainDeckSize = chaosSize.Value;
            if (mode is RandomDeckMode.DiyRandom or RandomDeckMode.DiyChaos && ctx.Game.Region != 3 && ctx.Game.Region < 5)
                ctx.Game.Region = 3;

            ctx.Game.SyncLobbyRulesToClients();
            ctx.Broadcast(mode == RandomDeckMode.None
                ? "[Rules] 房主已关闭全房强制随机卡组模式。"
                : $"[Rules] 房主已开启全房随机卡组模式：{mode}（玩家可直接点准备免改本地卡组）。");
        }, "/randmode <off|preset|mirror|chaos|chaos60|purechaos|diy|diychaos>", "房主", "random");

        RegisterCommand("death", "开启或关闭额外回合猝死规则 ED（房主，大厅与对局中均可用）", ctx =>
        {
            if (!ctx.IsHost)
            {
                ctx.Reply("[Death] 仅房主可以开关额外回合猝死规则。");
                return;
            }

            bool next = ParseToggle(ctx.Arguments, !ctx.Game.IsSuddenDeath);
            if (next)
            {
                int turns = 4;
                if (ctx.Arguments.Count >= 2 && int.TryParse(ctx.Arguments[1], out int parsedTurns))
                    turns = Math.Clamp(parsedTurns, 1, 20);

                ctx.Game.StartSuddenDeath(turns);
                if (ctx.Game.State != GameState.Duel)
                    ctx.Broadcast($"[Rules] 房主已预开启额外回合猝死规则（ED，开局 {turns} 回合后结算 LP）。");
            }
            else
            {
                ctx.Game.CancelSuddenDeath();
                if (ctx.Game.State != GameState.Duel)
                    ctx.Broadcast("[Rules] 房主已关闭额外回合猝死规则（ED）。");
            }
        }, "/death [on|off] [回合数]", "房主", "ed", "suddendeath");

        RegisterCommand("preset", "用房号规则语法一键套用组合规则（房主）", ctx =>
        {
            if (!EnsureHostLobby(ctx))
                return;

            if (string.IsNullOrWhiteSpace(ctx.RawArguments))
            {
                ctx.Reply("[Preset] 用法: /preset DIY,LP4000,MR5,NC,NF");
                return;
            }

            string normalized = ctx.RawArguments.Contains('#') ? ctx.RawArguments : ctx.RawArguments + "#";
            ParsedRoomPassword parsed = ctx.Game.Server.PasswordParser.Parse(normalized);
            if (!parsed.Overrides.HasAnyOverride)
            {
                ctx.Reply($"[Preset] 未识别到有效规则关键字：{ctx.RawArguments}");
                return;
            }

            if (!ctx.Game.ApplyLobbyRuleOverrides(parsed.Overrides))
            {
                ctx.Reply("[Preset] 应用规则失败（请确认房间在大厅且模式兼容）。");
                return;
            }

            ctx.Broadcast($"[Rules] 房主已套用规则组合: {string.Join(",", parsed.MatchedTokens)}。");
        }, "/preset <规则串>", "房主", "apply");

        RegisterCommand("script", "管理本局 DIY 预加载 Lua 脚本（房主）", ctx =>
        {
            if (ctx.Arguments.Count == 0 || ctx.Arguments[0].Equals("list", StringComparison.OrdinalIgnoreCase))
            {
                string list = ctx.Game.ExtraScripts.Count == 0 ? "<空>" : string.Join(", ", ctx.Game.ExtraScripts);
                ctx.Reply($"[Script] 当前预加载脚本: {list}（用法: /script add|remove|clear <路径>）");
                return;
            }

            if (!EnsureHostLobby(ctx))
                return;

            string action = ctx.Arguments[0].ToLowerInvariant();
            switch (action)
            {
                case "add" when ctx.Arguments.Count >= 2:
                {
                    string scriptPath = ctx.Arguments[1];
                    if (ctx.Game.AddExtraScript(scriptPath))
                        ctx.Broadcast($"[DIY] 房主添加预加载脚本: {scriptPath}");
                    else
                        ctx.Reply($"[Script] 脚本已在列表中或路径无效: {scriptPath}");
                    break;
                }
                case "remove" when ctx.Arguments.Count >= 2:
                case "del" when ctx.Arguments.Count >= 2:
                {
                    string scriptPath = ctx.Arguments[1];
                    if (ctx.Game.RemoveExtraScript(scriptPath))
                        ctx.Broadcast($"[DIY] 房主移除预加载脚本: {scriptPath}");
                    else
                        ctx.Reply($"[Script] 未找到脚本: {scriptPath}");
                    break;
                }
                case "clear":
                    ctx.Game.ClearExtraScripts();
                    ctx.Broadcast("[DIY] 房主已清空所有预加载脚本。");
                    break;
                default:
                    ctx.Reply("[Script] 用法: /script list | add <路径> | remove <路径> | clear");
                    break;
            }
        }, "/script <list|add|remove|clear> [路径]", "DIY", "diyscript");

        RegisterCommand("kick", "在大厅踢出指定槽位或名字的玩家（房主）", ctx =>
        {
            if (!EnsureHostLobby(ctx))
                return;

            if (ctx.Arguments.Count == 0)
            {
                ctx.Reply("[Kick] 用法: /kick <槽位编号|玩家名>");
                return;
            }

            int targetSlot = -1;
            if (int.TryParse(ctx.Arguments[0], out int slot) && slot >= 0 && slot < ctx.Game.Players.Length)
            {
                targetSlot = slot;
            }
            else
            {
                string name = ctx.RawArguments.Trim();
                for (int i = 0; i < ctx.Game.Players.Length; i++)
                {
                    if (ctx.Game.Players[i] is not null &&
                        string.Equals(ctx.Game.Players[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        targetSlot = i;
                        break;
                    }
                }
            }

            if (targetSlot < 0 || ctx.Game.Players[targetSlot] is null)
            {
                ctx.Reply($"[Kick] 未找到玩家 '{ctx.RawArguments}'。");
                return;
            }

            if (ctx.Player.Equals(ctx.Game.Players[targetSlot]))
            {
                ctx.Reply("[Kick] 不能踢出房主自己。");
                return;
            }

            string kickedName = ctx.Game.Players[targetSlot].Name;
            ctx.Game.KickPlayer(ctx.Player, targetSlot);
            ctx.Broadcast($"[Room] 房主已将 {kickedName} 移出房间。");
        }, "/kick <槽位|玩家名>", "房主");

        RegisterCommand("start", "在全员准备就绪后直接开始对局（房主）", ctx =>
        {
            if (!EnsureHostLobby(ctx))
                return;

            ctx.Game.StartDuel(ctx.Player);
            if (ctx.Game.State == GameState.Lobby)
                ctx.Reply("[Start] 无法开始：请确认所有决斗席位都有玩家且已准备。");
        }, "/start", "房主");
    }

    private void HandleHelp(ChatCommandContext ctx)
    {
        if (ctx.Arguments.Count > 0)
        {
            string query = NormalizeName(ctx.Arguments[0]);
            if (_commands.TryGetValue(query, out ChatCommandDefinition? def))
            {
                string aliases = def.Aliases is { Count: > 0 }
                    ? $" | 别名: {string.Join(", ", def.Aliases.Select(a => CommandPrefix + NormalizeName(a)))}"
                    : string.Empty;
                string usage = string.IsNullOrWhiteSpace(def.Usage) ? $"{CommandPrefix}{NormalizeName(def.Name)}" : def.Usage;
                ctx.Reply($"[Help] {usage} - {def.Description}{aliases}");
                return;
            }

            List<ChatCommandDefinition> byCategory = _registered
                .Where(c => string.Equals(c.Category, query, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (byCategory.Count > 0)
            {
                foreach (string line in FormatCommandLines(query, byCategory))
                    ctx.Reply(line);
                return;
            }

            ctx.Reply($"[Help] 未找到命令或分类 '{query}'，输入 {CommandPrefix}help 查看全部分类。");
            return;
        }

        // 按分类拆成多条短消息发送，避免超出 YGOPro/MDPro3 单条聊天长度上限（256 字符）而被截断。
        foreach (IGrouping<string, ChatCommandDefinition> group in _registered.GroupBy(c => string.IsNullOrWhiteSpace(c.Category) ? "扩展" : c.Category!))
        {
            foreach (string line in FormatCommandLines(group.Key, group))
                ctx.Reply(line);
        }
        ctx.Reply($"[Help] 输入 {CommandPrefix}help <命令名> 查看详细用法，例如 {CommandPrefix}help preset。");
    }

    private IEnumerable<string> FormatCommandLines(string category, IEnumerable<ChatCommandDefinition> commands)
    {
        List<string> lines = new();
        string prefix = $"[{category}] ";
        string current = prefix;

        foreach (ChatCommandDefinition cmd in commands)
        {
            string item = $"{CommandPrefix}{NormalizeName(cmd.Name)}";
            if (current.Length + item.Length + 2 > 180 && current.Length > prefix.Length)
            {
                lines.Add(current);
                current = prefix + item;
            }
            else
            {
                current = current.Length == prefix.Length ? current + item : current + ", " + item;
            }
        }

        if (current.Length > prefix.Length)
            lines.Add(current);

        return lines;
    }

    private static bool EnsureLobby(ChatCommandContext ctx)
    {
        if (ctx.Game.State == GameState.Lobby)
            return true;

        ctx.Reply("[Room] 该命令仅可在大厅阶段使用。");
        return false;
    }

    private static bool EnsureHostLobby(ChatCommandContext ctx)
    {
        if (!EnsureLobby(ctx))
            return false;

        if (ctx.IsHost)
            return true;

        ctx.Reply("[Room] 仅房主可以修改房间规则或执行管理命令。");
        return false;
    }

    private static bool ParseToggle(IReadOnlyList<string> args, bool defaultValue)
    {
        if (args.Count == 0)
            return defaultValue;

        string value = args[0].Trim().ToLowerInvariant();
        return value switch
        {
            "on" or "true" or "1" or "yes" or "开" => true,
            "off" or "false" or "0" or "no" or "关" => false,
            _ => defaultValue
        };
    }

    private static bool TryParseRegion(string raw, out int region)
    {
        string value = raw.Trim().ToLowerInvariant();
        switch (value)
        {
            case "0" or "ocg" or "oo":
                region = 0;
                return true;
            case "1" or "tcg" or "to":
                region = 1;
                return true;
            case "2" or "ot" or "ocgtcg":
                region = 2;
                return true;
            case "3" or "diy" or "custom":
                region = 3;
                return true;
            case "4" or "nu" or "noexclusive":
                region = 4;
                return true;
            case "5" or "all":
                region = 5;
                return true;
            default:
                region = 0;
                return false;
        }
    }

    private static bool TryParseRandomDeckMode(string raw, out RandomDeckMode mode, out int? chaosMainSize)
    {
        chaosMainSize = null;
        string value = raw.Trim().ToLowerInvariant();
        switch (value)
        {
            case "off" or "none" or "0" or "false":
                mode = RandomDeckMode.None;
                return true;
            case "preset" or "rd" or "rand" or "blind" or "box":
                mode = RandomDeckMode.PresetRandom;
                return true;
            case "mirror" or "rdm":
                mode = RandomDeckMode.MirrorRandom;
                return true;
            case "chaos" or "draft":
                mode = RandomDeckMode.ChaosDraft;
                chaosMainSize = 40;
                return true;
            case "chaos60":
                mode = RandomDeckMode.ChaosDraft;
                chaosMainSize = 60;
                return true;
            case "purechaos" or "pure":
                mode = RandomDeckMode.PureChaos;
                return true;
            case "diy" or "diyrd":
                mode = RandomDeckMode.DiyRandom;
                return true;
            case "diychaos":
                mode = RandomDeckMode.DiyChaos;
                return true;
            default:
                mode = RandomDeckMode.None;
                return false;
        }
    }

    private static bool TryParseMode(string raw, out int mode)
    {
        string value = raw.Trim().ToLowerInvariant();
        switch (value)
        {
            case "0" or "s" or "single" or "1v1" or "1vs1":
                mode = 0;
                return true;
            case "1" or "m" or "match":
                mode = 1;
                return true;
            case "2" or "t" or "tag" or "2v2" or "2vs2":
                mode = 2;
                return true;
            default:
                mode = 0;
                return false;
        }
    }

    private static string FormatMode(int mode, int matchWins)
    {
        return mode switch
        {
            1 => $"Match(Wins={matchWins})",
            2 => "Tag",
            _ => "Single"
        };
    }

    private static string FormatRegion(int region)
    {
        return region switch
        {
            0 => "OCG",
            1 => "TCG",
            2 => "OCG/TCG",
            3 => "DIY/Custom",
            4 => "NoExclusive",
            _ => "All"
        };
    }

    private void HandleBeforePlayerChat(object sender, PlayerChatInterceptEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Message))
            return;

        string trimmed = e.Message.Trim();
        if (trimmed.Length < 2 || trimmed[0] != CommandPrefix)
            return;

        if (e.Player is not Player player)
            return;

        string body = trimmed[1..].Trim();
        if (body.Length == 0)
            return;

        int spaceIndex = body.IndexOf(' ');
        string commandName = spaceIndex < 0 ? body : body[..spaceIndex];
        string rawArgs = spaceIndex < 0 ? string.Empty : body[(spaceIndex + 1)..].Trim();
        string[] args = rawArgs.Length == 0
            ? []
            : rawArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        e.Handled = true;

        if (_commands.TryGetValue(commandName, out ChatCommandDefinition? definition))
        {
            definition.Handler(new ChatCommandContext(Game, player, commandName, rawArgs, args));
        }
        else
        {
            Game.SendSystemMessage(player, $"[Commands] 未知命令 '{CommandPrefix}{commandName}'，输入 {CommandPrefix}help 查看帮助。");
        }
    }

    private static string NormalizeName(string name)
    {
        return name.Trim().TrimStart('/');
    }

    public override void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Game.OnBeforePlayerChat -= HandleBeforePlayerChat;
    }
}
