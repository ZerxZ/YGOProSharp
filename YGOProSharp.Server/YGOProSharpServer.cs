using Microsoft.Extensions.Logging;
using YGOProSharp.Abstractions.Ocg;
using YGOProSharp.Core.Cards;
using YGOProSharp.Abstractions.Logging;

namespace YGOProSharp.Server;

/// <summary>
/// 主库入口（library entrypoint），由 CLI host 调用，用来串接配置、数据 provider、native runtime 和核心 tick loop。
/// </summary>
public static class YGOProSharpServer
{
    public const uint DefaultClientVersion = 0x1349;

    public static uint ClientVersion { get; private set; } = DefaultClientVersion;

    /// <summary>
    /// 启动服务循环（server loop），但不接管 logger 配置或 native runtime 构造。
    /// </summary>
    public static async Task RunAsync(ServerOptions options, IOcgRuntime runtime, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(runtime);

        ILogger logger = AppLog.CreateLogger("YGOProSharp.Server");

        string rootPath = options.RootPath;
        string scriptDirectory = options.ScriptDirectory;
        string databaseFile = options.DatabaseFile;
        string databaseFullPath = Path.Combine(Path.GetFullPath(rootPath), databaseFile);
        int port = options.Port;
        ClientVersion = options.ClientVersion;
        ICardDatabaseManager cardDatabaseManager = new SqliteCardDatabaseManager();
        logger.LogInformation(
            "Starting server with RootPath={RootPath}, ScriptDirectory={ScriptDirectory}, DatabaseFile={DatabaseFile}, ClientVersion={ClientVersion}, Port={Port}.",
            rootPath,
            scriptDirectory,
            databaseFile,
            ClientVersion,
            port);

        ICardRepository cardRepository;
        try
        {
            cardRepository = cardDatabaseManager.LoadCards(databaseFullPath);
            logger.LogInformation("Loaded card database from {DatabasePath}.", databaseFullPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load card database from {DatabasePath}.", databaseFullPath);
            throw;
        }

        BanlistManager.Init(options.BanlistFile);
        try
        {
            runtime.Initialize(new OcgRuntimeOptions(
                rootPath,
                scriptDirectory,
                databaseFile,
                new RepositoryCardDataProvider(cardRepository),
                new FileScriptProvider(rootPath, scriptDirectory)));
            logger.LogInformation("Native OCG runtime initialized.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to initialize native OCG runtime.");
            throw;
        }

        CoreServer server = new(runtime.DuelFactory, cardRepository, options);
        server.Start();
        if (server.IsRunning)
            logger.LogInformation("Server loop started.");
        else
            logger.LogWarning("Server loop was not started because the core server is not running.");

        try
        {
            while (server.IsRunning && !cancellationToken.IsCancellationRequested)
            {
                server.Tick();
                await Task.Delay(1, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Server loop cancellation requested.");
        }
        finally
        {
            if (server.IsRunning)
            {
                logger.LogInformation("Stopping server.");
                server.Stop();
            }

            logger.LogInformation("Server stopped.");
        }
    }
}
