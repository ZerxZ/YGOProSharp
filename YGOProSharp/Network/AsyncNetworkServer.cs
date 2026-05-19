using System.Net;
using System.Net.Sockets;

namespace YGOProSharp.Network;

public class AsyncNetworkServer : IDisposable
{
    private readonly TcpListener _listener;

    private CancellationTokenSource? _acceptCancellation;
    private Task? _acceptTask;
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
        catch (Exception)
        {
            Close();
        }
    }
}
