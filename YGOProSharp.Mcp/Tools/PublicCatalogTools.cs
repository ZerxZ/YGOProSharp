using System.ComponentModel;
using ModelContextProtocol.Server;
using YGOProSharp.Core;
using YGOProSharp.Core.Cards;
using YGOProSharp.Persistence.Security;
using YGOProSharp.Server;

namespace YGOProSharp.Mcp.Tools;

/// <summary>
/// 公共房间列表、公开观战视角、卡库检索与禁限表查询工具（所有角色可用）。
/// </summary>
public sealed class PublicCatalogTools(McpRuntimeDependencies deps)
{
    [McpServerTool(Name = "room_list")]
    [McpRoles(McpRoleCodes.Spectator, McpRoleCodes.Player, McpRoleCodes.DiyCreator, McpRoleCodes.Admin)]
    [Description("列出当前服务器上的所有公开活动房间（含赛制 Mode、大师规则、随机卡组模式、决斗席与观战人数）。")]
    public ToolOutcome RoomList()
    {
        var rooms = deps.ServerHost.GetRooms().Select(r => new
        {
            r.RoomName,
            state = r.State.ToString(),
            r.Mode,
            isTag2v2 = r.Mode == 2,
            r.Region,
            r.MasterRule,
            r.StartLp,
            randomDeckMode = r.RandomDeckMode.ToString(),
            r.IsSuddenDeath,
            r.DuelistCount,
            r.MaxDuelists,
            r.ObserverCount,
            r.HostPlayerName,
            r.PlayerNames,
            r.TurnCount,
            r.Team0Lp,
            r.Team1Lp
        }).ToArray();

        return ToolOutcome.Ok(new { count = rooms.Length, rooms });
    }

    [McpServerTool(Name = "room_get_public_state")]
    [McpRoles(McpRoleCodes.Spectator, McpRoleCodes.Player, McpRoleCodes.DiyCreator, McpRoleCodes.Admin)]
    [Description("以观战者公开视角读取指定房间的双方 LP、回合数、决斗席与观众列表（严格隐藏所有未公开手牌、盖卡与玩家 IP）。")]
    public ToolOutcome RoomGetPublicState(
        [Description("目标房间名")] string roomName)
    {
        if (!deps.ServerHost.Server.TryGetRoom(roomName?.Trim() ?? string.Empty, out Game? game) || game is null)
            return ToolOutcome.Fail(new { message = $"房间 '{roomName}' 不存在或已结束。" });

        RoomSnapshot snap = game.CreateSnapshot();
        string[] observers = game.Observers.Where(static o => o is not null).Select(static o => o!.Name ?? "Observer").ToArray();
        return ToolOutcome.Ok(new
        {
            snap.RoomName,
            state = snap.State.ToString(),
            snap.Mode,
            snap.MasterRule,
            randomDeckMode = snap.RandomDeckMode.ToString(),
            snap.IsSuddenDeath,
            snap.TurnCount,
            snap.Team0Lp,
            snap.Team1Lp,
            slots = game.Players.Select((p, idx) => new
            {
                seatIndex = idx,
                teamId = game.IsTag ? (idx >= 2 ? 1 : 0) : idx,
                playerName = p?.Name,
                isReady = idx < game.IsReady.Length && game.IsReady[idx],
                seatOrigin = p?.SeatOrigin ?? "empty"
            }),
            observers
        });
    }

    [McpServerTool(Name = "card_get")]
    [McpRoles(McpRoleCodes.Spectator, McpRoleCodes.Player, McpRoleCodes.DiyCreator, McpRoleCodes.Admin)]
    [Description("按卡片密码（卡密 ID）查询卡片的完整名称、属性、种族、星级、攻守数值与完整效果文本（支持本服 DIY 卡）。")]
    public ToolOutcome CardGet(
        [Description("卡片正整数密码 ID")] int cardCode)
    {
        INamedCardRepository namedRepo = deps.NamedCardRepositoryProvider();
        if (cardCode > 0 && namedRepo.TryGetCard(cardCode, out NamedCard? nc) && nc is not null)
        {
            return ToolOutcome.Ok(new
            {
                nc.Id,
                nc.Name,
                nc.Description,
                nc.Type,
                level = nc.Level & 0xff,
                nc.LScale,
                nc.RScale,
                nc.Attack,
                nc.Defense,
                nc.Attribute,
                nc.Race,
                nc.Ot,
                isDiy = (nc.Ot & 0x4) != 0
            });
        }

        if (cardCode > 0 && deps.ServerHost.Server.CardRepository.TryGetCard(cardCode, out Card rawCard))
        {
            return ToolOutcome.Ok(new
            {
                rawCard.Id,
                name = $"#{rawCard.Id}",
                description = "",
                rawCard.Type,
                level = rawCard.Level & 0xff,
                rawCard.Attack,
                rawCard.Defense,
                rawCard.Attribute,
                rawCard.Race,
                rawCard.Ot
            });
        }

        return ToolOutcome.Fail(new { message = $"未找到卡密 #{cardCode}。" });
    }

    [McpServerTool(Name = "card_search")]
    [McpRoles(McpRoleCodes.Spectator, McpRoleCodes.Player, McpRoleCodes.DiyCreator, McpRoleCodes.Admin)]
    [Description("按关键词搜索卡片名称或效果文本（最多返回 25 条）。")]
    public ToolOutcome CardSearch(
        [Description("卡名或效果关键词")] string? query = null,
        bool diyOnly = false,
        int limit = 15)
    {
        int max = Math.Clamp(limit, 1, 25);
        INamedCardRepository repo = deps.NamedCardRepositoryProvider();
        IEnumerable<NamedCard> src = repo.GetAllCards();
        if (diyOnly)
            src = src.Where(static c => (c.Ot & 0x4) != 0);
        if (!string.IsNullOrWhiteSpace(query))
        {
            string q = query.Trim();
            src = src.Where(c =>
                c.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                c.Description.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        var items = src.Take(max).Select(static c => new
        {
            c.Id,
            c.Name,
            level = c.Level & 0xff,
            c.Attack,
            c.Defense,
            c.Type,
            isDiy = (c.Ot & 0x4) != 0,
            c.Description
        }).ToArray();

        return ToolOutcome.Ok(new { count = items.Length, cards = items });
    }

    [McpServerTool(Name = "banlist_list")]
    [McpRoles(McpRoleCodes.Spectator, McpRoleCodes.Player, McpRoleCodes.DiyCreator, McpRoleCodes.Admin)]
    [Description("查看当前服务器加载的禁限卡表列表。")]
    public ToolOutcome BanlistList()
    {
        var list = BanlistManager.Banlists.Select(static (b, idx) => new
        {
            index = idx,
            b.Name,
            b.WhitelistOnly,
            bannedCount = b.BannedIds.Count,
            limitedCount = b.LimitedIds.Count,
            semiLimitedCount = b.SemiLimitedIds.Count
        }).ToArray();

        return ToolOutcome.Ok(new { banlists = list });
    }
}
