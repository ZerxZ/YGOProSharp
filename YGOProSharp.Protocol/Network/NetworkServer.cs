using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using YGOProSharp.Abstractions.Logging;

namespace YGOProSharp.Protocol;

public class NetworkServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly List<NetworkClient> _acceptedClients = new();
    private readonly List<NetworkClient> _dispatchClients = new();

    private CancellationTokenSource? _acceptCancellation;
    private Task? _acceptTask;
    private readonly ILogger<NetworkServer> _logger = AppLog.CreateLogger<NetworkServer>();
    private bool _isClosed;

    public NetworkServer(IPAddress address, int port)
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
        _logger.LogInformation("Network server listening on {Endpoint}.", _listener.LocalEndpoint);
        _acceptCancellation = new CancellationTokenSource();
        _acceptTask = Task.Run(() => AcceptLoopAsync(_acceptCancellation.Token), CancellationToken.None);
    }

    public void Close()
    {
        if (_isClosed)
            return;

        _isClosed = true;
        IsListening = false;
        _logger.LogInformation("Network server listener closing.");
        _acceptCancellation?.Cancel();
        _listener.Stop();
    }

    public void Update()
    {
        // 每 tick 快速路径：无新连接时零分配直接返回。
        lock (_acceptedClients)
        {
            if (_acceptedClients.Count == 0)
                return;

            _dispatchClients.AddRange(_acceptedClients);
            _acceptedClients.Clear();
        }

        try
        {
            foreach (NetworkClient client in _dispatchClients)
                ClientConnected?.Invoke(client);
        }
        finally
        {
            _dispatchClients.Clear();
        }
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
                _logger.LogInformation("Accepted socket from {RemoteEndPoint}.", socket.RemoteEndPoint);
                NetworkClient client = new(socket);

                lock (_acceptedClients)
                    _acceptedClients.Add(client);
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
            _logger.LogError(ex, "Network server accept loop failed.");
            Close();
        }
    }
}
