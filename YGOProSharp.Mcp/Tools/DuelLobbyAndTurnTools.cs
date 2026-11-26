using System.ComponentModel;
using ModelContextProtocol.Server;
using WindBot.Game.AI;
using YGOProSharp.AgentBridge;
using YGOProSharp.Core;
using YGOProSharp.Mcp.Services;
using YGOProSharp.Persistence;
using YGOProSharp.Persistence.Security;
using YGOProSharp.Server;

namespace YGOProSharp.Mcp.Tools;

/// <summary>
/// 决斗者个人档案、大厅进退房、卡组提交、准备开局与受限视角轮询工具。
/// </summary>
public sealed class DuelLobbyAndTurnTools(McpRuntimeDependencies deps, DeckAndReplayAppService appService)
{
    [McpServerTool(Name = "duel_set_profile")]
    [McpRoles(McpRoleCodes.Spectator, McpRoleCodes.Player, McpRoleCodes.Admin)]
    [Description("查看或设置当前 AI Token 在游戏内持久绑定的决斗者昵称（2~18字符，严禁 AI1/AI2/Bot 等机械编号）与性格台词人设（personaStyle）。")]
    public async Task<ToolOutcome> DuelSetProfileAsync(
        [Description("你想使用的专属决斗者昵称（2~18字符，不要用 AI1/AI2/Bot 等机械名；留空保持不变）")] string? playerName = null,
        [Description("可选：决斗者性格与聊天吐槽风格（最长100字符）")] string? personaStyle = null,
        CancellationToken ct = default)
    {
        ValidatedMcpToken caller = McpCallerScope.Current.Caller;
        string requested = playerName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(requested) && personaStyle is null)
        {
            return ToolOutcome.Ok(new
            {
                tokenId = caller.TokenId,
                preferredPlayerName = caller.PreferredPlayerName,
                personaStyle = caller.PersonaStyle,
                needsSelfNaming = string.IsNullOrWhiteSpace(caller.PreferredPlayerName),
                hint = string.IsNullOrWhiteSpace(caller.PreferredPlayerName)
                    ? "当前尚未绑定昵称，请传入一个有个性的决斗者昵称（不要用 AI1/AI2/Bot），也可同时设置 personaStyle。"
                    : $"当前绑定昵称为「{caller.PreferredPlayerName}」，人设风格：「{caller.PersonaStyle ?? "默认"}」。"
            });
        }

        (bool ok, string msg, string? bound, string? boundPersona) = await deps.Store.BindMcpTokenProfileAsync(
            caller.TokenId,
            string.IsNullOrWhiteSpace(requested) ? null : requested,
            personaStyle,
            ct).ConfigureAwait(false);
        return ToolOutcome.From(ok, new { success = ok, message = msg, preferredPlayerName = bound, personaStyle = boundPersona });
    }

    [McpServerTool(Name = "duel_list_preset_decks")]
    [McpRoles(McpRoleCodes.Player, McpRoleCodes.Admin)]
    [Description("列出服务端预设卡组池与内置 WindBot 可用卡组（.ydk），可选按目标房间规则过滤出可直接过检的合法卡组。")]
    public ToolOutcome DuelListPresetDecks(
        [Description("可选：按指定房间的禁限卡表与 OCG/TCG 区域过滤兼容卡组")] string? roomName = null,
        [Description("若提供 roomName，是否仅返回完全符合该房间禁限表的卡组（默认 false）")] bool compatibleOnly = false)
    {
        CoreServer server = deps.ServerHost.Server;
        IReadOnlyList<PresetDeckEntry> allEntries = appService.Presets.GetCombinedPresetDecks(server);

        Game? targetRoom = null;
        if (!string.IsNullOrWhiteSpace(roomName))
            server.TryGetRoom(AgentSessionManager.ExtractTargetRoomName(roomName), out targetRoom);

        var presets = allEntries
            .Select(d =>
            {
                bool? roomLegal = targetRoom is not null
                    ? PresetDeckCatalogService.IsDeckCompatibleWithRoom(d.Data, targetRoom, server.CardRepository)
                    : null;
                return new
                {
                    d.Id,
                    d.Name,
                    d.HasDiyCards,
                    roomCompatible = roomLegal,
                    tags = d.Data.Tags,
                    mainCount = d.Data.Main.Count,
                    extraCount = d.Data.Extra.Count
                };
            })
            .Where(x => !compatibleOnly || targetRoom is null || x.roomCompatible == true)
            .ToArray();

        return ToolOutcome.Ok(new
        {
            count = presets.Length,
            presetDecks = presets,
            windBotDecks = DecksManager.GetRegisteredDeckNames()
        });
    }

    [McpServerTool(Name = "duel_join_room")]
    [McpRoles(McpRoleCodes.Player, McpRoleCodes.Admin)]
    [Description("以玩家（或观战者）身份加入指定房间。首次使用未命名 Token 时请自己取一个有个性的决斗者昵称（严禁 AI1/AI2/Bot 等机械名）传入 playerName。")]
    public async Task<ToolOutcome> DuelJoinRoomAsync(
        [Description("房间名或完整主机密码，如 Room01 或 RD#Room01")] string room,
        [Description("游戏内决斗者昵称（2~18字符，已绑定昵称时可留空沿用）")] string? playerName = null,
        [Description("是否作为观战者进入")] bool asObserver = false,
        [Description("返回盘面详略级别（minimal|standard|full，默认 standard）")] string? detail = null,
        CancellationToken ct = default)
    {
        McpCallerScope scope = McpCallerScope.Current;
        ValidatedMcpToken caller = scope.Caller;
        AgentObservationDetail obsDetail = McpArgHelpers.ParseDetail(detail, AgentObservationDetail.Standard);

        string effectiveName;
        if (!string.IsNullOrWhiteSpace(caller.PreferredPlayerName))
        {
            effectiveName = caller.PreferredPlayerName;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(playerName) || OperationalStore.IsMechanicalAiName(playerName))
            {
                return ToolOutcome.Fail(new
                {
                    success = false,
                    error = "self_naming_required",
                    needsSelfNaming = true,
                    message = "这是该 AI Token 首次登场且玩家未预设名字！请不要留空或使用 'AI1'/'AI2'/'Bot'/'Player' 等生硬编号——请构思一个帅气的名字（2~18字符）填入 playerName 再次调用 duel_join_room！"
                });
            }

            (bool boundOk, string boundMsg, string? cleanBound) = await deps.Store.BindMcpTokenPlayerNameAsync(caller.TokenId, playerName.Trim(), ct).ConfigureAwait(false);
            if (!boundOk || cleanBound is null)
                return ToolOutcome.Fail(new { success = false, error = "invalid_player_name", message = boundMsg });

            effectiveName = cleanBound;
        }

        (bool ok, string msg, AgentDuelSession? session) = await deps.SessionManager.CreateAndJoinSessionAsync(
            caller.TokenId,
            caller.AllowedRooms,
            caller.MaxSessions,
            effectiveName,
            room?.Trim() ?? string.Empty,
            asObserver,
            ct).ConfigureAwait(false);

        if (!ok || session is null)
            return ToolOutcome.Fail(new { success = false, message = msg });

        session.PersonaStyle = caller.PersonaStyle;

        if (deps.ServerHost.Server.TryGetRoom(session.RoomName, out Game? game) && game is not null)
        {
            foreach (Player? p in game.Players)
            {
                if (p is not null && string.Equals(p.Name, session.PlayerName, StringComparison.Ordinal))
                    p.SeatOrigin = "mcp_ai";
            }
            foreach (Player? obs in game.Observers)
            {
                if (obs is not null && string.Equals(obs.Name, session.PlayerName, StringComparison.Ordinal))
                    obs.SeatOrigin = "mcp_ai";
            }
        }

        await deps.Store.AppendAuditAsync(
            "mcp_token",
            caller.TokenId.ToString(),
            $"{caller.OwnerUsername}:{caller.Name}",
            "mcp.duel_join_room",
            caller.McpRole,
            session.RoomName,
            scope.RemoteIp,
            "ok",
            session.SessionId,
            ct).ConfigureAwait(false);

        return ToolOutcome.Ok(new
        {
            success = true,
            message = msg,
            sessionId = session.SessionId,
            playerName = session.PlayerName,
            roomName = session.RoomName,
            observation = session.GetObservation(obsDetail)
        });
    }

    [McpServerTool(Name = "duel_list_sessions")]
    [McpRoles(McpRoleCodes.Player, McpRoleCodes.Admin)]
    [Description("列出当前 AI Token 名下正在运行的对局会话列表。")]
    public ToolOutcome DuelListSessions()
    {
        var list = deps.SessionManager.ListOwnedSessions(McpCallerScope.Current.Caller.TokenId);
        return ToolOutcome.Ok(new { sessions = list });
    }

    [McpServerTool(Name = "duel_swap_seat")]
    [McpRoles(McpRoleCodes.Player, McpRoleCodes.Admin)]
    [Description("在大厅未准备阶段切换决斗席位/更换 2v2 队伍，或切换到观战席。")]
    public async Task<ToolOutcome> DuelSwapSeatAsync(
        string sessionId,
        bool toObserver = false,
        string? detail = null,
        CancellationToken ct = default)
    {
        (bool ok, string err, AgentDuelSession? s) = deps.SessionManager.TryGetOwnedSession(McpCallerScope.Current.Caller.TokenId, sessionId);
        if (!ok || s is null)
            return ToolOutcome.Fail(new { message = err });

        AgentObservationDetail obsDetail = McpArgHelpers.ParseDetail(detail, AgentObservationDetail.Minimal);
        (bool swapped, string msg, AgentObservation obs) = await s.SwapSeatAsync(toObserver, obsDetail, ct).ConfigureAwait(false);
        return ToolOutcome.From(swapped, new { success = swapped, message = msg, observation = obs });
    }

    [McpServerTool(Name = "duel_set_deck")]
    [McpRoles(McpRoleCodes.Player, McpRoleCodes.Admin)]
    [Description("在大厅或换副阶段提交卡组：支持 randomMode ('preset'|'chaos'|'diy') 自动按当前房间禁限表生成合法卡组，或传 presetName / ydkContent / main+extra 数组。")]
    public ToolOutcome DuelSetDeck(
        string sessionId,
        string? randomMode = null,
        string? presetName = null,
        string? ydkContent = null,
        int[]? main = null,
        int[]? extra = null,
        int[]? side = null,
        string? detail = null)
    {
        (bool ok, string err, AgentDuelSession? s) = deps.SessionManager.TryGetOwnedSession(McpCallerScope.Current.Caller.TokenId, sessionId);
        if (!ok || s is null)
            return ToolOutcome.Fail(new { message = err });

        CoreServer server = deps.ServerHost.Server;
        AgentObservationDetail obsDetail = McpArgHelpers.ParseDetail(detail, AgentObservationDetail.Minimal);
        YdkDeckData? deckData = null;
        string? selectedSource = null;

        server.TryGetRoom(s.RoomName, out Game? roomGame);
        (Banlist? roomBanlist, bool ocg, bool tcg, bool allowCustom, bool noCheck) = PresetDeckCatalogService.ExtractRoomDeckConstraints(roomGame);

        if (!string.IsNullOrWhiteSpace(ydkContent))
        {
            deckData = YdkSerializer.Parse(ydkContent.Trim(), "McpCustom");
            selectedSource = "ydkContent";
        }
        else if (!string.IsNullOrWhiteSpace(presetName))
        {
            PresetDeckEntry? found = appService.Presets.FindByNameOrId(server, presetName);
            if (found is null)
                return ToolOutcome.Fail(new { message = $"未找到预设卡组 '{presetName}'。可调用 duel_list_preset_decks 查看可用列表。" });
            deckData = found.Data;
            selectedSource = $"preset:{found.Id}";
        }
        else if (main is { Length: > 0 })
        {
            deckData = new YdkDeckData("McpArrayDeck", main, extra ?? [], side ?? [], []);
            selectedSource = "array";
        }
        else
        {
            bool wantDiy = string.Equals(randomMode?.Trim(), "diy", StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(randomMode) || string.Equals(randomMode.Trim(), "preset", StringComparison.OrdinalIgnoreCase) || wantDiy)
            {
                List<PresetDeckEntry> compatible = appService.Presets.GetCombinedPresetDecks(server)
                    .Where(e => (!wantDiy || e.HasDiyCards) && PresetDeckCatalogService.IsDeckCompatibleWithRoom(e.Data, roomGame, server.CardRepository))
                    .ToList();
                if (compatible.Count > 0)
                {
                    PresetDeckEntry chosen = compatible[Random.Shared.Next(compatible.Count)];
                    deckData = chosen.Data;
                    selectedSource = $"preset:{chosen.Id}";
                }
            }

            if (deckData is null)
            {
                RandomDeckGenerator generator = new(server.CardRepository);
                Deck generated = generator.GenerateChaosDeck(
                    banlist: noCheck ? null : roomBanlist,
                    ocg: noCheck || ocg,
                    tcg: noCheck || tcg,
                    allowCustom: noCheck || allowCustom || wantDiy,
                    options: new ChaosDraftOptions
                    {
                        MainDeckSize = 40,
                        ExtraDeckSize = 15,
                        BalancedRatio = true,
                        PreferDiyCards = wantDiy
                    },
                    random: Random.Shared);
                deckData = new YdkDeckData("McpChaosDeck", generated.Main.ToArray(), generated.Extra.ToArray(), generated.Side.ToArray(), []);
                selectedSource = "chaos_draft";
            }
        }

        (bool subOk, string subMsg) = s.SubmitDeck(deckData);
        return ToolOutcome.From(subOk, new
        {
            success = subOk,
            message = subMsg,
            deckName = deckData.Name,
            source = selectedSource,
            mainCount = deckData.Main.Count,
            extraCount = deckData.Extra.Count,
            observation = s.GetObservation(obsDetail)
        });
    }

    [McpServerTool(Name = "duel_get_deck_list")]
    [McpRoles(McpRoleCodes.Player, McpRoleCodes.Admin)]
    [Description("按需查询己方本局已提交卡组的无序张数清单与估算剩余在牌堆中的张数（不会泄露洗牌顺序）。")]
    public ToolOutcome DuelGetDeckList(string sessionId)
    {
        (bool ok, string err, AgentDuelSession? s) = deps.SessionManager.TryGetOwnedSession(McpCallerScope.Current.Caller.TokenId, sessionId);
        if (!ok || s is null)
            return ToolOutcome.Fail(new { message = err });

        IReadOnlyList<AgentDeckCountSummary> list = s.GetDeckList();
        return ToolOutcome.Ok(new { count = list.Count, cards = list });
    }

    [McpServerTool(Name = "duel_set_ready")]
    [McpRoles(McpRoleCodes.Player, McpRoleCodes.Admin)]
    [Description("在大厅准备或取消准备（自动等待服务端完成卡组禁限校验并同步最新状态）；若自己是房主且 startIfHost=true，会同时尝试开局。")]
    public async Task<ToolOutcome> DuelSetReadyAsync(
        string sessionId,
        bool ready = true,
        bool startIfHost = true,
        string? detail = null,
        CancellationToken ct = default)
    {
        (bool ok, string err, AgentDuelSession? s) = deps.SessionManager.TryGetOwnedSession(McpCallerScope.Current.Caller.TokenId, sessionId);
        if (!ok || s is null)
            return ToolOutcome.Fail(new { message = err });

        AgentObservationDetail obsDetail = McpArgHelpers.ParseDetail(detail, AgentObservationDetail.Standard);
        (bool rOk, string rMsg, AgentObservation obs) = await s.SetReadyAsync(ready, startIfHost, obsDetail, ct).ConfigureAwait(false);
        return ToolOutcome.From(rOk, new { success = rOk, message = rMsg, observation = obs });
    }

    [McpServerTool(Name = "duel_start")]
    [McpRoles(McpRoleCodes.Player, McpRoleCodes.Admin)]
    [Description("当自己是房主且全员已准备就绪时，主动发起开局并等待进入猜拳/选先后攻。")]
    public async Task<ToolOutcome> DuelStartAsync(
        string sessionId,
        string? detail = null,
        CancellationToken ct = default)
    {
        (bool ok, string err, AgentDuelSession? s) = deps.SessionManager.TryGetOwnedSession(McpCallerScope.Current.Caller.TokenId, sessionId);
        if (!ok || s is null)
            return ToolOutcome.Fail(new { message = err });

        AgentObservationDetail obsDetail = McpArgHelpers.ParseDetail(detail, AgentObservationDetail.Standard);
        (bool stOk, string stMsg, AgentObservation obs) = await s.RequestStartDuelAsync(obsDetail, ct).ConfigureAwait(false);
        return ToolOutcome.From(stOk, new { success = stOk, message = stMsg, observation = obs });
    }

    [McpServerTool(Name = "duel_wait_for_turn")]
    [McpRoles(McpRoleCodes.Player, McpRoleCodes.Admin)]
    [Description("长轮询等待直到轮到当前会话做出决策或对局结束，直接返回己方视角局面与编号化合法动作。")]
    public async Task<ToolOutcome> DuelWaitForTurnAsync(
        string sessionId,
        [Description("最长等待秒数 (1~28，默认 20)")] int timeoutSeconds = 20,
        [Description("盘面详略级别（minimal|standard|full，默认 standard）")] string? detail = null,
        CancellationToken ct = default)
    {
        (bool ok, string err, AgentDuelSession? s) = deps.SessionManager.TryGetOwnedSession(McpCallerScope.Current.Caller.TokenId, sessionId);
        if (!ok || s is null)
            return ToolOutcome.Fail(new { message = err });

        int sec = Math.Clamp(timeoutSeconds, 1, 28);
        AgentObservationDetail obsDetail = McpArgHelpers.ParseDetail(detail, AgentObservationDetail.Standard);
        AgentObservation obs = await s.WaitForTurnAsync(TimeSpan.FromSeconds(sec), obsDetail, ct).ConfigureAwait(false);
        return ToolOutcome.Ok(obs);
    }

    [McpServerTool(Name = "duel_get_observation")]
    [McpRoles(McpRoleCodes.Player, McpRoleCodes.Admin)]
    [Description("立即读取严格己方战争迷雾视角的结构化局面（默认 standard，传 detail='full' 可展开全部卡效）。")]
    public ToolOutcome DuelGetObservation(
        string sessionId,
        string? detail = null)
    {
        (bool ok, string err, AgentDuelSession? s) = deps.SessionManager.TryGetOwnedSession(McpCallerScope.Current.Caller.TokenId, sessionId);
        if (!ok || s is null)
            return ToolOutcome.Fail(new { message = err });

        AgentObservationDetail obsDetail = McpArgHelpers.ParseDetail(detail, AgentObservationDetail.Standard);
        return ToolOutcome.Ok(s.GetObservation(obsDetail));
    }

    [McpServerTool(Name = "duel_get_legal_actions")]
    [McpRoles(McpRoleCodes.Player, McpRoleCodes.Admin)]
    [Description("读取当前等待响应的 promptId、submitField（'actionIndex' 或 'cardIndices'）与编号化合法动作列表。")]
    public ToolOutcome DuelGetLegalActions(string sessionId)
    {
        (bool ok, string err, AgentDuelSession? s) = deps.SessionManager.TryGetOwnedSession(McpCallerScope.Current.Caller.TokenId, sessionId);
        if (!ok || s is null)
            return ToolOutcome.Fail(new { message = err });

        AgentPendingPrompt? prompt = s.GetPendingPrompt();
        return ToolOutcome.Ok(new { hasPendingAction = prompt is not null, pendingPrompt = prompt });
    }

    [McpServerTool(Name = "duel_chat")]
    [McpRoles(McpRoleCodes.Player, McpRoleCodes.Admin)]
    [Description("在当前所在对局房间发送聊天消息（支持与人类对手或 2v2 队友沟通）。")]
    public ToolOutcome DuelChat(string sessionId, string message)
    {
        (bool ok, string err, AgentDuelSession? s) = deps.SessionManager.TryGetOwnedSession(McpCallerScope.Current.Caller.TokenId, sessionId);
        if (!ok || s is null)
            return ToolOutcome.Fail(new { message = err });

        (bool sent, string msg) = s.SendChat(message ?? string.Empty);
        return ToolOutcome.From(sent, new { success = sent, message = msg });
    }

    [McpServerTool(Name = "duel_surrender")]
    [McpRoles(McpRoleCodes.Player, McpRoleCodes.Admin)]
    [Description("主动投降（2v2 双打下首次调用向队友发送投降确认征询，或确认队友的投降请求）。")]
    public ToolOutcome DuelSurrender(string sessionId)
    {
        (bool ok, string err, AgentDuelSession? s) = deps.SessionManager.TryGetOwnedSession(McpCallerScope.Current.Caller.TokenId, sessionId);
        if (!ok || s is null)
            return ToolOutcome.Fail(new { message = err });

        (bool surOk, string msg) = s.Surrender();
        return ToolOutcome.From(surOk, new { success = surOk, message = msg });
    }

    [McpServerTool(Name = "duel_leave")]
    [McpRoles(McpRoleCodes.Player, McpRoleCodes.Admin)]
    [Description("离开房间并关闭释放该 sessionId。")]
    public ToolOutcome DuelLeave(string sessionId)
    {
        bool closed = deps.SessionManager.CloseOwnedSession(McpCallerScope.Current.Caller.TokenId, sessionId ?? string.Empty);
        return ToolOutcome.From(closed, new { success = closed, message = closed ? "已离开房间并释放会话。" : "未找到归属当前 Token 的该会话。" });
    }
}
