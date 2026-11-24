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

    internal static void SetClientVersion(uint clientVersion)
    {
        ClientVersion = clientVersion;
    }

    /// <summary>
    /// 根据配置加载卡库、禁限表并初始化 native runtime，返回可直接托管或手动 Tick 的 <see cref="YGOProSharpServerHost"/>。
    /// </summary>
    public static YGOProSharpServerHost CreateHost(ServerOptions options, IOcgRuntime runtime)
    {
        CoreServer server = InitializeCoreServer(options, runtime);
        return new YGOProSharpServerHost(server);
    }

    /// <summary>
    /// 启动服务循环（server loop），但不接管 logger 配置或 native runtime 构造。
    /// </summary>
    public static async Task RunAsync(ServerOptions options, IOcgRuntime runtime, CancellationToken cancellationToken = default)
    {
        using YGOProSharpServerHost host = CreateHost(options, runtime);
        await host.RunLoopAsync(tickDelayMilliseconds: 1, cancellationToken).ConfigureAwait(false);
    }

    internal static CoreServer InitializeCoreServer(ServerOptions options, IOcgRuntime runtime)
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
            "Starting server with RootPath={RootPath}, ScriptDirectory={ScriptDirectory}, DatabaseFile={DatabaseFile}, ClientVersion={ClientVersion}, Port={Port}, EnableNetworkListener={EnableNetworkListener}.",
            rootPath,
            scriptDirectory,
            databaseFile,
            ClientVersion,
            port,
            options.EnableNetworkListener);

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

        string banlistPath = Path.IsPathRooted(options.BanlistFile)
            ? options.BanlistFile
            : Path.Combine(Path.GetFullPath(rootPath), options.BanlistFile);
        if (!File.Exists(banlistPath) && File.Exists(options.BanlistFile))
            banlistPath = options.BanlistFile;

        BanlistManager.Init(banlistPath);
        logger.LogInformation("Loaded banlist from {BanlistPath}.", banlistPath);
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

        return new CoreServer(runtime.DuelFactory, cardRepository, options);
    }
}
