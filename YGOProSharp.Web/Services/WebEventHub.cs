using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using YGOProSharp.Persistence;
using YGOProSharp.Persistence.Security;
using YGOProSharp.Web.Logging;

namespace YGOProSharp.Web.Services;

/// <summary>
/// 管理面板 WebSocket 实时事件分发中心（按连接所属用户的 RBAC 权限过滤推送内容）。
/// </summary>
public sealed class WebEventHub : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ConcurrentDictionary<Guid, ConnectedSubscriber> _subscribers = new();
    private bool _disposed;

    public int SubscriberCount => _subscribers.Count;

    public async Task HandleConnectionAsync(
        WebSocket socket,
        AuthenticatedPrincipal principal,
        CancellationToken cancellationToken)
    {
        Guid connectionId = Guid.NewGuid();
        ConnectedSubscriber subscriber = new(socket, principal);
        _subscribers[connectionId] = subscriber;

        try
        {
            await SendJsonAsync(subscriber, new
            {
                type = "connected",
                timestampUtc = DateTime.UtcNow,
                user = principal.Username,
                isStreamerOnly = principal.IsStreamerOnly
            }, cancellationToken).ConfigureAwait(false);

            byte[] receiveBuffer = new byte[1024];
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                WebSocketReceiveResult result = await socket.ReceiveAsync(receiveBuffer, cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException)
        {
        }
        finally
        {
            _subscribers.TryRemove(connectionId, out _);
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                }
            }
            subscriber.Dispose();
        }
    }

    public void BroadcastEvent(string eventType, object data, string? requiredPermission = null)
    {
        if (_disposed || _subscribers.IsEmpty)
            return;

        byte[] serialized = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = eventType,
            timestampUtc = DateTime.UtcNow,
            data
        }, JsonOptions);

        foreach (KeyValuePair<Guid, ConnectedSubscriber> pair in _subscribers)
        {
            ConnectedSubscriber sub = pair.Value;
            if (sub.Socket.State != WebSocketState.Open)
            {
                _subscribers.TryRemove(pair.Key, out _);
                continue;
            }

            if (requiredPermission is not null && !sub.Principal.HasPermission(requiredPermission))
                continue;

            _ = SendRawSafeAsync(pair.Key, sub, serialized);
        }
    }

    public void BroadcastLog(BufferedLogEntry entry)
    {
        // 主播只读角色 (streamer) 不推送底层系统日志以防直播画面意外暴露连接 IP
        if (_disposed || _subscribers.IsEmpty)
            return;

        byte[] serialized = JsonSerializer.SerializeToUtf8Bytes(new
        {
            type = "log.entry",
            timestampUtc = entry.TimestampUtc,
            data = entry
        }, JsonOptions);

        foreach (KeyValuePair<Guid, ConnectedSubscriber> pair in _subscribers)
        {
            ConnectedSubscriber sub = pair.Value;
            if (sub.Socket.State != WebSocketState.Open)
            {
                _subscribers.TryRemove(pair.Key, out _);
                continue;
            }

            if (sub.Principal.IsStreamerOnly || !sub.Principal.HasPermission(RbacPermissions.DashboardView))
                continue;

            _ = SendRawSafeAsync(pair.Key, sub, serialized);
        }
    }

    private async Task SendRawSafeAsync(Guid id, ConnectedSubscriber subscriber, byte[] utf8Json)
    {
        try
        {
            await subscriber.SendGate.WaitAsync().ConfigureAwait(false);
            try
            {
                if (subscriber.Socket.State == WebSocketState.Open)
                {
                    await subscriber.Socket.SendAsync(utf8Json, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                subscriber.SendGate.Release();
            }
        }
        catch
        {
            _subscribers.TryRemove(id, out _);
        }
    }

    private static Task SendJsonAsync(ConnectedSubscriber subscriber, object payload, CancellationToken ct)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOptions));
        return subscriber.Socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, ct);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (ConnectedSubscriber sub in _subscribers.Values)
            sub.Dispose();
        _subscribers.Clear();
    }

    private sealed class ConnectedSubscriber(WebSocket socket, AuthenticatedPrincipal principal) : IDisposable
    {
        public WebSocket Socket { get; } = socket;
        public AuthenticatedPrincipal Principal { get; } = principal;
        public SemaphoreSlim SendGate { get; } = new(1, 1);

        public void Dispose() => SendGate.Dispose();
    }
}
