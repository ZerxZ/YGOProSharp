using System.Collections.Concurrent;
using YGOProSharp.Core.Cards;
using YGOProSharp.Protocol;

namespace YGOProSharp.AgentBridge;

/// <summary>
/// 统一托管外部 MCP AI 的对局协议会话，强制执行单 Token 会话归属隔离、房间白名单与并发席位上限。
/// </summary>
public sealed class AgentSessionManager : IDisposable
{
    private readonly Func<INetworkTransport> _transportFactory;
    private readonly Func<INamedCardRepository> _cardRepositoryProvider;
    private readonly uint _clientVersion;
    private readonly ConcurrentDictionary<string, AgentDuelSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _createGate = new();
    private bool _disposed;

    public AgentSessionManager(
        Func<INetworkTransport> transportFactory,
        Func<INamedCardRepository> cardRepositoryProvider,
        uint clientVersion = 0x1362)
    {
        _transportFactory = transportFactory ?? throw new ArgumentNullException(nameof(transportFactory));
        _cardRepositoryProvider = cardRepositoryProvider ?? throw new ArgumentNullException(nameof(cardRepositoryProvider));
        _clientVersion = clientVersion;
    }

    public event Action<AgentThoughtNotification>? OnThoughtPublished;

    public static string ExtractTargetRoomName(string joinSpecOrRoomName)
    {
        if (string.IsNullOrWhiteSpace(joinSpecOrRoomName))
            return string.Empty;

        string trimmed = joinSpecOrRoomName.Trim();
        int hashIdx = trimmed.LastIndexOf('#');
        return hashIdx >= 0 ? trimmed[(hashIdx + 1)..].Trim() : trimmed;
    }

    public async Task<(bool Ok, string Message, AgentDuelSession? Session)> CreateAndJoinSessionAsync(
        Guid ownerTokenId,
        IReadOnlyList<string> allowedRooms,
        int maxSessions,
        string playerName,
        string joinSpecOrRoomName,
        bool joinAsObserver = false,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        string joinSpec = string.IsNullOrWhiteSpace(joinSpecOrRoomName) ? string.Empty : joinSpecOrRoomName.Trim();
        string targetRoom = ExtractTargetRoomName(joinSpec);
        if (string.IsNullOrWhiteSpace(targetRoom))
            return (false, "必须指定要加入的房间名称或主机密码（例如 Arena01 或 RD#Arena01）。", null);

        if (allowedRooms.Count > 0 && !allowedRooms.Any(r => string.Equals(r, targetRoom, StringComparison.OrdinalIgnoreCase)))
        {
            return (false, $"当前 AI Token 仅被授权进入房间 [{string.Join(", ", allowedRooms)}]，禁止进入 '{targetRoom}'。", null);
        }

        AgentDuelSession session;
        lock (_createGate)
        {
            PruneDeadSessionsForToken(ownerTokenId);
            int activeCount = _sessions.Values.Count(s => s.OwnerTokenId == ownerTokenId && s.IsConnected);
            int cap = Math.Max(1, maxSessions);
            if (activeCount >= cap)
            {
                return (false, $"当前 AI Token 已达到并发房间会话上限（{cap} 个），请先调用 duel_leave 释放旧会话。", null);
            }

            string sessionId = $"duel_{Guid.NewGuid():N}"[..17];
            INetworkTransport transport = _transportFactory();
            session = new AgentDuelSession(
                sessionId,
                ownerTokenId,
                playerName,
                targetRoom,
                joinSpec,
                transport,
                _cardRepositoryProvider,
                _clientVersion,
                joinAsObserver);
            session.ThoughtEmitted += HandleSessionThought;
            _sessions[sessionId] = session;
        }

        bool joined = await session.WaitUntilJoinedAsync(TimeSpan.FromSeconds(4), cancellationToken).ConfigureAwait(false);
        if (!joined)
        {
            string? reason = session.LastError;
            CloseOwnedSession(ownerTokenId, session.SessionId);
            string detail = string.IsNullOrWhiteSpace(reason)
                ? "可能房间已满、已开局、处于维护模式或被封禁"
                : reason;
            return (false, $"加入房间 '{targetRoom}' 失败：{detail}。", null);
        }

        return (true, $"已成功加入房间 '{targetRoom}'（sessionId={session.SessionId}）。", session);
    }

    public (bool Ok, string ErrorMessage, AgentDuelSession? Session) TryGetOwnedSession(Guid callerTokenId, string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return (false, "必须传入有效的 sessionId。", null);

        if (!_sessions.TryGetValue(sessionId.Trim(), out AgentDuelSession? session))
            return (false, $"未找到对局会话 '{sessionId}'（可能已离开或已过期）。", null);

        if (session.OwnerTokenId != callerTokenId)
            return (false, "越权拒绝：禁止读取或操作其他 AI Token 的对局会话。", null);

        return (true, string.Empty, session);
    }

    public IReadOnlyList<AgentSessionSummary> ListOwnedSessions(Guid callerTokenId)
    {
        return _sessions.Values
            .Where(s => s.OwnerTokenId == callerTokenId)
            .OrderByDescending(static s => s.CreatedAtUtc)
            .Select(static s => s.CreateSummary())
            .ToArray();
    }

    public IReadOnlyList<AgentSessionSummary> ListAllActiveSessions()
    {
        return _sessions.Values
            .Where(static s => s.IsConnected)
            .OrderByDescending(static s => s.CreatedAtUtc)
            .Select(static s => s.CreateSummary())
            .ToArray();
    }

    public bool CloseOwnedSession(Guid callerTokenId, string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId.Trim(), out AgentDuelSession? existing) || existing.OwnerTokenId != callerTokenId)
            return false;

        if (_sessions.TryRemove(sessionId.Trim(), out AgentDuelSession? removed))
        {
            removed.ThoughtEmitted -= HandleSessionThought;
            removed.Dispose();
            return true;
        }

        return false;
    }

    public int RevokeSessionsByToken(Guid tokenId)
    {
        int closed = 0;
        foreach ((string key, AgentDuelSession session) in _sessions.ToArray())
        {
            if (session.OwnerTokenId != tokenId)
                continue;

            if (_sessions.TryRemove(key, out AgentDuelSession? removed))
            {
                removed.ThoughtEmitted -= HandleSessionThought;
                removed.Dispose();
                closed++;
            }
        }
        return closed;
    }

    private void PruneDeadSessionsForToken(Guid tokenId)
    {
        foreach ((string key, AgentDuelSession session) in _sessions.ToArray())
        {
            if (session.OwnerTokenId == tokenId && !session.IsConnected)
            {
                if (_sessions.TryRemove(key, out AgentDuelSession? dead))
                {
                    dead.ThoughtEmitted -= HandleSessionThought;
                    dead.Dispose();
                }
            }
        }
    }

    private void HandleSessionThought(AgentThoughtNotification notification)
    {
        OnThoughtPublished?.Invoke(notification);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        foreach ((string key, _) in _sessions.ToArray())
        {
            if (_sessions.TryRemove(key, out AgentDuelSession? s))
            {
                s.ThoughtEmitted -= HandleSessionThought;
                s.Dispose();
            }
        }
    }
}
