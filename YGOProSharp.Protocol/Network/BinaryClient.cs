using System.IO;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using YGOProSharp.Abstractions.Logging;

namespace YGOProSharp.Protocol;

public class BinaryClient
{
    public event Action? Connected;
    public event Action<Exception?>? Disconnected;
    public event Action<ReadOnlyMemory<byte>>? PacketReceivedRaw;
    public event Action<BinaryReader>? PacketReceived;

    protected int MaxPacketLength = 0xFFFF;
    protected int HeaderSize = 2;
    protected bool IsHeaderSizeIncluded = false;

    private readonly INetworkTransport _client;
    private readonly Queue<byte[]> _pendingPackets = new();
    private readonly PacketFramer _framer;
    private readonly ILogger<BinaryClient> _logger = AppLog.CreateLogger<BinaryClient>();

    private volatile bool _wasConnected;
    private volatile bool _wasDisconnected;
    private bool _wasDisconnectedEventFired;
    private Exception? _closingException;

    public BinaryClient(NetworkClient client)
        : this((INetworkTransport)client)
    {
    }

    public BinaryClient(INetworkTransport transport)
    {
        _client = transport ?? throw new ArgumentNullException(nameof(transport));
        _framer = new PacketFramer(MaxPacketLength, HeaderSize, IsHeaderSizeIncluded);

        _client.Connected += Client_Connected;
        _client.Disconnected += Client_Disconnected;
        _client.DataReceived += Client_DataReceived;

        if (_client.IsConnected)
            _client.BeginReceive();
    }

    public bool IsConnected => !_wasDisconnectedEventFired;

    public IPAddress RemoteIPAddress => _client.RemoteIPAddress;

    public void Connect(IPAddress address, int port)
    {
        if (_client is NetworkClient tcpClient)
        {
            tcpClient.BeginConnect(address, port);
            return;
        }

        throw new InvalidOperationException("IPAddress/port Connect is only supported by TCP NetworkClient transports. Use Connect(Uri) for WebSocket transports.");
    }

    public Task ConnectAsync(IPAddress address, int port, CancellationToken cancellationToken = default)
    {
        if (_client is NetworkClient tcpClient)
            return tcpClient.ConnectAsync(address, port, cancellationToken);

        throw new InvalidOperationException("IPAddress/port ConnectAsync is only supported by TCP NetworkClient transports. Use ConnectAsync(Uri) for WebSocket transports.");
    }

    public void Connect(Uri uri)
    {
        if (_client is WebSocketNetworkClient wsClient)
        {
            wsClient.BeginConnect(uri);
            return;
        }

        throw new InvalidOperationException("URI Connect is only supported by WebSocketNetworkClient transports.");
    }

    public Task ConnectAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        if (_client is WebSocketNetworkClient wsClient)
            return wsClient.ConnectAsync(uri, cancellationToken);

        throw new InvalidOperationException("URI ConnectAsync is only supported by WebSocketNetworkClient transports.");
    }

    public void Initialize(Socket socket)
    {
        if (_client is NetworkClient tcpClient)
        {
            tcpClient.Initialize(socket);
            tcpClient.BeginReceive();
            return;
        }

        throw new InvalidOperationException("Socket Initialize is only supported by TCP NetworkClient transports.");
    }

    public void Update()
    {
        if (_wasConnected)
        {
            _wasConnected = false;
            Connected?.Invoke();
        }

        ReceivePendingPackets();

        if (_wasDisconnected && !_wasDisconnectedEventFired)
        {
            _wasDisconnectedEventFired = true;
            Disconnected?.Invoke(_closingException);
        }
    }

    public void Send(byte[] packet)
    {
        Send(packet.AsSpan());
    }

    public void Send(ReadOnlySpan<byte> packet)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Sending packet to {RemoteAddress}. Length={PacketLength}.", RemoteIPAddress, packet.Length);
        byte[] payload = _client.IsMessageFramed ? packet.ToArray() : _framer.Frame(packet);
        _client.BeginSend(payload);
    }

    public Task SendAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken = default)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Sending async packet to {RemoteAddress}. Length={PacketLength}.", RemoteIPAddress, packet.Length);
        if (_client.IsMessageFramed)
            return _client.SendAsync(packet, cancellationToken);

        byte[] frame = _framer.Frame(packet.Span);
        return _client.SendAsync(frame, cancellationToken);
    }

    public void Close(Exception? error = null)
    {
        _client.Close(error);
    }

    private void ReceivePendingPackets()
    {
        while (true)
        {
            byte[]? packet = null;
            lock (_pendingPackets)
            {
                if (_pendingPackets.Count > 0)
                    packet = _pendingPackets.Dequeue();
            }

            if (packet is null)
                return;

            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("Dispatching packet from {RemoteAddress}. Length={PacketLength}.", RemoteIPAddress, packet.Length);
            PacketReceivedRaw?.Invoke(packet);

            if (PacketReceived is null)
                continue;

            using MemoryStream stream = new(packet, writable: false);
            using BinaryReader reader = new(stream);
            PacketReceived.Invoke(reader);
        }
    }

    private void Client_Connected()
    {
        _wasConnected = true;
        _logger.LogInformation("Binary client connected to {RemoteAddress}.", RemoteIPAddress);
    }

    private void Client_Disconnected(Exception? ex)
    {
        _wasDisconnected = true;
        _closingException = ex;
        if (ex is null)
            _logger.LogInformation("Binary client disconnected from {RemoteAddress}.", RemoteIPAddress);
        else
            _logger.LogWarning(ex, "Binary client disconnected with error from {RemoteAddress}.", RemoteIPAddress);
    }

    private void Client_DataReceived(byte[] data)
    {
        try
        {
            if (_client.IsMessageFramed)
            {
                if (data.Length > MaxPacketLength)
                    throw new InvalidDataException($"Packet length {data.Length} exceeds maximum {MaxPacketLength}.");

                if (_logger.IsEnabled(LogLevel.Debug))
                    _logger.LogDebug("Queued framed packet from {RemoteAddress}. Length={PacketLength}.", RemoteIPAddress, data.Length);
                lock (_pendingPackets)
                    _pendingPackets.Enqueue(data);
                return;
            }

            _framer.Append(data);
            while (_framer.TryReadPacket(out byte[] packet))
            {
                if (_logger.IsEnabled(LogLevel.Debug))
                    _logger.LogDebug("Queued packet from {RemoteAddress}. Length={PacketLength}.", RemoteIPAddress, packet.Length);
                lock (_pendingPackets)
                    _pendingPackets.Enqueue(packet);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to frame incoming data from {RemoteAddress}.", RemoteIPAddress);
            _client.Close(ex);
        }
    }
}
