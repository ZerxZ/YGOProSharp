using WindBot.Game;

namespace WindBot;

public sealed class WindBotService
{
    private readonly WindBotRuntimeOptions _options;

    public WindBotService(WindBotRuntimeOptions options = null)
    {
        _options = options ?? new WindBotRuntimeOptions();
        WindBotRuntime.Configure(_options);
    }

    public async Task RunBotAsync(WindBotInfo info, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        WindBotRuntime.Configure(_options);

#if !DEBUG
        try
        {
#endif
            GameClient client = new GameClient(info, _options.CardRepository);
            client.Start();
            Logger.DebugWriteLine(client.Username + " started.");
            try
            {
                while (client.Connection.IsConnected && !cancellationToken.IsCancellationRequested)
                {
#if !DEBUG
                    try
                    {
#endif
                        client.Tick();
                        await Task.Delay(_options.TickDelayMilliseconds, cancellationToken).ConfigureAwait(false);
#if !DEBUG
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        Logger.WriteErrorLine("Tick Error: " + ex);
                    }
#endif
                }
            }
            finally
            {
                client.Connection?.Close();
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

    public static Task RunServerModeAsync(WindBotServerModeOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        WindBotService service = new WindBotService(options.RuntimeOptions);
        WindBotServerModeHost host = new WindBotServerModeHost(
            options.ServerPort,
            (info, token) => service.RunBotAsync(info, token),
            options.BotListPath);

        return host.RunAsync(cancellationToken);
    }
}
