using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using YGOProSharp.Logging;

namespace YGOProSharp.Network;

public class NetworkServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly List<NetworkClient> _acceptedClients = new();

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
        List<NetworkClient> clients = new();
        lock (_acceptedClients)
        {
            clients.AddRange(_acceptedClients);
            _acceptedClients.Clear();
        }

        foreach (NetworkClient client in clients)
            ClientConnected?.Invoke(client);
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
