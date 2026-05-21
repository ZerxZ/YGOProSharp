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

    private readonly NetworkClient _client;
    private readonly Queue<byte[]> _pendingPackets = new();
    private readonly PacketFramer _framer;
    private readonly ILogger<BinaryClient> _logger = AppLog.CreateLogger<BinaryClient>();

    private bool _wasConnected;
    private bool _wasDisconnected;
    private bool _wasDisconnectedEventFired;
    private Exception? _closingException;

    public BinaryClient(NetworkClient client)
    {
        _client = client;
        _framer = new PacketFramer(MaxPacketLength, HeaderSize, IsHeaderSizeIncluded);

        client.Connected += Client_Connected;
        client.Disconnected += Client_Disconnected;
        client.DataReceived += Client_DataReceived;

        if (_client.IsConnected)
            _client.BeginReceive();
    }

    public bool IsConnected => !_wasDisconnectedEventFired;

    public IPAddress RemoteIPAddress => _client.RemoteIPAddress;

    public void Connect(IPAddress address, int port)
    {
        _client.BeginConnect(address, port);
    }

    public Task ConnectAsync(IPAddress address, int port, CancellationToken cancellationToken = default)
    {
        return _client.ConnectAsync(address, port, cancellationToken);
    }

    public void Initialize(Socket socket)
    {
        _client.Initialize(socket);
        _client.BeginReceive();
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
        _logger.LogDebug("Sending packet to {RemoteAddress}. Length={PacketLength}.", RemoteIPAddress, packet.Length);
        byte[] frame = _framer.Frame(packet);
        _client.BeginSend(frame);
    }

    public Task SendAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Sending async packet to {RemoteAddress}. Length={PacketLength}.", RemoteIPAddress, packet.Length);
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
            _framer.Append(data);
            while (_framer.TryReadPacket(out byte[] packet))
            {
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
