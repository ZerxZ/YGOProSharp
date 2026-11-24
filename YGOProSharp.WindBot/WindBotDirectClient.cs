using WindBot.Game;
using YGOProSharp.Core.Cards;
using YGOProSharp.Protocol;
using YGOProSharp.Protocol.Enums;

namespace WindBot;

/// <summary>
/// 面向 C# 宿主的纯包级直调 WindBot 客户端（零 Socket、零 HTTP/WS 端口、零后台线程）。
/// 调用方通过 <see cref="FeedStocPacket(ReadOnlySpan{byte}, bool)"/> 直接喂入 STOC 数据包，
/// 并通过 <see cref="CtosPacketProduced"/> 事件或 <see cref="DrainCtosPackets"/> 同步获取 WindBot 产出的 CTOS 响应包。
/// </summary>
public sealed class WindBotDirectClient : IDisposable
{
    private readonly object _outgoingGate = new();
    private readonly Queue<byte[]> _outgoingCtosPackets = new();
    private readonly InMemoryNetworkTransport _botTransport;
    private readonly InMemoryNetworkTransport _hostTransport;
    private bool _disposed;

    public event Action<ReadOnlyMemory<byte>> CtosPacketProduced;
    public event Action<Exception> Disconnected;

    public WindBotDirectClient(WindBotInfo info, INamedCardRepository cardRepository = null)
    {
        ArgumentNullException.ThrowIfNull(info);

        (_botTransport, _hostTransport) = InMemoryNetworkTransport.CreatePair(connected: true);
        _hostTransport.DataReceived += OnHostDataReceived;
        _hostTransport.Disconnected += OnHostDisconnected;
        _hostTransport.BeginReceive();

        // 纯包级直调模式无后台 Tick 循环，默认关闭聊天节流延迟以保证同步 DrainCtosPackets 即时产出。
        WindBotInfo effectiveInfo = info;
        if (info.ChatIntervalMilliseconds == null || info.DuelStartChatDelayMilliseconds == null)
        {
            effectiveInfo = new WindBotInfo
            {
                Name = info.Name,
                Deck = info.Deck,
                DeckFile = info.DeckFile,
                Dialog = info.Dialog,
                Locale = info.Locale,
                Host = info.Host,
                Port = info.Port,
                HostInfo = info.HostInfo,
                Version = info.Version,
                Hand = info.Hand,
                Debug = info.Debug,
                Chat = info.Chat,
                ChatIntervalMilliseconds = info.ChatIntervalMilliseconds ?? 0,
                DuelStartChatDelayMilliseconds = info.DuelStartChatDelayMilliseconds ?? 0,
                MaxQueuedChatMessages = info.MaxQueuedChatMessages,
                UseWebSocket = info.UseWebSocket,
                WebSocketUseWss = info.WebSocketUseWss,
                WebSocketPath = info.WebSocketPath,
                WebSocketFramingMode = info.WebSocketFramingMode
            };
        }

        Client = new GameClient(effectiveInfo, cardRepository ?? CardDatabase.Repository, _botTransport);
    }

    public GameClient Client { get; }

    public long InstanceId => Client.InstanceId;

    public string ExecutorName => Client.ExecutorName;

    public string CurrentStocMessage => Client.CurrentSTOCMessage;

    public bool IsStarted { get; private set; }

    public bool IsConnected => IsStarted && !_disposed && Client.IsConnected && _hostTransport.IsConnected;

    public int PendingCtosPacketCount
    {
        get
        {
            lock (_outgoingGate)
                return _outgoingCtosPackets.Count;
        }
    }

    /// <summary>
    /// 初始化 WindBot 运行时与卡组执行器；默认同时执行一次 <see cref="Tick"/> 以产出初始握手 CTOS 包
    ///（<see cref="CtosMessage.ExternalAddress"/>、<see cref="CtosMessage.PlayerInfo"/>、<see cref="CtosMessage.JoinGame"/>）。
    /// </summary>
    public void Start(bool pumpInitialHandshake = true)
    {
        ThrowIfDisposed();
        if (IsStarted)
            return;

        Client.Start();
        IsStarted = true;

        if (pumpInitialHandshake)
            Client.Tick();
    }

    /// <summary>
    /// 直接喂入一个完整 STOC 数据包（首字节为 <see cref="StocMessage"/>），默认立即调用 <see cref="Tick"/> 产出 CTOS 响应。
    /// </summary>
    public void FeedStocPacket(ReadOnlySpan<byte> packet, bool autoTick = true)
    {
        ThrowIfDisposed();
        if (!IsStarted)
            throw new InvalidOperationException("WindBotDirectClient must be started before feeding STOC packets.");
        if (packet.IsEmpty)
            throw new ArgumentException("STOC packet cannot be empty.", nameof(packet));

        _hostTransport.BeginSend(packet.ToArray());

        if (autoTick)
            Client.Tick();
    }

    /// <summary>
    /// 便捷重载：按 <see cref="StocMessage"/> 与负载构造完整 STOC 包并喂给 WindBot。
    /// </summary>
    public void FeedStocPacket(StocMessage message, ReadOnlySpan<byte> payload = default, bool autoTick = true)
    {
        byte[] packet = new byte[1 + payload.Length];
        packet[0] = (byte)message;
        if (!payload.IsEmpty)
            payload.CopyTo(packet.AsSpan(1));

        FeedStocPacket(packet, autoTick);
    }

    /// <summary>
    /// 手动推进一次 WindBot 协议收发与决策处理（适合在 <c>autoTick: false</c> 批量喂包后统一驱动）。
    /// </summary>
    public void Tick()
    {
        ThrowIfDisposed();
        if (!IsStarted)
            throw new InvalidOperationException("WindBotDirectClient must be started before calling Tick.");

        Client.Tick();
    }

    /// <summary>
    /// 尝试取出一条尚未消费的 CTOS 响应包。
    /// </summary>
    public bool TryDequeueCtosPacket(out byte[] packet)
    {
        lock (_outgoingGate)
        {
            if (_outgoingCtosPackets.Count > 0)
            {
                packet = _outgoingCtosPackets.Dequeue();
                return true;
            }
        }

        packet = null;
        return false;
    }

    /// <summary>
    /// 一次性取出当前队列中全部尚未消费的 CTOS 响应包。
    /// </summary>
    public IReadOnlyList<byte[]> DrainCtosPackets()
    {
        lock (_outgoingGate)
        {
            if (_outgoingCtosPackets.Count == 0)
                return Array.Empty<byte[]>();

            byte[][] packets = _outgoingCtosPackets.ToArray();
            _outgoingCtosPackets.Clear();
            return packets;
        }
    }

    public void Chat(string message)
    {
        ThrowIfDisposed();
        if (!IsStarted)
            throw new InvalidOperationException("WindBotDirectClient must be started before sending chat.");

        Client.Chat(message);
    }

    public void Surrender()
    {
        ThrowIfDisposed();
        if (!IsStarted)
            throw new InvalidOperationException("WindBotDirectClient must be started before surrendering.");

        Client.Surrender();
    }

    public void Close()
    {
        if (_disposed)
            return;

        _hostTransport.Close();
        Client.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _hostTransport.DataReceived -= OnHostDataReceived;
        _hostTransport.Disconnected -= OnHostDisconnected;
        _hostTransport.Close();
        Client.Dispose();
        _hostTransport.Dispose();
        _botTransport.Dispose();
    }

    private void OnHostDataReceived(byte[] packet)
    {
        lock (_outgoingGate)
            _outgoingCtosPackets.Enqueue(packet);

        CtosPacketProduced?.Invoke(packet);
    }

    private void OnHostDisconnected(Exception error)
    {
        Disconnected?.Invoke(error);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
