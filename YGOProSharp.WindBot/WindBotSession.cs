using WindBot.Game;
using YGOProSharp.Protocol;

namespace WindBot;

/// <summary>
/// 由 <see cref="WindBotService.StartBot(WindBotInfo, CancellationToken)"/> 启动的单个 WindBot 托管会话句柄。
/// 允许 C# 宿主直接查看状态、发送聊天、投降或停止指定 Bot，无需通过 HTTP ServerMode。
/// </summary>
public sealed class WindBotSession : IDisposable, IAsyncDisposable
{
    private readonly object _clientGate = new();
    private readonly CancellationTokenSource _cts;
    private readonly TaskCompletionSource<bool> _completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Action<long> _onCompleted;
    private GameClient _client;
    private Exception _lastError;
    private bool _disposed;

    public event Action<WindBotSession> Started;
    public event Action<WindBotSession, Exception> Stopped;

    internal WindBotSession(
        WindBotInfo info,
        GameClient client,
        CancellationToken externalCancellationToken,
        Action<long> onCompleted)
    {
        Info = info ?? throw new ArgumentNullException(nameof(info));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        InstanceId = client.InstanceId;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(externalCancellationToken);
        _onCompleted = onCompleted;
    }

    public long InstanceId { get; }

    public WindBotInfo Info { get; }

    public string Name => Info.Name;

    public string Deck => Info.Deck;

    public string ExecutorName
    {
        get
        {
            lock (_clientGate)
                return _client?.ExecutorName;
        }
    }

    public string CurrentStocMessage
    {
        get
        {
            lock (_clientGate)
                return _client?.CurrentSTOCMessage;
        }
    }

    public bool IsRunning => !_completionSource.Task.IsCompleted;

    public bool IsConnected
    {
        get
        {
            lock (_clientGate)
                return _client != null && _client.IsConnected;
        }
    }

    public Exception LastError => _lastError;

    /// <summary>
    /// 会话结束时完成的任务（断线、取消或异常退出均会正常完成；异常信息记录于 <see cref="LastError"/>）。
    /// </summary>
    public Task Completion => _completionSource.Task;

    internal CancellationToken Token => _cts.Token;

    internal GameClient Client => _client;

    /// <summary>
    /// 等待会话结束；当 <paramref name="throwOnError"/> 为 <c>true</c> 且会话因异常退出时重新抛出 <see cref="LastError"/>。
    /// </summary>
    public async Task WaitForCompletionAsync(bool throwOnError = false)
    {
        await _completionSource.Task.ConfigureAwait(false);
        if (throwOnError && _lastError != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(_lastError).Throw();
    }

    /// <summary>
    /// 以该 Bot 身份发送一条房间聊天消息。
    /// </summary>
    public void Chat(string message)
    {
        GameClient client;
        lock (_clientGate)
            client = _client;

        client?.Chat(message);
    }

    /// <summary>
    /// 让该 Bot 立即发送投降消息。
    /// </summary>
    public void Surrender()
    {
        GameClient client;
        lock (_clientGate)
            client = _client;

        client?.Surrender();
    }

    /// <summary>
    /// 请求停止该 Bot 会话（异步生效，不阻塞当前线程）。
    /// </summary>
    public void Stop()
    {
        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    /// <summary>
    /// 停止该 Bot 会话并等待其彻底释放退出。
    /// </summary>
    public async Task StopAsync()
    {
        Stop();
        await _completionSource.Task.ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        StopAsync().GetAwaiter().GetResult();
        _cts.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        await StopAsync().ConfigureAwait(false);
        _cts.Dispose();
    }

    internal void NotifyStarted()
    {
        Started?.Invoke(this);
    }

    internal void MarkCompleted(Exception error)
    {
        _lastError = error;

        lock (_clientGate)
        {
            _client?.Dispose();
            _client = null;
        }

        _onCompleted?.Invoke(InstanceId);
        Stopped?.Invoke(this, error);
        _completionSource.TrySetResult(true);
    }
}
