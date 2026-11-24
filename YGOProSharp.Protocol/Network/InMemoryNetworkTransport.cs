using System.Net;

namespace YGOProSharp.Protocol;

/// <summary>
/// 进程内双向内存传输通道，实现 <see cref="INetworkTransport"/>。
/// 用于 C# 代码在同一进程内直接收发 CTOS/STOC 包，无需经过 TCP、WebSocket 或 HTTP 端口。
/// </summary>
public sealed class InMemoryNetworkTransport : INetworkTransport
{
    public event Action? Connected;
    public event Action<Exception?>? Disconnected;
    public event Action<byte[]>? DataReceived;

    private readonly object _gate = new();
    private readonly Queue<byte[]> _pendingIncoming = new();
    private InMemoryNetworkTransport? _peer;
    private bool _isConnected;
    private bool _isClosed;
    private bool _receivingStarted;

    private InMemoryNetworkTransport(IPAddress remoteAddress)
    {
        RemoteIPAddress = remoteAddress;
    }

    public bool IsConnected
    {
        get
        {
            lock (_gate)
                return _isConnected && !_isClosed;
        }
    }

    public IPAddress RemoteIPAddress { get; }

    /// <summary>
    /// 内存传输每次收发均为完整业务包，无需额外的 2 字节长度头分帧。
    /// </summary>
    public bool IsMessageFramed => true;

    /// <summary>
    /// 创建一对互相连接的进程内内存传输端点。
    /// </summary>
    /// <param name="clientAddress">客户端侧对外显示的远端地址（服务端看到的客户端 IP，默认 <see cref="IPAddress.Loopback"/>）。</param>
    /// <param name="serverAddress">服务端侧对外显示的远端地址（客户端看到的服务端 IP，默认 <see cref="IPAddress.Loopback"/>）。</param>
    /// <param name="connected">是否在创建时即处于已连接状态（默认 <c>true</c>）。为 <c>false</c> 时需调用任一端点的 <see cref="Connect"/>。</param>
    public static (InMemoryNetworkTransport Client, InMemoryNetworkTransport Server) CreatePair(
        IPAddress? clientAddress = null,
        IPAddress? serverAddress = null,
        bool connected = true)
    {
        InMemoryNetworkTransport client = new(serverAddress ?? IPAddress.Loopback);
        InMemoryNetworkTransport server = new(clientAddress ?? IPAddress.Loopback);
        client._peer = server;
        server._peer = client;

        if (connected)
        {
            client._isConnected = true;
            server._isConnected = true;
        }

        return (client, server);
    }

    /// <summary>
    /// 建立双向连接并触发两端 <see cref="Connected"/> 事件；若已连接则为空操作。
    /// </summary>
    public void Connect()
    {
        InMemoryNetworkTransport? peer = _peer;
        if (peer is null)
            throw new InvalidOperationException("In-memory transport has no linked peer.");

        bool clientMarked = TryMarkConnected();
        bool peerMarked = peer.TryMarkConnected();

        if (clientMarked)
        {
            Connected?.Invoke();
            BeginReceive();
        }
        if (peerMarked)
        {
            peer.Connected?.Invoke();
            peer.BeginReceive();
        }
    }

    public void BeginSend(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);

        if (!TryGetConnectedPeer(out InMemoryNetworkTransport? peer))
            return;

        peer!.DeliverIncoming(data.AsSpan().ToArray());
    }

    public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!TryGetConnectedPeer(out InMemoryNetworkTransport? peer))
            throw new InvalidOperationException("In-memory transport is not connected.");

        peer!.DeliverIncoming(data.Span.ToArray());
        return Task.CompletedTask;
    }

    public void BeginReceive()
    {
        List<byte[]>? buffered = null;
        Action<byte[]>? handler;

        lock (_gate)
        {
            if (_isClosed)
                return;

            _receivingStarted = true;
            if (_pendingIncoming.Count > 0)
            {
                buffered = new List<byte[]>(_pendingIncoming.Count);
                while (_pendingIncoming.Count > 0)
                    buffered.Add(_pendingIncoming.Dequeue());
            }

            handler = DataReceived;
        }

        if (buffered is null || handler is null)
            return;

        for (int i = 0; i < buffered.Count; i++)
            handler.Invoke(buffered[i]);
    }

    public void Close(Exception? error = null)
    {
        InMemoryNetworkTransport? peerToClose = null;
        Action<Exception?>? handler = null;

        lock (_gate)
        {
            if (_isClosed)
                return;

            _isClosed = true;
            _isConnected = false;
            _pendingIncoming.Clear();
            peerToClose = _peer;
            handler = Disconnected;
        }

        handler?.Invoke(error);
        peerToClose?.CloseFromPeer();
    }

    public void Dispose()
    {
        Close();
    }

    private bool TryMarkConnected()
    {
        lock (_gate)
        {
            if (_isClosed || _isConnected)
                return false;

            _isConnected = true;
            return true;
        }
    }

    private bool TryGetConnectedPeer(out InMemoryNetworkTransport? peer)
    {
        lock (_gate)
        {
            if (_isClosed || !_isConnected || _peer is null)
            {
                peer = null;
                return false;
            }

            peer = _peer;
            return true;
        }
    }

    private void DeliverIncoming(byte[] packet)
    {
        Action<byte[]>? handler = null;

        lock (_gate)
        {
            if (_isClosed)
                return;

            if (!_receivingStarted)
            {
                _pendingIncoming.Enqueue(packet);
                return;
            }

            handler = DataReceived;
        }

        handler?.Invoke(packet);
    }

    private void CloseFromPeer()
    {
        Action<Exception?>? handler;

        lock (_gate)
        {
            if (_isClosed)
                return;

            _isClosed = true;
            _isConnected = false;
            _pendingIncoming.Clear();
            handler = Disconnected;
        }

        handler?.Invoke(null);
    }
}
