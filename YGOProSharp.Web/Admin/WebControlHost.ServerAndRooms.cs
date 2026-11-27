using Microsoft.AspNetCore.Http;
using YGOProSharp.Core;
using YGOProSharp.Persistence;
using YGOProSharp.Persistence.Caching;
using YGOProSharp.Persistence.Security;
using YGOProSharp.Protocol.Enums;
using YGOProSharp.Server;
using YGOProSharp.Web.Logging;
using YGOProSharp.Web.Routing;
using YGOProSharp.Web.Security;
using YGOProSharp.Web.Services;

namespace YGOProSharp.Web;

public sealed partial class WebControlHost
{
    [HttpGet("/api/v1/system/cache/stats")]
    [AdminAuth(RbacPermissions.DashboardView)]
    private async Task<IResult> GetSystemCacheStatsAsync(CancellationToken ct)
    {
        DatabaseHealthSnapshot dbHealth = await _store.Database.CheckHealthAsync(ct).ConfigureAwait(false);
        CacheStatsSnapshot stats = _store.Cache.GetStats();
        return Results.Ok(new
        {
            database = new
            {
                provider = dbHealth.Provider,
                healthy = dbHealth.IsHealthy,
                isHealthy = dbHealth.IsHealthy,
                latencyMs = dbHealth.LatencyMs,
                journalMode = dbHealth.JournalMode,
                activeUserCount = dbHealth.ActiveUserCount,
                activeBanCount = dbHealth.ActiveBanCount,
                totalDuelRecords = dbHealth.TotalDuelRecords,
                error = dbHealth.ErrorMessage
            },
            cache = stats
        });
    }

    [HttpPost("/api/v1/system/cache/flush")]
    [AdminAuth(RbacPermissions.SystemCacheFlush)]
    private async Task<IResult> FlushSystemCacheAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        FlushCacheRequestDto? body,
        CancellationToken ct)
    {
        string? ns = body?.Namespace;
        long generation = string.IsNullOrWhiteSpace(ns)
            ? await _store.Cache.FlushAllAsync(ct).ConfigureAwait(false)
            : await _store.Cache.FlushNamespaceAsync(ns, ct).ConfigureAwait(false);

        await _store.AppendAuditAsync("panel_user", principal.UserId.ToString(), principal.Username, "system.cache_flush", RbacPermissions.SystemCacheFlush, ns ?? "all", http.GetRemoteIpNormalized(), "ok", null, ct).ConfigureAwait(false);
        _eventHub.BroadcastEvent("cache.stats", _store.Cache.GetStats(), RbacPermissions.DashboardView);
        return Results.Ok(new { success = true, @namespace = ns ?? "all", generation });
    }

    [HttpGet("/api/v1/system/logs")]
    [AdminAuth(RbacPermissions.DashboardView)]
    private IResult GetSystemLogs(
        AuthenticatedPrincipal principal,
        int? limit,
        string? level,
        string? search)
    {
        if (!principal.CanViewSystemLogs)
            return Results.Ok(new { entries = Array.Empty<BufferedLogEntry>(), redactedForStreamer = true });

        IReadOnlyList<BufferedLogEntry> entries = _logBuffer.GetRecent(limit ?? 150, level, search);
        return Results.Ok(new { entries, redactedForStreamer = false });
    }

    [HttpGet("/api/v1/server/status")]
    [AdminAuth(RbacPermissions.DashboardView)]
    private async Task<IResult> GetServerStatusAsync(CancellationToken ct)
    {
        CoreServer srv = _serverHost.Server;
        DatabaseHealthSnapshot dbHealth = await _store.Database.CheckHealthAsync(ct).ConfigureAwait(false);
        CacheStatsSnapshot cache = _store.Cache.GetStats();
        IReadOnlyList<DiyPackageManifest> diyPackages = _diyManager.ListPackages();

        return Results.Ok(new
        {
            uptimeSeconds = (long)(DateTime.UtcNow - _startedAtUtc).TotalSeconds,
            isRunning = srv.IsRunning,
            isListening = srv.IsListening,
            port = srv.Options.Port,
            multiRoom = srv.IsMultiRoom,
            maintenanceMode = srv.IsMaintenanceMode,
            maintenanceReason = srv.MaintenanceReason,
            clientCount = srv.ClientCount,
            roomCount = srv.RoomCount,
            wsSubscribers = _eventHub.SubscriberCount,
            catalogVersion = srv.ReloadableCards.CatalogVersion,
            banlistCount = BanlistManager.Banlists.Count,
            presetDeckCount = srv.PresetDecks.Count,
            diyPackageCount = diyPackages.Count,
            enabledDiyPackageCount = diyPackages.Count(static p => p.Enabled),
            database = new
            {
                provider = dbHealth.Provider,
                healthy = dbHealth.IsHealthy,
                isHealthy = dbHealth.IsHealthy,
                latencyMs = dbHealth.LatencyMs,
                journalMode = dbHealth.JournalMode,
                activeUserCount = dbHealth.ActiveUserCount,
                activeBanCount = dbHealth.ActiveBanCount,
                totalDuelRecords = dbHealth.TotalDuelRecords,
                error = dbHealth.ErrorMessage
            },
            cache
        });
    }

    [HttpPost("/api/v1/server/maintenance")]
    [AdminAuth(RbacPermissions.ServerStop)]
    private async Task<IResult> SetMaintenanceModeAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        MaintenanceRequestDto body,
        CancellationToken ct)
    {
        _serverHost.Server.SetMaintenanceMode(body.Enabled, body.Reason);
        await _store.AppendAuditAsync("panel_user", principal.UserId.ToString(), principal.Username, "server.maintenance", RbacPermissions.ServerStop, body.Enabled ? "enable" : "disable", http.GetRemoteIpNormalized(), "ok", body.Reason, ct).ConfigureAwait(false);
        _eventHub.BroadcastEvent("server.maintenance", new { enabled = body.Enabled, reason = _serverHost.Server.MaintenanceReason }, RbacPermissions.DashboardView);
        return Results.Ok(new { success = true, maintenanceMode = _serverHost.Server.IsMaintenanceMode, reason = _serverHost.Server.MaintenanceReason });
    }

    [HttpPost("/api/v1/server/broadcast")]
    [AdminAuth(RbacPermissions.RoomShout)]
    private async Task<IResult> BroadcastServerMessageAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        MessageRequestDto body,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Message))
            return Results.BadRequest(new { success = false, message = "广播内容不能为空。" });

        string formatted = $"[公告/{principal.DisplayName}] {body.Message.Trim()}";
        _serverHost.Server.BroadcastSystemMessage(formatted);
        await _store.AppendAuditAsync("panel_user", principal.UserId.ToString(), principal.Username, "server.broadcast", RbacPermissions.RoomShout, "all_rooms", http.GetRemoteIpNormalized(), "ok", body.Message.Trim(), ct).ConfigureAwait(false);
        return Results.Ok(new { success = true });
    }

    [HttpGet("/api/v1/rooms")]
    [AdminAuth(RbacPermissions.RoomView)]
    private async Task<IResult> ListRoomsAsync(AuthenticatedPrincipal principal, CancellationToken ct)
    {
        RawRoomSummaryDto[] rawRooms = await _store.Cache.GetOrCreateAsync(
            "rooms",
            "public_list",
            _ => ValueTask.FromResult(BuildRawRoomSummaries()),
            ttl: TimeSpan.FromMilliseconds(500),
            l1Ttl: TimeSpan.FromMilliseconds(500),
            cancellationToken: ct).ConfigureAwait(false);

        object[] sanitized = rawRooms.Select(r => SanitizeRoomSummary(r, principal)).ToArray();
        return Results.Ok(new
        {
            rooms = sanitized,
            banlists = BanlistManager.Banlists.Select(static (b, idx) => new { index = idx, b.Name, b.WhitelistOnly }).ToArray(),
            availableBotDecks = _options.AvailableBotDecksProvider?.Invoke() ?? []
        });
    }

    [HttpPost("/api/v1/rooms")]
    [AdminAuth(RbacPermissions.RoomCreate, RbacPermissions.RoomCreateOwn)]
    private async Task<IResult> CreateRoomAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        CreateRoomRequestDto body,
        CancellationToken ct)
    {
        if (!_serverHost.Server.IsMultiRoom)
            return Results.BadRequest(new { success = false, message = "当前服务端未开启 EnableMultiRoom，无法动态新建独立房间。" });

        string roomName = string.IsNullOrWhiteSpace(body.RoomName)
            ? $"Arena-{TokenCrypto.GenerateShortCode(5)}"
            : body.RoomName.Trim();

        GameOptions roomOptions = RoomConfigHelper.ResolveCreateRoomOptions(
            _serverHost.Server.Options.Game,
            _serverHost.Server.PasswordParser,
            roomName,
            body,
            out string canonicalFullHostPassword);

        if (!_serverHost.Server.TryCreateRoom(roomName, out Game? createdRoom, roomOptions) || createdRoom is null)
            return Results.Conflict(new { success = false, message = $"无法创建房间 '{roomName}'（可能房名已存在、处于维护模式或已达上限）。" });

        RoomActivityState state = _coordinator.GetOrCreateRoomState(createdRoom.RoomName);
        state.OwnerUserId = principal.UserId;
        state.OwnerUsername = principal.Username;
        state.RawJoinPassword = canonicalFullHostPassword;

        List<object> issuedTokens = new();
        if (body.IssuePlayerTokens && RbacPolicyEvaluator.CanIssueMcpRole(principal.RoleCodes, principal.EffectivePermissions, McpRoleCodes.Player))
        {
            int defaultCount = createdRoom.IsTag ? 4 : 2;
            int count = Math.Clamp(body.AiTokenCount ?? defaultCount, 1, 4);
            for (int i = 0; i < count; i++)
            {
                string? customNickname = body.AiPlayerNames is not null && i < body.AiPlayerNames.Length
                    ? body.AiPlayerNames[i]?.Trim()
                    : null;
                if (string.IsNullOrWhiteSpace(customNickname))
                    customNickname = null;

                string label = customNickname ?? $"待AI自取名 ({createdRoom.RoomName})";
                (bool issuedOk, string issueErr, IssuedMcpTokenResult? issued) = await _store.IssueMcpTokenAsync(
                    principal,
                    name: label,
                    mcpRole: McpRoleCodes.Player,
                    allowedRooms: [createdRoom.RoomName],
                    maxSessions: 1,
                    requireApproval: true,
                    expiresInHours: 12,
                    ip: http.GetRemoteIpNormalized(),
                    cancellationToken: ct,
                    preferredPlayerName: customNickname).ConfigureAwait(false);

                if (!issuedOk && customNickname is not null)
                    return Results.BadRequest(new { success = false, message = issueErr });

                if (issuedOk && issued is not null)
                {
                    issuedTokens.Add(new
                    {
                        seat = i,
                        name = issued.Entity.Name,
                        preferredPlayerName = issued.Entity.PreferredPlayerName,
                        mcpRole = issued.Entity.McpRole,
                        rawToken = issued.RawToken
                    });
                }
            }
        }

        await _store.Cache.FlushNamespaceAsync("rooms", ct).ConfigureAwait(false);
        await _store.Cache.FlushNamespaceAsync("portal", ct).ConfigureAwait(false);
        await _store.AppendAuditAsync("panel_user", principal.UserId.ToString(), principal.Username, "room.create", RbacPermissions.RoomCreate, createdRoom.RoomName, http.GetRemoteIpNormalized(), "ok", null, ct).ConfigureAwait(false);

        return Results.Ok(new
        {
            success = true,
            hostPassword = principal.IsStreamerOnly ? null : createdRoom.RoomName,
            fullHostPassword = principal.IsStreamerOnly ? null : canonicalFullHostPassword,
            room = SanitizeRoomSummary(BuildSingleRawRoomSummary(createdRoom), principal),
            issuedPlayerTokens = issuedTokens
        });
    }

    [HttpGet("/api/v1/rooms/{roomName}")]
    [AdminAuth(RbacPermissions.RoomView)]
    private IResult GetRoomDetail(AuthenticatedPrincipal principal, string roomName)
    {
        if (!_serverHost.Server.TryGetRoom(roomName, out Game? game) || game is null)
            return Results.NotFound(new { message = $"房间 '{roomName}' 不存在或已结束。" });

        RoomActivityState? activity = _coordinator.TryGetRoomState(game.RoomName);
        return Results.Ok(new { room = SanitizeRoomDetail(game, activity, principal) });
    }

    [HttpPatch("/api/v1/rooms/{roomName}/rules")]
    [AdminAuth(RbacPermissions.RoomEditRules, RbacPermissions.RoomCreateOwn)]
    private async Task<IResult> UpdateRoomRulesAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        string roomName,
        UpdateRoomRulesRequestDto body,
        CancellationToken ct)
    {
        if (!_serverHost.Server.TryGetRoom(roomName, out Game? game) || game is null)
            return Results.NotFound(new { message = $"房间 '{roomName}' 不存在。" });

        if (!CanManageRoom(principal, game.RoomName, RbacPermissions.RoomEditRules))
            return Results.Json(new { message = "仅房管或房间创建者可修改该房间规则。" }, statusCode: StatusCodes.Status403Forbidden);

        RoomRuleOverrides overrides = new();
        if (!string.IsNullOrWhiteSpace(body.PresetRuleString))
        {
            string spec = body.PresetRuleString.Contains('#') ? body.PresetRuleString : body.PresetRuleString + "#";
            overrides = _serverHost.Server.PasswordParser.Parse(spec).Overrides;
        }

        if (body.Mode.HasValue) overrides.Mode = body.Mode.Value;
        if (body.Region.HasValue) overrides.Region = body.Region.Value;
        if (body.MasterRule.HasValue) overrides.MasterRule = Math.Clamp(body.MasterRule.Value, 1, 5);
        if (body.StartLp.HasValue) overrides.StartLp = Math.Clamp(body.StartLp.Value, 1, 999999);
        if (body.StartHand.HasValue) overrides.StartHand = Math.Clamp(body.StartHand.Value, 0, 40);
        if (body.DrawCount.HasValue) overrides.DrawCount = Math.Clamp(body.DrawCount.Value, 0, 35);
        if (body.GameTimer.HasValue) overrides.GameTimer = Math.Clamp(body.GameTimer.Value, 0, 9999);
        if (body.NoCheckDeck.HasValue) overrides.NoCheckDeck = body.NoCheckDeck.Value;
        if (body.NoShuffleDeck.HasValue) overrides.NoShuffleDeck = body.NoShuffleDeck.Value;
        if (body.BanlistIndex.HasValue)
        {
            if (body.BanlistIndex.Value < 0) overrides.NoBanlist = true;
            else overrides.BanlistIndex = body.BanlistIndex.Value;
        }
        if (!string.IsNullOrWhiteSpace(body.RandomDeckMode) &&
            Enum.TryParse(body.RandomDeckMode.Trim(), ignoreCase: true, out RandomDeckMode parsedRand))
        {
            overrides.RandomDeck = parsedRand;
        }

        if (!game.ApplyLobbyRuleOverrides(overrides))
            return Results.BadRequest(new { success = false, message = "当前房间已离开大厅阶段或 Tag 席位不兼容，无法修改规则。" });

        await _store.Cache.FlushNamespaceAsync("rooms", ct).ConfigureAwait(false);
        await _store.AppendAuditAsync("panel_user", principal.UserId.ToString(), principal.Username, "room.edit_rules", RbacPermissions.RoomEditRules, game.RoomName, http.GetRemoteIpNormalized(), "ok", null, ct).ConfigureAwait(false);
        _eventHub.BroadcastEvent("room.updated", new { roomName = game.RoomName, reason = "rules.changed" }, RbacPermissions.RoomView);
        return Results.Ok(new { success = true, room = SanitizeRoomDetail(game, _coordinator.TryGetRoomState(game.RoomName), principal) });
    }

    [HttpPost("/api/v1/rooms/{roomName}/death")]
    [AdminAuth(RbacPermissions.RoomStartDeath)]
    private async Task<IResult> ToggleSuddenDeathAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        string roomName,
        SuddenDeathRequestDto body,
        CancellationToken ct)
    {
        if (!_serverHost.Server.TryGetRoom(roomName, out Game? game) || game is null)
            return Results.NotFound(new { message = $"房间 '{roomName}' 不存在。" });

        bool ok;
        if (body.Enabled)
        {
            ok = game.StartSuddenDeath(body.ExtraTurns ?? 4);
            if (ok) game.BroadcastSystemMessage($"[Judge] 裁判 {principal.DisplayName} 已开启额外回合猝死规则（ED）。");
        }
        else
        {
            game.CancelSuddenDeath();
            ok = true;
            game.BroadcastSystemMessage($"[Judge] 裁判 {principal.DisplayName} 已取消额外回合猝死规则。");
        }

        await _store.Cache.FlushNamespaceAsync("rooms", ct).ConfigureAwait(false);
        await _store.AppendAuditAsync("panel_user", principal.UserId.ToString(), principal.Username, "room.sudden_death", RbacPermissions.RoomStartDeath, game.RoomName, http.GetRemoteIpNormalized(), ok ? "ok" : "error", null, ct).ConfigureAwait(false);
        return Results.Ok(new { success = ok, isSuddenDeath = game.IsSuddenDeath, targetTurn = game.SuddenDeathTargetTurn });
    }

    [HttpPost("/api/v1/rooms/{roomName}/chat")]
    [AdminAuth(RbacPermissions.RoomShout)]
    private async Task<IResult> SendRoomRefereeChatAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        string roomName,
        MessageRequestDto body,
        CancellationToken ct)
    {
        if (!_serverHost.Server.TryGetRoom(roomName, out Game? game) || game is null)
            return Results.NotFound(new { message = $"房间 '{roomName}' 不存在。" });
        if (string.IsNullOrWhiteSpace(body.Message))
            return Results.BadRequest(new { message = "消息内容不能为空。" });

        string text = $"[裁判/{principal.DisplayName}] {body.Message.Trim()}";
        game.BroadcastSystemMessage(text);
        _coordinator.GetOrCreateRoomState(game.RoomName).AddChat($"[裁判/{principal.DisplayName}]", (int)PlayerType.Yellow, body.Message.Trim());
        _eventHub.BroadcastEvent("room.chat", new { roomName = game.RoomName, sender = $"[裁判/{principal.DisplayName}]", senderType = (int)PlayerType.Yellow, message = body.Message.Trim(), timestampUtc = DateTime.UtcNow }, RbacPermissions.RoomView);
        await _store.AppendAuditAsync("panel_user", principal.UserId.ToString(), principal.Username, "room.shout", RbacPermissions.RoomShout, game.RoomName, http.GetRemoteIpNormalized(), "ok", body.Message.Trim(), ct).ConfigureAwait(false);
        return Results.Ok(new { success = true });
    }

    [HttpPost("/api/v1/rooms/{roomName}/bots")]
    [AdminAuth(RbacPermissions.BotSpawn)]
    private async Task<IResult> SpawnRoomBotsAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        string roomName,
        SpawnBotRequestDto body,
        CancellationToken ct)
    {
        if (!_serverHost.Server.TryGetRoom(roomName, out Game? game) || game is null)
            return Results.NotFound(new { message = $"房间 '{roomName}' 不存在。" });
        if (game.State != GameState.Lobby)
            return Results.BadRequest(new { message = "仅可在大厅阶段召唤内置 WindBot。" });
        if (_options.WindBotSpawner is null)
            return Results.BadRequest(new { message = "当前宿主未挂载内置 WindBot 召唤器。" });

        int emptySlots = game.Players.Count(static p => p is null);
        if (emptySlots == 0)
            return Results.BadRequest(new { message = "当前房间决斗席位已满。" });

        int count = body.FillAll ? emptySlots : Math.Clamp(body.Count ?? 1, 1, emptySlots);
        IReadOnlyList<string> spawned = _options.WindBotSpawner(game, body.Deck, body.Dialog, count);
        await _store.AppendAuditAsync("panel_user", principal.UserId.ToString(), principal.Username, "room.spawn_bot", RbacPermissions.BotSpawn, game.RoomName, http.GetRemoteIpNormalized(), "ok", string.Join(",", spawned), ct).ConfigureAwait(false);
        return Results.Ok(new { success = true, spawned });
    }

    [HttpPost("/api/v1/rooms/{roomName}/kick")]
    [AdminAuth(RbacPermissions.RoomKick)]
    private async Task<IResult> KickRoomPlayerAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        string roomName,
        KickPlayerRequestDto body,
        CancellationToken ct)
    {
        if (!_serverHost.Server.TryGetRoom(roomName, out Game? game) || game is null)
            return Results.NotFound(new { message = $"房间 '{roomName}' 不存在。" });

        Player? target = null;
        if (body.SlotIndex.HasValue && body.SlotIndex.Value >= 0 && body.SlotIndex.Value < game.Players.Length)
            target = game.Players[body.SlotIndex.Value];
        else if (!string.IsNullOrWhiteSpace(body.PlayerName))
        {
            string trimmedName = body.PlayerName.Trim();
            target = game.Players.FirstOrDefault(p => p is not null && string.Equals(p.Name, trimmedName, StringComparison.OrdinalIgnoreCase))
                     ?? RoomConfigHelper.SnapshotObservers(game).FirstOrDefault(o => string.Equals(o.Name, trimmedName, StringComparison.OrdinalIgnoreCase));
        }

        if (target is null)
            return Results.NotFound(new { message = "未找到目标玩家。" });

        string kickedName = target.Name ?? "Player";
        game.RemovePlayer(target);
        target.Disconnect();
        await _store.Cache.FlushNamespaceAsync("rooms", ct).ConfigureAwait(false);
        await _store.AppendAuditAsync("panel_user", principal.UserId.ToString(), principal.Username, "room.kick", RbacPermissions.RoomKick, $"{game.RoomName}:{kickedName}", http.GetRemoteIpNormalized(), "ok", null, ct).ConfigureAwait(false);
        return Results.Ok(new { success = true, kicked = kickedName });
    }

    [HttpDelete("/api/v1/rooms/{roomName}")]
    [AdminAuth(RbacPermissions.RoomClose, RbacPermissions.RoomCreateOwn)]
    private async Task<IResult> CloseRoomAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        string roomName,
        CancellationToken ct)
    {
        if (!_serverHost.Server.TryGetRoom(roomName, out Game? game) || game is null)
            return Results.NotFound(new { message = $"房间 '{roomName}' 不存在。" });

        if (!CanManageRoom(principal, game.RoomName, RbacPermissions.RoomClose))
            return Results.Json(new { message = "无权关闭他人创建的房间。" }, statusCode: StatusCodes.Status403Forbidden);

        if (!_serverHost.Server.IsMultiRoom)
            return Results.BadRequest(new { message = "单房间模式下不支持单独销毁默认房间。" });

        foreach (Player? p in game.Players.ToArray())
            p?.Disconnect();
        foreach (Player obs in game.Observers.ToArray())
            obs.Disconnect();
        game.End();

        await _store.Cache.FlushNamespaceAsync("rooms", ct).ConfigureAwait(false);
        await _store.AppendAuditAsync("panel_user", principal.UserId.ToString(), principal.Username, "room.close", RbacPermissions.RoomClose, game.RoomName, http.GetRemoteIpNormalized(), "ok", null, ct).ConfigureAwait(false);
        return Results.Ok(new { success = true });
    }

    private bool CanManageRoom(AuthenticatedPrincipal principal, string roomName, string globalPermission)
    {
        if (principal.HasPermission(globalPermission))
            return true;
        if (principal.HasPermission(RbacPermissions.RoomCreateOwn))
            return _coordinator.TryGetRoomState(roomName)?.OwnerUserId == principal.UserId;
        return false;
    }

    private RawRoomSummaryDto[] BuildRawRoomSummaries()
    {
        IReadOnlyList<RoomSnapshot> snapshots = _serverHost.GetRooms();
        List<RawRoomSummaryDto> list = new(snapshots.Count);
        foreach (RoomSnapshot snap in snapshots)
        {
            if (_serverHost.Server.TryGetRoom(snap.RoomName, out Game? g) && g is not null)
                list.Add(BuildSingleRawRoomSummary(g));
        }
        return list.ToArray();
    }

    private RawRoomSummaryDto BuildSingleRawRoomSummary(Game g)
    {
        RoomSnapshot snap = g.CreateSnapshot();
        RoomActivityState? activity = _coordinator.TryGetRoomState(g.RoomName);
        List<RawSlotDto> slots = new(g.Players.Length);
        for (int i = 0; i < g.Players.Length; i++)
        {
            Player? p = g.Players[i];
            slots.Add(new RawSlotDto(
                SeatIndex: i,
                TeamId: g.IsTag ? (i >= 2 ? 1 : 0) : i,
                PlayerName: p?.Name,
                IsReady: i < g.IsReady.Length && g.IsReady[i],
                IsHost: p is not null && g.IsHost(p),
                SeatOrigin: p?.SeatOrigin ?? "empty",
                ClientIp: p?.RemoteAddress));
        }

        Player[] observerPlayers = RoomConfigHelper.SnapshotObservers(g);
        List<RawObserverDto> observers = new(observerPlayers.Length);
        for (int i = 0; i < observerPlayers.Length; i++)
        {
            Player obs = observerPlayers[i];
            observers.Add(new RawObserverDto(
                obs.Name ?? $"Observer{i + 1}",
                obs.SeatOrigin,
                obs.RemoteAddress));
        }

        string fullHostPw = activity?.RawJoinPassword ?? RoomConfigHelper.FormatPublicRuleHostPassword(g, _serverHost.Server.Options.Game);

        return new RawRoomSummaryDto(
            RoomName: snap.RoomName,
            State: snap.State.ToString(),
            Mode: snap.Mode,
            Region: snap.Region,
            MasterRule: snap.MasterRule,
            StartLp: snap.StartLp,
            Team0Lp: snap.Team0Lp,
            Team1Lp: snap.Team1Lp,
            TurnCount: snap.TurnCount,
            DuelCount: g.DuelCount,
            CurrentPlayer: g.CurrentPlayer,
            DuelistCount: snap.DuelistCount,
            MaxDuelists: snap.MaxDuelists,
            ObserverCount: snap.ObserverCount,
            HostPlayerName: snap.HostPlayerName,
            RandomDeckMode: snap.RandomDeckMode.ToString(),
            IsSuddenDeath: snap.IsSuddenDeath,
            BanlistName: g.Banlist?.Name,
            NoCheckDeck: g.NoCheckDeck,
            NoShuffleDeck: g.NoShuffleDeck,
            JoinPassword: activity?.RawJoinPassword,
            FullHostPassword: fullHostPw,
            OwnerUsername: activity?.OwnerUsername,
            CreatedAtUtc: activity?.CreatedAtUtc ?? DateTime.UtcNow,
            Slots: slots,
            Observers: observers);
    }

    private static object SanitizeRoomSummary(RawRoomSummaryDto raw, AuthenticatedPrincipal principal) =>
        new
        {
            raw.RoomName,
            raw.State,
            raw.Mode,
            raw.Region,
            raw.MasterRule,
            raw.StartLp,
            raw.Team0Lp,
            raw.Team1Lp,
            raw.TurnCount,
            raw.DuelCount,
            raw.CurrentPlayer,
            raw.DuelistCount,
            raw.MaxDuelists,
            raw.ObserverCount,
            raw.HostPlayerName,
            raw.RandomDeckMode,
            raw.IsSuddenDeath,
            raw.BanlistName,
            raw.NoCheckDeck,
            raw.NoShuffleDeck,
            joinPassword = principal.CanViewPrivateRoomSecrets(raw.OwnerUsername) ? raw.JoinPassword : null,
            hostPassword = principal.IsStreamerOnly ? null : raw.RoomName,
            fullHostPassword = principal.IsStreamerOnly ? null : raw.FullHostPassword,
            raw.OwnerUsername,
            raw.CreatedAtUtc,
            slots = raw.Slots.Select(s => new
            {
                s.SeatIndex,
                s.TeamId,
                s.PlayerName,
                s.IsReady,
                s.IsHost,
                s.SeatOrigin,
                clientIp = IpPrivacyHelper.SanitizeIp(s.ClientIp, principal.CanViewIp, principal.IsStreamerOnly)
            }),
            observers = raw.Observers.Select(o => new
            {
                o.PlayerName,
                o.SeatOrigin,
                clientIp = IpPrivacyHelper.SanitizeIp(o.ClientIp, principal.CanViewIp, principal.IsStreamerOnly)
            })
        };

    private object SanitizeRoomDetail(Game game, RoomActivityState? activity, AuthenticatedPrincipal principal) =>
        new
        {
            summary = SanitizeRoomSummary(BuildSingleRawRoomSummary(game), principal),
            startHand = game.StartHand,
            drawCount = game.DrawCount,
            timer = game.Timer,
            suddenDeathTargetTurn = game.SuddenDeathTargetTurn,
            extraScripts = game.ExtraScripts,
            chats = activity?.GetChats() ?? [],
            publicEvents = activity?.GetPublicEvents() ?? []
        };

    private sealed record RawSlotDto(
        int SeatIndex,
        int TeamId,
        string? PlayerName,
        bool IsReady,
        bool IsHost,
        string SeatOrigin,
        string? ClientIp);

    private sealed record RawObserverDto(
        string PlayerName,
        string SeatOrigin,
        string? ClientIp);

    private sealed record RawRoomSummaryDto(
        string RoomName,
        string State,
        int Mode,
        int Region,
        int MasterRule,
        int StartLp,
        int Team0Lp,
        int Team1Lp,
        int TurnCount,
        int DuelCount,
        int CurrentPlayer,
        int DuelistCount,
        int MaxDuelists,
        int ObserverCount,
        string? HostPlayerName,
        string RandomDeckMode,
        bool IsSuddenDeath,
        string? BanlistName,
        bool NoCheckDeck,
        bool NoShuffleDeck,
        string? JoinPassword,
        string FullHostPassword,
        string? OwnerUsername,
        DateTime CreatedAtUtc,
        IReadOnlyList<RawSlotDto> Slots,
        IReadOnlyList<RawObserverDto> Observers);
}
