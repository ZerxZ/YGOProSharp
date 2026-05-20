using Microsoft.Extensions.Logging;
using YGOProSharp.Abstractions.Ocg;
using YGOProSharp.Cards;
using YGOProSharp.Logging;

namespace YGOProSharp;

public static class YGOProSharpServer
{
    public const uint DefaultClientVersion = 0x1349;

    public static uint ClientVersion { get; private set; } = DefaultClientVersion;

    public static async Task RunAsync(string[] args, IOcgRuntime runtime, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        ILogger logger = AppLog.CreateLogger("YGOProSharp.Server");

        Config.Load(args);

        string rootPath = Config.GetString("RootPath", ".");
        string scriptDirectory = Config.GetString("ScriptDirectory", "script");
        string databaseFile = Config.GetString("DatabaseFile", "cards.cdb");
        string databaseFullPath = Path.Combine(Path.GetFullPath(rootPath), databaseFile);
        int port = Config.GetInt("Port", CoreServer.DefaultPort);
        ClientVersion = Config.GetUInt("ClientVersion", ClientVersion);
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

        BanlistManager.Init(Config.GetString("BanlistFile", "lflist.conf"));
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

        CoreServer server = new(runtime.DuelFactory, cardRepository);
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
