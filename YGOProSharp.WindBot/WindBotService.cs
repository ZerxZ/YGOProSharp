using System.Collections.Concurrent;
using WindBot.Game;
using WindBot.Game.AI;
using YGOProSharp.Protocol;

namespace WindBot;

/// <summary>
/// WindBot C# 托管服务入口。
/// 除支持原有的单 Bot 运行（<see cref="RunBotAsync(WindBotInfo, CancellationToken)"/>）与 HTTP ServerMode（<see cref="RunServerModeAsync"/>）外，
/// 还提供面向 C# 宿主的进程内多 Bot 会话管理（<see cref="StartBot(WindBotInfo, CancellationToken)"/>）、
/// 自定义/内存传输直连（<see cref="StartBot(WindBotInfo, INetworkTransport, CancellationToken)"/>）以及
/// 纯包级直调客户端（<see cref="CreateDirectClient"/>）。
/// </summary>
public sealed class WindBotService : IDisposable, IAsyncDisposable
{
    private static readonly object _configureGate = new();
    private static WindBotService _lastConfiguredService;

    private readonly WindBotRuntimeOptions _options;
    private readonly ConcurrentDictionary<long, WindBotSession> _activeSessions = new();
    private bool _disposed;

    public WindBotService(WindBotRuntimeOptions options = null)
    {
        _options = options ?? new WindBotRuntimeOptions();
        EnsureConfigured(force: true);
    }

    public WindBotRuntimeOptions Options => _options;

    /// <summary>
    /// 查询当前所有可用卡组目录（含内置 C# 卡组与已注册 Lua 卡组）。
    /// </summary>
    public IReadOnlyList<DecksManager.DeckCatalogItem> GetAvailableDecks()
    {
        EnsureConfigured();
        return DecksManager.GetRegisteredDecks();
    }

    /// <summary>
    /// 获取当前正在运行中的全部托管 Bot 会话快照。
    /// </summary>
    public IReadOnlyList<WindBotSession> GetActiveSessions()
    {
        return _activeSessions.Values.ToArray();
    }

    /// <summary>
    /// 按 <see cref="WindBotSession.InstanceId"/> 查找运行中的 Bot 会话。
    /// </summary>
    public bool TryGetSession(long instanceId, out WindBotSession session)
    {
        return _activeSessions.TryGetValue(instanceId, out session);
    }

    /// <summary>
    /// 创建一个纯包级直调的 <see cref="WindBotDirectClient"/>（无需 TCP/WebSocket/HTTP 或后台线程）。
    /// </summary>
    public WindBotDirectClient CreateDirectClient(WindBotInfo info)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(info);
        EnsureConfigured();
        return new WindBotDirectClient(info, _options.CardRepository);
    }

    /// <summary>
    /// 在后台启动一个按 <paramref name="info"/> 配置连接（TCP/WebSocket）的托管 Bot 会话并立即返回控制句柄。
    /// </summary>
    public WindBotSession StartBot(WindBotInfo info, CancellationToken cancellationToken = default)
    {
        return StartBotCore(info, transport: null, cancellationToken);
    }

    /// <summary>
    /// 在后台启动一个绑定到自定义传输（如 <see cref="InMemoryNetworkTransport"/>）的托管 Bot 会话并立即返回控制句柄。
    /// </summary>
    public WindBotSession StartBot(WindBotInfo info, INetworkTransport transport, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        return StartBotCore(info, transport, cancellationToken);
    }

    /// <summary>
    /// 运行单个 Bot（走 TCP/WebSocket）直到断线或取消。
    /// </summary>
    public Task RunBotAsync(WindBotInfo info, CancellationToken cancellationToken = default)
    {
        return RunBotCoreAsync(info, transport: null, cancellationToken);
    }

    /// <summary>
    /// 在自定义传输通道（例如 <see cref="InMemoryNetworkTransport"/>）上运行单个 Bot 直到断线或取消。
    /// </summary>
    public Task RunBotAsync(WindBotInfo info, INetworkTransport transport, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        return RunBotCoreAsync(info, transport, cancellationToken);
    }

    /// <summary>
    /// 停止并等待当前服务启动的全部活跃 Bot 会话退出。
    /// </summary>
    public async Task StopAllAsync()
    {
        WindBotSession[] sessions = _activeSessions.Values.ToArray();
        if (sessions.Length == 0)
            return;

        foreach (WindBotSession session in sessions)
            session.Stop();

        await Task.WhenAll(sessions.Select(session => session.Completion)).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        StopAllAsync().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        await StopAllAsync().ConfigureAwait(false);
    }

    public static Task RunServerModeAsync(WindBotServerModeOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        WindBotService service = new WindBotService(options.RuntimeOptions);
        WindBotRuntime.ServerMode = true;
        WindBotServerModeHost host = new WindBotServerModeHost(
            options.ServerPort,
            (info, token) => service.RunBotAsync(info, token),
            options.BotListPath);

        return host.RunAsync(cancellationToken);
    }

    private WindBotSession StartBotCore(WindBotInfo info, INetworkTransport transport, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(info);
        EnsureConfigured();

        GameClient client = new GameClient(info, _options.CardRepository, transport);
        WindBotSession session = new WindBotSession(
            info,
            client,
            cancellationToken,
            instanceId => _activeSessions.TryRemove(instanceId, out _));

        _activeSessions[session.InstanceId] = session;

        _ = Task.Run(async () =>
        {
            Exception capturedError = null;
            try
            {
                session.Token.ThrowIfCancellationRequested();
                client.Start();
                Logger.DebugWriteLine(client.Username + " started.");
                session.NotifyStarted();

                while (client.IsConnected && !session.Token.IsCancellationRequested)
                {
                    try
                    {
                        client.Tick();
                        if (_options.TickDelayMilliseconds > 0)
                            await Task.Delay(_options.TickDelayMilliseconds, session.Token).ConfigureAwait(false);
                        else
                            await Task.Yield();
                    }
                    catch (OperationCanceledException) when (session.Token.IsCancellationRequested)
                    {
                        break;
                    }
#if !DEBUG
                    catch (Exception ex)
                    {
                        Logger.WriteErrorLine("Tick Error: " + ex);
                        capturedError = ex;
                    }
#endif
                }
            }
            catch (OperationCanceledException) when (session.Token.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                capturedError = ex;
                Logger.WriteErrorLine("Run Error: " + ex);
            }
            finally
            {
                Logger.DebugWriteLine(client.Username + " end.");
                session.MarkCompleted(capturedError);
            }
        }, CancellationToken.None);

        return session;
    }

    private async Task RunBotCoreAsync(WindBotInfo info, INetworkTransport transport, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(info);
        EnsureConfigured();

#if !DEBUG
        try
        {
#endif
            using GameClient client = new GameClient(info, _options.CardRepository, transport);
            client.Start();
            Logger.DebugWriteLine(client.Username + " started.");
            try
            {
                while (client.IsConnected && !cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        client.Tick();
                        if (_options.TickDelayMilliseconds > 0)
                            await Task.Delay(_options.TickDelayMilliseconds, cancellationToken).ConfigureAwait(false);
                        else
                            await Task.Yield();
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
#if !DEBUG
                    catch (Exception ex)
                    {
                        Logger.WriteErrorLine("Tick Error: " + ex);
                    }
#endif
                }
            }
            finally
            {
                client.Dispose();
                Logger.DebugWriteLine(client.Username + " end.");
            }
#if !DEBUG
        }
        catch (Exception ex)
        {
            Logger.WriteErrorLine("Run Error: " + ex);
        }
#endif
    }

    private void EnsureConfigured(bool force = false)
    {
        lock (_configureGate)
        {
            if (force || !ReferenceEquals(_lastConfiguredService, this))
            {
                WindBotRuntime.Configure(_options);
                _lastConfiguredService = this;
            }
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
