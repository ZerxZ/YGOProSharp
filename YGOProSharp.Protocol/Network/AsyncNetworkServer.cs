using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using YGOProSharp.Abstractions.Logging;

namespace YGOProSharp.Protocol;

public class AsyncNetworkServer : IDisposable
{
    private readonly TcpListener _listener;

    private CancellationTokenSource? _acceptCancellation;
    private Task? _acceptTask;
    private readonly ILogger<AsyncNetworkServer> _logger = AppLog.CreateLogger<AsyncNetworkServer>();
    private bool _isClosed;

    public AsyncNetworkServer(IPAddress address, int port)
    {
        _listener = new TcpListener(address, port);
    }

    public event Action<NetworkClient>? ClientConnected;

    public bool IsListening { get; private set; }

    public void Start()
    {
        if (IsListening || _isClosed)
            return;

        IsListening = true;
        _listener.Start();
        _logger.LogInformation("Async network server listening on {Endpoint}.", _listener.LocalEndpoint);
        _acceptCancellation = new CancellationTokenSource();
        _acceptTask = Task.Run(() => AcceptLoopAsync(_acceptCancellation.Token), CancellationToken.None);
    }

    public void Close()
    {
        if (_isClosed)
            return;

        _isClosed = true;
        IsListening = false;
        _logger.LogInformation("Async network server listener closing.");
        _acceptCancellation?.Cancel();
        _listener.Stop();
    }

    public void Dispose()
    {
        Close();
        _acceptCancellation?.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                Socket socket = await _listener.AcceptSocketAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogInformation("Accepted async socket from {RemoteEndPoint}.", socket.RemoteEndPoint);
                ClientConnected?.Invoke(new NetworkClient(socket));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (_isClosed)
        {
        }
        catch (SocketException) when (_isClosed)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Async network server accept loop failed.");
            Close();
        }
    }
}
