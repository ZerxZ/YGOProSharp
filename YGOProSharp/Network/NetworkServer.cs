using System.Net;
using System.Net.Sockets;

namespace YGOProSharp.Network;

public class NetworkServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly List<NetworkClient> _acceptedClients = new();

    private CancellationTokenSource? _acceptCancellation;
    private Task? _acceptTask;
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
        _acceptCancellation = new CancellationTokenSource();
        _acceptTask = Task.Run(() => AcceptLoopAsync(_acceptCancellation.Token), CancellationToken.None);
    }

    public void Close()
    {
        if (_isClosed)
            return;

        _isClosed = true;
        IsListening = false;
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
        catch (Exception)
        {
            Close();
        }
    }
}
