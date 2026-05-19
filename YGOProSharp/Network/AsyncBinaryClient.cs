using System.Net;
using System.Net.Sockets;

namespace YGOProSharp.Network;

public class AsyncBinaryClient
{
    public event Action? Connected;
    public event Action<Exception?>? Disconnected;
    public event Action<ReadOnlyMemory<byte>>? PacketReceivedRaw;
    public event Action<byte[]>? PacketReceived;

    protected int MaxPacketLength = 0xFFFF;
    protected int HeaderSize = 2;
    protected bool IsHeaderSizeIncluded = false;

    private readonly NetworkClient _client;
    private readonly PacketFramer _framer;

    public AsyncBinaryClient(NetworkClient client)
    {
        _client = client;
        _framer = new PacketFramer(MaxPacketLength, HeaderSize, IsHeaderSizeIncluded);

        client.Connected += Client_Connected;
        client.Disconnected += Client_Disconnected;
        client.DataReceived += Client_DataReceived;

        if (_client.IsConnected)
            _client.BeginReceive();
    }

    public bool IsConnected => _client.IsConnected;

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

    public void Send(byte[] packet)
    {
        Send(packet.AsSpan());
    }

    public void Send(ReadOnlySpan<byte> packet)
    {
        byte[] frame = _framer.Frame(packet);
        _client.BeginSend(frame);
    }

    public Task SendAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken = default)
    {
        byte[] frame = _framer.Frame(packet.Span);
        return _client.SendAsync(frame, cancellationToken);
    }

    public void Close(Exception? error = null)
    {
        _client.Close(error);
    }

    private void Client_Connected()
    {
        Connected?.Invoke();
    }

    private void Client_Disconnected(Exception? ex)
    {
        Disconnected?.Invoke(ex);
    }

    private void Client_DataReceived(byte[] data)
    {
        try
        {
            _framer.Append(data);
            while (_framer.TryReadPacket(out byte[] packet))
            {
                PacketReceivedRaw?.Invoke(packet);
                PacketReceived?.Invoke(packet);
            }
        }
        catch (Exception ex)
        {
            _client.Close(ex);
        }
    }
}
