using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using YGOProSharp.Abstractions.Logging;

namespace YGOProSharp.Protocol;

public class NetworkClient : IDisposable
{
    public event Action? Connected;
    public event Action<Exception?>? Disconnected;
    public event Action<byte[]>? DataReceived;

    private const int BufferSize = 4096;

    private readonly byte[] _receiveBuffer = new byte[BufferSize];
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private Socket? _socket;
    private IPEndPoint? _endPoint;
    private readonly ILogger<NetworkClient> _logger = AppLog.CreateLogger<NetworkClient>();
    private CancellationTokenSource? _receiveCancellation;
    private Task? _receiveTask;
    private bool _isClosed;

    public NetworkClient()
    {
    }

    public NetworkClient(Socket socket)
    {
        Initialize(socket);
    }

    public bool IsConnected { get; private set; }

    public IPAddress RemoteIPAddress => _endPoint?.Address ?? IPAddress.None;

    public void Initialize(Socket socket)
    {
        _endPoint = (IPEndPoint?)socket.RemoteEndPoint ?? new IPEndPoint(IPAddress.None, 0);
        _socket = socket;
        _isClosed = false;
        IsConnected = true;
        _logger.LogInformation("Network client initialized for {RemoteAddress}.", RemoteIPAddress);
        Connected?.Invoke();
    }

    public async Task ConnectAsync(IPAddress address, int port, CancellationToken cancellationToken = default)
    {
        if (IsConnected || _isClosed)
            return;

        try
        {
            _endPoint = new IPEndPoint(address, port);
            _socket = new Socket(_endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            await _socket.ConnectAsync(_endPoint, cancellationToken).ConfigureAwait(false);

            IsConnected = true;
            _logger.LogInformation("Network client connected to {RemoteAddress}:{RemotePort}.", _endPoint.Address, _endPoint.Port);
            Connected?.Invoke();
            BeginReceive(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to connect network client to {RemoteAddress}:{RemotePort}.", address, port);
            Close(ex);
        }
    }

    public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
    {
        Socket socket = _socket ?? throw new InvalidOperationException("Client is not connected.");

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _logger.LogDebug("Sending {ByteCount} bytes to {RemoteAddress}.", data.Length, RemoteIPAddress);
            _logger.LogTrace("Send payload preview: {PayloadPreview}.", CreatePayloadPreview(data.Span));
            int sent = 0;
            while (sent < data.Length)
            {
                int bytesSent = await socket.SendAsync(data[sent..], SocketFlags.None, cancellationToken).ConfigureAwait(false);
                if (bytesSent == 0)
                    throw new IOException("Socket send returned 0 bytes.");

                sent += bytesSent;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Network send failed for {RemoteAddress}.", RemoteIPAddress);
            Close(ex);
            throw;
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public void BeginConnect(IPAddress address, int port)
    {
        _ = ConnectAsync(address, port);
    }

    public void BeginSend(byte[] data)
    {
        _ = SendAsync(data).ContinueWith(
            task => Close(task.Exception?.GetBaseException()),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    public void BeginReceive()
    {
        BeginReceive(CancellationToken.None);
    }

    public void BeginReceive(CancellationToken cancellationToken)
    {
        if (_socket is null || _isClosed)
            return;

        if (_receiveTask is { IsCompleted: false })
            return;

        _receiveCancellation?.Dispose();
        _receiveCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _receiveTask = Task.Run(() => ReceiveLoopAsync(_receiveCancellation.Token), CancellationToken.None);
    }

    public void Close(Exception? error = null)
    {
        if (_isClosed)
            return;

        _isClosed = true;
        IsConnected = false;

        if (error is null)
            _logger.LogInformation("Network client disconnected from {RemoteAddress}.", RemoteIPAddress);
        else
            _logger.LogWarning(error, "Network client disconnected with error from {RemoteAddress}.", RemoteIPAddress);

        try
        {
            _receiveCancellation?.Cancel();
            _socket?.Shutdown(SocketShutdown.Both);
        }
        catch (Exception ex)
        {
            error = error is null ? ex : new AggregateException(error, ex);
        }
        finally
        {
            _socket?.Dispose();
            _socket = null;
            Disconnected?.Invoke(error);
        }
    }

    public void Dispose()
    {
        Close();
        _receiveCancellation?.Dispose();
        _sendLock.Dispose();
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        Socket socket = _socket ?? throw new InvalidOperationException("Client is not connected.");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int bytesRead = await socket.ReceiveAsync(_receiveBuffer, SocketFlags.None, cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    _logger.LogInformation("Remote endpoint {RemoteAddress} closed the connection.", RemoteIPAddress);
                    Close();
                    return;
                }

                _logger.LogDebug("Received {ByteCount} bytes from {RemoteAddress}.", bytesRead, RemoteIPAddress);
                _logger.LogTrace("Receive payload preview: {PayloadPreview}.", CreatePayloadPreview(_receiveBuffer.AsSpan(0, bytesRead)));
                DataReceived?.Invoke(_receiveBuffer.AsSpan(0, bytesRead).ToArray());
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (_isClosed)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Network receive failed for {RemoteAddress}.", RemoteIPAddress);
            Close(ex);
        }
    }

    private static string CreatePayloadPreview(ReadOnlySpan<byte> payload)
    {
        const int maxPreviewLength = 32;
        ReadOnlySpan<byte> preview = payload[..Math.Min(payload.Length, maxPreviewLength)];
        string suffix = payload.Length > maxPreviewLength ? "..." : string.Empty;
        return Convert.ToHexString(preview) + suffix;
    }
}
