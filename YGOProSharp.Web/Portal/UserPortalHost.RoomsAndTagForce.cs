using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using YGOProSharp.AgentBridge;
using YGOProSharp.Persistence;
using YGOProSharp.Persistence.Security;
using YGOProSharp.Server;
using YGOProSharp.Web.Routing;
using YGOProSharp.Web.Security;
using YGOProSharp.Web.Services;

namespace YGOProSharp.Web;

public sealed partial class UserPortalHost
{
    [HttpGet("/api/portal/rooms", Group = "PortalCore")]
    private async Task<IResult> ListPortalRoomsAsync(CancellationToken ct)
    {
        PortalRoomSummaryDto[] rooms = await _store.Cache.GetOrCreateAsync(
            "portal",
            "public_rooms",
            _ => ValueTask.FromResult(BuildPublicRoomSummaries()),
            ttl: TimeSpan.FromSeconds(1),
            l1Ttl: TimeSpan.FromSeconds(1),
            cancellationToken: ct).ConfigureAwait(false);

        return Results.Ok(new { rooms });
    }

    [HttpGet("/api/portal/rooms/{roomName}", Group = "PortalCore")]
    private IResult GetPortalRoomDetail(string roomName)
    {
        if (!_serverHost.Server.TryGetRoom(roomName, out Game? game) || game is null)
            return Results.NotFound(new { message = $"房间 '{roomName}' 不存在或已结束。" });

        RoomActivityState? activity = _coordinator.TryGetRoomState(game.RoomName);
        return Results.Ok(new
        {
            room = new
            {
                summary = BuildSinglePublicRoomSummary(game, activity),
                startHand = game.StartHand,
                drawCount = game.DrawCount,
                timer = game.Timer,
                noCheckDeck = game.NoCheckDeck,
                noShuffleDeck = game.NoShuffleDeck,
                chats = activity?.GetChats() ?? [],
                publicEvents = activity?.GetPublicEvents() ?? []
            }
        });
    }

    [HttpPost("/api/portal/rooms", Group = "PortalCore")]
    [PortalAuth]
    [RateLimit(WebRateLimitPolicies.PortalRoomCreate)]
    private async Task<IResult> CreatePortalRoomAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        CreateRoomRequestDto body,
        CancellationToken ct)
    {
        if (!RbacPolicyEvaluator.HasAnyPermission(principal.EffectivePermissions, [RbacPermissions.RoomCreate, RbacPermissions.RoomCreateOwn]))
            return Results.Json(new { message = "当前账号无建房权限。" }, statusCode: StatusCodes.Status403Forbidden);

        if (!_serverHost.Server.IsMultiRoom)
            return Results.BadRequest(new { message = "当前服务端未开启多房间模式（MultiRoom=true），无法通过网页新建独立房间。" });

        if (_serverHost.Server.IsMaintenanceMode)
            return Results.BadRequest(new { message = "服务器当前处于维护模式，暂不开放新建房间。" });

        if (!principal.HasPermission(RbacPermissions.RoomCreate))
        {
            int ownedActive = _coordinator.CountOwnedActiveRooms(principal.UserId, _serverHost.Server);
            if (ownedActive >= 3)
                return Results.BadRequest(new { message = "每人最多同时保留 3 个自建活动房间，请先关闭旧房间后再新建。" });
        }

        string roomName = string.IsNullOrWhiteSpace(body.RoomName)
            ? $"Room-{TokenCrypto.GenerateShortCode(5)}"
            : body.RoomName.Trim();

        if (roomName.Length > 24 || roomName.Any(static c => char.IsControl(c) || c is '#' or '$' or ',' or ';'))
            return Results.BadRequest(new { message = "房间名称长度不超过 24 字符，且不要包含 # $ , ; 等符号。" });

        GameOptions roomOptions = RoomConfigHelper.ResolveCreateRoomOptions(
            _serverHost.Server.Options.Game,
            _serverHost.Server.PasswordParser,
            roomName,
            body,
            out string canonicalFullHostPassword);

        if (!_serverHost.Server.TryCreateRoom(roomName, out Game? createdRoom, roomOptions) || createdRoom is null)
            return Results.Conflict(new { message = $"无法创建房间 '{roomName}'（房名已存在或服务器房间数已满）。" });

        RoomActivityState state = _coordinator.GetOrCreateRoomState(createdRoom.RoomName);
        state.OwnerUserId = principal.UserId;
        state.OwnerUsername = principal.Username;
        state.RawJoinPassword = canonicalFullHostPassword;

        string ip = http.GetRemoteIpNormalized() ?? "unknown";
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
                    ip: ip,
                    cancellationToken: ct,
                    preferredPlayerName: customNickname).ConfigureAwait(false);

                if (!issuedOk && customNickname is not null)
                    return Results.BadRequest(new { message = issueErr });

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

        await _store.Cache.FlushNamespaceAsync("portal", ct).ConfigureAwait(false);
        await _store.Cache.FlushNamespaceAsync("rooms", ct).ConfigureAwait(false);
        await _store.AppendAuditAsync("panel_user", principal.UserId.ToString(), principal.Username, "portal.room_create", RbacPermissions.RoomCreateOwn, createdRoom.RoomName, ip, "ok", null, ct).ConfigureAwait(false);
        _coordinator.PublishEvent("room.created", new { roomName = createdRoom.RoomName });

        return Results.Ok(new
        {
            success = true,
            hostPassword = createdRoom.RoomName,
            fullHostPassword = canonicalFullHostPassword,
            room = BuildSinglePublicRoomSummary(createdRoom, state),
            issuedPlayerTokens = issuedTokens
        });
    }

    [HttpDelete("/api/portal/rooms/{roomName}", Group = "PortalCore")]
    [PortalAuth]
    private async Task<IResult> ClosePortalRoomAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        string roomName,
        CancellationToken ct)
    {
        if (!_serverHost.Server.TryGetRoom(roomName, out Game? game) || game is null)
            return Results.NotFound(new { message = $"房间 '{roomName}' 不存在或已关闭。" });

        RoomActivityState? state = _coordinator.TryGetRoomState(game.RoomName);
        bool canClose = principal.HasPermission(RbacPermissions.RoomClose) ||
                        (principal.HasPermission(RbacPermissions.RoomCreateOwn) && state?.OwnerUserId == principal.UserId);
        if (!canClose)
            return Results.Json(new { message = "仅房间创建者本人可关闭该房间。" }, statusCode: StatusCodes.Status403Forbidden);

        if (!_serverHost.Server.IsMultiRoom)
            return Results.BadRequest(new { message = "单房间模式下不支持关闭默认房间。" });

        foreach (Player? p in game.Players.ToArray())
            p?.Disconnect();
        foreach (Player obs in RoomConfigHelper.SnapshotObservers(game))
            obs.Disconnect();
        game.End();

        await _store.Cache.FlushNamespaceAsync("portal", ct).ConfigureAwait(false);
        await _store.Cache.FlushNamespaceAsync("rooms", ct).ConfigureAwait(false);
        await _store.AppendAuditAsync("panel_user", principal.UserId.ToString(), principal.Username, "portal.room_close", RbacPermissions.RoomCreateOwn, game.RoomName, http.GetRemoteIpNormalized(), "ok", null, ct).ConfigureAwait(false);
        return Results.Ok(new { success = true });
    }

    [HttpPost("/api/portal/rooms/{roomName}/partner-tactic", Group = "PortalCore")]
    [PortalAuth]
    private async Task<IResult> DispatchPortalPartnerTacticAsync(
        AuthenticatedPrincipal principal,
        HttpContext http,
        string roomName,
        PortalPartnerTacticRequestDto body,
        CancellationToken ct)
    {
        if (!_serverHost.Server.TryGetRoom(roomName, out Game? game) || game is null)
            return Results.NotFound(new { message = $"房间 '{roomName}' 不存在或已结束。" });

        if (!game.IsTag)
            return Results.BadRequest(new { message = "仅 2v2 双打模式（Mode = 2）房间支持发送搭档战术指令。" });

        RoomActivityState? roomState = _coordinator.TryGetRoomState(game.RoomName);
        bool isRoomOwnerOrAdmin = principal.HasPermission(RbacPermissions.RoomClose) ||
                                  principal.HasPermission(RbacPermissions.UserManage) ||
                                  roomState?.OwnerUserId == principal.UserId;

        int? detectedUserTeam = null;
        for (int i = 0; i < game.Players.Length; i++)
        {
            string? seatName = game.Players[i]?.Name;
            if (!string.IsNullOrWhiteSpace(seatName) &&
                (string.Equals(seatName, principal.Username, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(seatName, principal.DisplayName, StringComparison.OrdinalIgnoreCase)))
            {
                detectedUserTeam = i >= 2 ? 1 : 0;
                break;
            }
        }

        if (detectedUserTeam is null)
        {
            await using OperationalDbContext db = _store.Database.CreateDbContext();
            List<Guid> ownedTokenIds = await db.McpTokens
                .Where(t => t.OwnerUserId == principal.UserId && t.RevokedAt == null)
                .Select(t => t.Id)
                .ToListAsync(ct)
                .ConfigureAwait(false);
            if (ownedTokenIds.Count > 0)
            {
                HashSet<Guid> ownedSet = new(ownedTokenIds);
                AgentSessionManager sm = _coordinator.GetOrCreateAgentSessionManager(_serverHost, _options.NamedCardRepositoryProvider);
                AgentSessionSummary? ownedActive = sm.ListAllActiveSessions().FirstOrDefault(s =>
                    s.IsConnected &&
                    !s.IsObserver &&
                    s.SeatIndex is >= 0 and < 4 &&
                    string.Equals(s.RoomName, game.RoomName, StringComparison.OrdinalIgnoreCase) &&
                    ownedSet.Contains(s.OwnerTokenId));
                if (ownedActive is not null)
                    detectedUserTeam = ownedActive.SeatIndex >= 2 ? 1 : 0;
            }
        }

        if (!isRoomOwnerOrAdmin && detectedUserTeam is null)
        {
            return Results.Json(
                new { message = "仅房间创建者或本房间参赛玩家/AI 令牌主人可向队伍下发战术指令。" },
                statusCode: StatusCodes.Status403Forbidden);
        }

        int targetTeamId = body.TeamId is 0 or 1 ? body.TeamId.Value : (detectedUserTeam ?? 0);
        if (!isRoomOwnerOrAdmin && detectedUserTeam.HasValue && targetTeamId != detectedUserTeam.Value)
        {
            return Results.Json(
                new { message = "反剧透保护：参赛选手仅可向自己所属队伍发送双打战术方针。" },
                statusCode: StatusCodes.Status403Forbidden);
        }

        (bool ok, string msg, string normStance) = _coordinator.DispatchTagPartnerDirective(
            game,
            targetTeamId,
            body.Stance,
            body.Note,
            principal.DisplayName);

        if (!ok)
            return Results.BadRequest(new { success = false, message = msg });

        await _store.AppendAuditAsync(
            "panel_user",
            principal.UserId.ToString(),
            principal.Username,
            "portal.partner_tactic",
            null,
            $"{game.RoomName}:team{targetTeamId}",
            http.GetRemoteIpNormalized(),
            "ok",
            normStance,
            ct).ConfigureAwait(false);

        return Results.Ok(new
        {
            success = true,
            roomName = game.RoomName,
            teamId = targetTeamId,
            stance = normStance,
            message = msg
        });
    }

    private PortalRoomSummaryDto[] BuildPublicRoomSummaries()
    {
        IReadOnlyList<RoomSnapshot> snapshots = _serverHost.GetRooms();
        List<PortalRoomSummaryDto> list = new(snapshots.Count);
        foreach (RoomSnapshot snap in snapshots)
        {
            if (_serverHost.Server.TryGetRoom(snap.RoomName, out Game? g) && g is not null)
                list.Add(BuildSinglePublicRoomSummary(g, _coordinator.TryGetRoomState(g.RoomName)));
        }
        return list.ToArray();
    }

    private PortalRoomSummaryDto BuildSinglePublicRoomSummary(Game g, RoomActivityState? activity)
    {
        RoomSnapshot snap = g.CreateSnapshot();
        Player[] obsPlayers = RoomConfigHelper.SnapshotObservers(g);
        PortalRoomObserverDto[] observers = obsPlayers
            .Select(static (o, i) => new PortalRoomObserverDto(o.Name ?? $"观众{i + 1}", o.SeatOrigin))
            .ToArray();

        return new PortalRoomSummaryDto(
            snap.RoomName,
            snap.RoomName,
            RoomConfigHelper.FormatPublicRuleHostPassword(g, _serverHost.Server.Options.Game),
            activity?.OwnerUsername,
            snap.State.ToString(),
            snap.Mode,
            snap.Region,
            snap.MasterRule,
            snap.StartLp,
            snap.Team0Lp,
            snap.Team1Lp,
            snap.TurnCount,
            g.DuelCount,
            snap.DuelistCount,
            snap.MaxDuelists,
            snap.ObserverCount,
            snap.HostPlayerName,
            snap.RandomDeckMode.ToString(),
            snap.IsSuddenDeath,
            g.Banlist?.Name,
            g.Players.Select((p, idx) => new PortalRoomSlotDto(
                idx,
                g.IsTag ? (idx >= 2 ? 1 : 0) : idx,
                p?.Name,
                idx < g.IsReady.Length && g.IsReady[idx],
                p is not null && g.IsHost(p),
                p?.SeatOrigin ?? "empty")).ToArray(),
            observers);
    }
}
