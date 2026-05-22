using Microsoft.Extensions.Logging;
using WindBot;
using YGOProSharp.Abstractions.Logging;
using YGOProSharp.Cli;
using YGOProSharp.Core.Cards;
using YGOProSharp.NativeApi;
using YGOProSharp.Server;

using CancellationTokenSource shutdown = new();
using ILoggerFactory bootstrapLoggerFactory = CreateLoggerFactory(LogLevel.Information);
AppLog.Configure(bootstrapLoggerFactory);

ILogger logger = AppLog.CreateLogger("YGOProSharp.Cli");
ILoggerFactory? runtimeLoggerFactory = null;

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    logger.LogInformation("Shutdown requested from console.");
    shutdown.Cancel();
};

try
{
    if (args.Length == 0)
    {
        logger.LogError("Missing command. Usage: YGOProSharp.Cli server Key=Value ... | YGOProSharp.Cli windbot Key=Value ...");
        return 2;
    }

    string command = args[0].Trim().ToLowerInvariant();
    CliConfiguration configuration = CliConfiguration.Load(args.Skip(1));
    LogLevel logLevel = AppLog.ParseLevel(configuration.GetString("LogLevel"));
    runtimeLoggerFactory = CreateLoggerFactory(logLevel);
    AppLog.Configure(runtimeLoggerFactory);
    logger = AppLog.CreateLogger("YGOProSharp.Cli");

    if (configuration.GetString("LogLevel") is { } rawLogLevel &&
        !Enum.TryParse(rawLogLevel, ignoreCase: true, out LogLevel _))
    {
        logger.LogWarning("Invalid LogLevel '{LogLevel}', falling back to {DefaultLogLevel}.", rawLogLevel, logLevel);
    }

    return command switch
    {
        "server" => await RunServerAsync(configuration, shutdown.Token).ConfigureAwait(false),
        "windbot" => await RunWindBotAsync(configuration, shutdown.Token).ConfigureAwait(false),
        _ => UnknownCommand(command)
    };
}
catch (Exception ex) when (ex is not OperationCanceledException)
{
    string crashFile = $"crash_{DateTime.UtcNow:yyyy-MM-dd_HH-mm-ss}.txt";
    await File.WriteAllTextAsync(crashFile, ex.ToString());
    logger.LogCritical(ex, "YGOProSharp crashed. Crash details were written to {CrashFile}.", crashFile);
    return 1;
}
finally
{
    runtimeLoggerFactory?.Dispose();
}

static async Task<int> RunServerAsync(CliConfiguration configuration, CancellationToken cancellationToken)
{
    ILogger logger = AppLog.CreateLogger("YGOProSharp.Cli.Server");
    ServerOptions options = CliOptionsFactory.CreateServerOptions(configuration);

    if (configuration.Has("Rule") && !configuration.Has("Region"))
        logger.LogWarning("'Rule' is deprecated, please use 'Region' instead.");

    using NativeOcgRuntime runtime = new NativeOcgRuntime();
    logger.LogInformation("Starting YGOProSharp server.");
    await YGOProSharpServer.RunAsync(options, runtime, cancellationToken).ConfigureAwait(false);
    logger.LogInformation("YGOProSharp server stopped.");
    return 0;
}

static async Task<int> RunWindBotAsync(CliConfiguration configuration, CancellationToken cancellationToken)
{
    ILogger logger = AppLog.CreateLogger("YGOProSharp.Cli.WindBot");
    List<string> databasePaths = CliOptionsFactory.ResolveWindBotDatabasePaths(configuration);
    if (databasePaths.Count == 0)
    {
        logger.LogError("Can't find cards database file. Configure DbPath/DbPaths or place cards.cdb next to the working directory.");
        return 2;
    }

    SqliteCardDatabaseManager databaseManager = new SqliteCardDatabaseManager();
    INamedCardRepository cardRepository = databaseManager.LoadNamedCards(databasePaths);
    WindBotRuntimeOptions runtimeOptions = CliOptionsFactory.CreateWindBotRuntimeOptions(configuration, cardRepository);

    if (configuration.GetBool("ServerMode", false))
    {
        logger.LogInformation("Starting WindBot server mode.");
        await WindBotService.RunServerModeAsync(
            CliOptionsFactory.CreateWindBotServerModeOptions(configuration, runtimeOptions),
            cancellationToken).ConfigureAwait(false);
        return 0;
    }

    logger.LogInformation("Starting WindBot client.");
    WindBotService service = new WindBotService(runtimeOptions);
    await service.RunBotAsync(CliOptionsFactory.CreateWindBotInfo(configuration), cancellationToken).ConfigureAwait(false);
    logger.LogInformation("WindBot client stopped.");
    return 0;
}

static int UnknownCommand(string command)
{
    AppLog.CreateLogger("YGOProSharp.Cli")
        .LogError("Unknown command '{Command}'. Usage: YGOProSharp.Cli server Key=Value ... | YGOProSharp.Cli windbot Key=Value ...", command);
    return 2;
}

static ILoggerFactory CreateLoggerFactory(LogLevel logLevel)
{
    return LoggerFactory.Create(builder =>
    {
        builder.SetMinimumLevel(logLevel);
        builder.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
        });
    });
}
