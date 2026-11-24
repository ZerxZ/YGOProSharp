using System.Net;
using Microsoft.Extensions.Logging;
using YGOProSharp.Abstractions.Logging;
using YGOProSharp.Abstractions.Ocg;
using YGOProSharp.Core.Cards;
using YGOProSharp.Protocol;

namespace YGOProSharp.Server;

/// <summary>
/// 面向 C# 宿主直接调用的嵌入式服务端托管对象。
/// 支持纯内存无端口运行（<see cref="StartEmbedded"/>）、手动驱动 <see cref="Tick"/>、
/// 后台自动循环（<see cref="StartBackgroundLoop"/>）以及直接创建进程内本地连接
/// （<see cref="ConnectLocalClient"/> / <see cref="CreateLocalTransport"/>）。
/// </summary>
public sealed class YGOProSharpServerHost : IDisposable, IAsyncDisposable
{
    private readonly ILogger<YGOProSharpServerHost> _logger = AppLog.CreateLogger<YGOProSharpServerHost>();
    private readonly object _loopGate = new();
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private bool _disposed;

    public YGOProSharpServerHost(
        IDuelFactory? duelFactory = null,
        ICardRepository? cardRepository = null,
        ServerOptions? options = null)
        : this(new CoreServer(duelFactory, cardRepository, options))
    {
    }

    public YGOProSharpServerHost(CoreServer server)
    {
        Server = server ?? throw new ArgumentNullException(nameof(server));
    }

    /// <summary>
    /// 通过配置文件、卡库路径与 <see cref="IOcgRuntime"/> 创建已初始化的服务端宿主。
    /// </summary>
    public static YGOProSharpServerHost Create(ServerOptions options, IOcgRuntime runtime)
    {
        return YGOProSharpServer.CreateHost(options, runtime);
    }

    public CoreServer Server { get; }

    public Game? Game => Server.Game;

    public bool IsRunning => Server.IsRunning;

    public bool IsListening => Server.IsListening;

    public int ClientCount => Server.ClientCount;

    /// <summary>
    /// 按 <see cref="ServerOptions.EnableNetworkListener"/> 启动核心服务端（不自动开启后台循环，适合手动调用 <see cref="Tick"/>）。
    /// </summary>
    public void Start()
    {
        ThrowIfDisposed();
        Server.Start();
    }

    /// <summary>
    /// 以无 TCP 监听端口的进程内模式启动服务端（不自动开启后台循环）。
    /// </summary>
    public void StartEmbedded()
    {
        ThrowIfDisposed();
        Server.StartEmbedded();
    }

    /// <summary>
    /// 推进一次服务端协议收发与对局状态 Tick。
    /// </summary>
    public void Tick()
    {
        ThrowIfDisposed();
        Server.Tick();
    }

    /// <summary>
    /// 启动后台 Tick 循环（若尚未启动服务端，会自动按配置或 <paramref name="embedded"/> 参数启动）。
    /// </summary>
    public void StartBackgroundLoop(
        int tickDelayMilliseconds = 1,
        bool embedded = false,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (tickDelayMilliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(tickDelayMilliseconds), tickDelayMilliseconds, "Tick delay must be non-negative.");

        lock (_loopGate)
        {
            if (_loopTask is { IsCompleted: false })
                return;

            if (!Server.IsRunning)
            {
                if (embedded)
                    Server.StartEmbedded();
                else
                    Server.Start();
            }

            _loopCts?.Dispose();
            _loopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            CancellationToken token = _loopCts.Token;
            _loopTask = Task.Run(() => RunTickLoopCoreAsync(tickDelayMilliseconds, token), CancellationToken.None);
        }
    }

    /// <summary>
    /// 在当前异步调用中运行服务端循环，直至服务端停止或取消令牌触发。
    /// </summary>
    public async Task RunLoopAsync(int tickDelayMilliseconds = 1, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (tickDelayMilliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(tickDelayMilliseconds), tickDelayMilliseconds, "Tick delay must be non-negative.");

        if (!Server.IsRunning)
            Server.Start();

        await RunTickLoopCoreAsync(tickDelayMilliseconds, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 创建一个直连到当前服务端的进程内内存传输通道（客户端侧），可直接传给 WindBot 或自定义客户端。
    /// </summary>
    public InMemoryNetworkTransport CreateLocalTransport(IPAddress? remoteAddress = null)
    {
        ThrowIfDisposed();
        return Server.CreateLocalTransport(remoteAddress);
    }

    /// <summary>
    /// 创建一个直连到当前服务端的进程内 <see cref="YGOClient"/>。
    /// </summary>
    public YGOClient ConnectLocalClient(IPAddress? remoteAddress = null)
    {
        ThrowIfDisposed();
        return Server.ConnectLocalClient(remoteAddress);
    }

    /// <summary>
    /// 挂载任意自定义传输实现的 <see cref="YGOClient"/>。
    /// </summary>
    public void AddClient(YGOClient client)
    {
        ThrowIfDisposed();
        Server.AddClient(client);
    }

    /// <summary>
    /// 停止后台循环并关闭服务端。
    /// </summary>
    public async Task StopAsync()
    {
        Task? loopTask;
        CancellationTokenSource? loopCts;

        lock (_loopGate)
        {
            loopTask = _loopTask;
            loopCts = _loopCts;
            _loopTask = null;
            _loopCts = null;
        }

        if (loopCts is not null)
        {
            try
            {
                loopCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        if (loopTask is not null)
        {
            try
            {
                await loopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        loopCts?.Dispose();

        if (Server.IsRunning)
            Server.Stop();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        StopAsync().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        await StopAsync().ConfigureAwait(false);
    }

    private async Task RunTickLoopCoreAsync(int tickDelayMilliseconds, CancellationToken cancellationToken)
    {
        if (Server.IsRunning)
            _logger.LogInformation("Server loop started.");
        else
            _logger.LogWarning("Server loop was not started because the core server is not running.");

        try
        {
            while (Server.IsRunning && !cancellationToken.IsCancellationRequested)
            {
                Server.Tick();
                if (tickDelayMilliseconds > 0)
                    await Task.Delay(tickDelayMilliseconds, cancellationToken).ConfigureAwait(false);
                else
                    await Task.Yield();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Server loop cancellation requested.");
        }
        finally
        {
            if (Server.IsRunning)
            {
                _logger.LogInformation("Stopping server.");
                Server.Stop();
            }

            _logger.LogInformation("Server stopped.");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
