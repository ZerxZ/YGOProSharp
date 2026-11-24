using Microsoft.Extensions.Logging;
using WindBot;
using WindBot.Game.AI;
using YGOProSharp.Abstractions.Logging;
using YGOProSharp.Cli;
using YGOProSharp.Core.Cards;
using YGOProSharp.NativeApi;
using YGOProSharp.Persistence;
using YGOProSharp.Protocol.Enums;
using YGOProSharp.Server;
using YGOProSharp.Server.Addons;
using YGOProSharp.Web;
using YGOProSharp.Web.Logging;
using YGOProSharp.Web.Services;

using CancellationTokenSource shutdown = new();
LogRingBuffer sharedLogBuffer = new(500);
using ILoggerFactory bootstrapLoggerFactory = CreateLoggerFactory(LogLevel.Information, sharedLogBuffer);
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
    runtimeLoggerFactory = CreateLoggerFactory(logLevel, sharedLogBuffer);
    AppLog.Configure(runtimeLoggerFactory);
    logger = AppLog.CreateLogger("YGOProSharp.Cli");

    if (configuration.GetString("LogLevel") is { } rawLogLevel &&
        !Enum.TryParse(rawLogLevel, ignoreCase: true, out LogLevel _))
    {
        logger.LogWarning("Invalid LogLevel '{LogLevel}', falling back to {DefaultLogLevel}.", rawLogLevel, logLevel);
    }

    return command switch
    {
        "server" => await RunServerAsync(configuration, sharedLogBuffer, shutdown.Token).ConfigureAwait(false),
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

static async Task<int> RunServerAsync(CliConfiguration configuration, LogRingBuffer logRingBuffer, CancellationToken cancellationToken)
{
    ILogger logger = AppLog.CreateLogger("YGOProSharp.Cli.Server");
    ServerOptions baseOptions = CliOptionsFactory.CreateServerOptions(configuration);

    if (configuration.Has("Rule") && !configuration.Has("Region"))
        logger.LogWarning("'Rule' is deprecated, please use 'Region' instead.");

    YGOProSharpServerHost? activeHost = null;
    WindBotService? sharedBotService = null;
    INamedCardRepository? cachedNamedCards = null;
    long cachedNamedCardsVersion = -1;
    object botGate = new();
    int spawnedBotCounter = 0;

    List<string> CollectCardDatabasePaths()
    {
        List<string> dbPaths = new();
        foreach (string serverDbPath in YGOProSharpServer.ResolveDatabasePaths(baseOptions))
        {
            if (File.Exists(serverDbPath) && !dbPaths.Contains(serverDbPath, StringComparer.OrdinalIgnoreCase))
                dbPaths.Add(serverDbPath);
        }
        foreach (string cliDbPath in CliOptionsFactory.ResolveWindBotDatabasePaths(configuration))
        {
            if (File.Exists(cliDbPath) && !dbPaths.Contains(cliDbPath, StringComparer.OrdinalIgnoreCase))
                dbPaths.Add(cliDbPath);
        }
        return dbPaths;
    }

    INamedCardRepository GetNamedCardRepository()
    {
        long currentVer = activeHost?.Server.ReloadableCards.CatalogVersion ?? 0;
        lock (botGate)
        {
            if (cachedNamedCards is not null && cachedNamedCardsVersion == currentVer)
                return cachedNamedCards;

            List<string> dbPaths = CollectCardDatabasePaths();
            if (dbPaths.Count == 0)
                return EmptyNamedCardRepository.Instance;

            SqliteCardDatabaseManager databaseManager = new();
            cachedNamedCards = databaseManager.LoadNamedCards(dbPaths);
            cachedNamedCardsVersion = currentVer;
            return cachedNamedCards;
        }
    }

    WindBotService? GetOrCreateBotService()
    {
        lock (botGate)
        {
            if (sharedBotService is not null)
                return sharedBotService;

            List<string> botDbPaths = CollectCardDatabasePaths();
            if (botDbPaths.Count == 0)
                return null;

            SqliteCardDatabaseManager databaseManager = new();
            INamedCardRepository botCardRepository = databaseManager.LoadNamedCards(botDbPaths);
            WindBotRuntimeOptions botRuntimeOptions = CliOptionsFactory.CreateWindBotRuntimeOptions(configuration, botCardRepository);
            sharedBotService = new WindBotService(botRuntimeOptions);
            return sharedBotService;
        }
    }

    List<string> CollectAvailableBotDeckNames()
    {
        HashSet<string> names = new(DecksManager.GetRegisteredDeckNames(), StringComparer.OrdinalIgnoreCase);
        string[] luaDirs =
        [
            Path.Combine("Decks", "Lua"),
            Path.Combine(AppContext.BaseDirectory, "Decks", "Lua"),
            Path.Combine(baseOptions.RootPath, "Decks", "Lua"),
            Path.Combine(baseOptions.RootPath, "windbot", "Decks", "Lua")
        ];
        foreach (string dir in luaDirs)
        {
            if (!Directory.Exists(dir))
                continue;
            foreach (string file in Directory.EnumerateFiles(dir, "*.lua", SearchOption.TopDirectoryOnly))
                names.Add(Path.GetFileNameWithoutExtension(file));
        }

        return names.OrderBy(static n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    IReadOnlyList<string> SpawnBotsIntoRoom(Game targetGame, string? requestedDeck, string? requestedDialog, int requestedCount)
    {
        if (activeHost is null || !activeHost.IsRunning)
            throw new InvalidOperationException("服务端尚未就绪。");
        if (targetGame.State != GameState.Lobby)
            throw new InvalidOperationException("仅可在大厅阶段召唤 AI。");

        int emptySlots = targetGame.Players.Count(p => p is null);
        if (emptySlots == 0)
            throw new InvalidOperationException("当前房间决斗席位已满。");

        WindBotService? botService = GetOrCreateBotService();
        if (botService is null)
            throw new InvalidOperationException("未找到可用卡库，无法启动内置 WindBot。");

        int spawnCount = Math.Clamp(requestedCount, 1, emptySlots);
        WindBotInfo templateInfo = CliOptionsFactory.CreateServerBotInfo(configuration, baseOptions);
        string? effectiveDeck = !string.IsNullOrWhiteSpace(requestedDeck) ? requestedDeck : templateInfo.Deck;
        string? effectiveDialog = !string.IsNullOrWhiteSpace(requestedDialog) ? requestedDialog : templateInfo.Dialog;
        bool pickRandomDeck = effectiveDeck is not null &&
            (effectiveDeck.Equals("random", StringComparison.OrdinalIgnoreCase) ||
             effectiveDeck.Equals("rand", StringComparison.OrdinalIgnoreCase));
        List<string>? availablePool = pickRandomDeck ? CollectAvailableBotDeckNames() : null;

        List<string> spawnedNames = new(spawnCount);
        for (int i = 0; i < spawnCount; i++)
        {
            WindBotInfo botInfo = CliOptionsFactory.CreateServerBotInfo(configuration, baseOptions);
            int botSeq = Interlocked.Increment(ref spawnedBotCounter);
            string? chosenDeck = pickRandomDeck && availablePool is { Count: > 0 }
                ? availablePool[Random.Shared.Next(availablePool.Count)]
                : (pickRandomDeck ? null : effectiveDeck);

            botInfo.Deck = chosenDeck;
            if (!string.IsNullOrWhiteSpace(effectiveDialog))
                botInfo.Dialog = effectiveDialog;
            if (!string.IsNullOrWhiteSpace(chosenDeck))
                botInfo.Name = $"{chosenDeck}#{botSeq}";
            else if (botSeq > 1 || spawnCount > 1)
                botInfo.Name = $"{botInfo.Name}#{botSeq}";
            botInfo.HostInfo = targetGame.RoomName;

            botService.StartBot(botInfo, activeHost.CreateLocalTransport(), cancellationToken);
            spawnedNames.Add(botInfo.Name);
        }

        return spawnedNames;
    }

    List<ChatCommandDefinition> cliCommands = new(baseOptions.ChatCommands);
    if (baseOptions.EnableChatCommands)
    {
        cliCommands.Add(new ChatCommandDefinition(
            "ai",
            "召唤内置 WindBot 加入当前房间（支持 random 随机卡组与 fill 一键补齐 2v2 空位）",
            ctx =>
            {
                int emptySlots = ctx.Game.Players.Count(p => p is null);
                bool fillAll = ctx.Arguments.Count >= 1 &&
                    (ctx.Arguments[0].Equals("fill", StringComparison.OrdinalIgnoreCase) ||
                     ctx.Arguments[0].Equals("all", StringComparison.OrdinalIgnoreCase));
                int argOffset = fillAll ? 1 : 0;
                int spawnCount = fillAll ? Math.Max(1, emptySlots) : 1;
                string? requestedDeck = ctx.Arguments.Count > argOffset ? ctx.Arguments[argOffset] : null;
                string? requestedDialog = ctx.Arguments.Count > argOffset + 1 ? ctx.Arguments[argOffset + 1] : null;

                try
                {
                    IReadOnlyList<string> spawnedNames = SpawnBotsIntoRoom(ctx.Game, requestedDeck, requestedDialog, spawnCount);
                    ctx.Broadcast($"[AI] 正在召唤 {string.Join(", ", spawnedNames)} 加入房间...");
                }
                catch (InvalidOperationException ex)
                {
                    ctx.Reply($"[AI] {ex.Message}");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to spawn WindBot via /ai command.");
                    ctx.Reply($"[AI] 召唤失败：{ex.Message}");
                }
            },
            Aliases: ["bot", "addai"],
            Usage: "/ai [fill] [卡组名|random] [对话名]",
            Category: "AI"));

        cliCommands.Add(new ChatCommandDefinition(
            "decks",
            "查看可供 /ai 使用的内置与 Lua 卡组列表",
            ctx =>
            {
                string filter = ctx.RawArguments.Trim();
                List<string> matched = CollectAvailableBotDeckNames()
                    .Where(n => string.IsNullOrEmpty(filter) || n.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (matched.Count == 0)
                {
                    ctx.Reply($"[Decks] 未找到匹配 '{filter}' 的卡组。");
                    return;
                }

                const int maxShow = 15;
                string preview = string.Join(", ", matched.Take(maxShow));
                string suffix = matched.Count > maxShow ? $" ...（共 {matched.Count} 套，可用 /decks <关键字> 过滤）" : $"（共 {matched.Count} 套）";
                ctx.Reply($"[Decks] {preview}{suffix}");
            },
            Aliases: ["aidecks"],
            Usage: "/decks [关键字]",
            Category: "AI"));
    }

    bool enablePersistence = CliOptionsFactory.ShouldEnablePersistence(configuration);
    bool enableAdmin = CliOptionsFactory.ShouldEnableAdminPanel(configuration);
    bool enablePortal = CliOptionsFactory.ShouldEnableUserPortal(configuration);
    OperationalStore? operationalStore = null;
    OperationalServerCoordinator? coordinator = null;
    WebControlHost? webHost = null;
    UserPortalHost? portalHost = null;

    WebControlOptions? plannedAdminOptions = enableAdmin
        ? CliOptionsFactory.CreateWebControlOptions(
            configuration,
            windBotSpawner: SpawnBotsIntoRoom,
            availableBotDecksProvider: CollectAvailableBotDeckNames,
            namedCardRepositoryProvider: GetNamedCardRepository)
        : null;

    UserPortalOptions? plannedPortalOptions = enablePortal
        ? CliOptionsFactory.CreateUserPortalOptions(configuration, GetNamedCardRepository)
        : null;

    ValidateDistinctPorts(baseOptions, plannedAdminOptions, plannedPortalOptions);

    List<Func<Game, AddonBase>> addonFactories = new(baseOptions.AddonFactories);
    Func<PlayerJoinAccessContext, PlayerJoinAccessDecision>? joinValidator = baseOptions.JoinAccessValidator;

    if (enablePersistence)
    {
        PersistenceOptions persistenceOptions = CliOptionsFactory.CreatePersistenceOptions(configuration, baseOptions);
        operationalStore = new OperationalStore(persistenceOptions);
        BootstrapAdminResult bootstrap = await operationalStore.InitializeAsync(cancellationToken).ConfigureAwait(false);
        if (bootstrap.CreatedNewAdmin && !string.IsNullOrEmpty(bootstrap.GeneratedOneTimePassword))
        {
            logger.LogWarning(
                "Initialized bootstrap super_admin '{Username}'. One-time initial password: {Password} (password change is mandatory on first login).",
                bootstrap.Username,
                bootstrap.GeneratedOneTimePassword);
        }
        else
        {
            logger.LogInformation(
                "Admin account ready: Username='{Username}', MustChangePassword={MustChangePassword}.",
                bootstrap.Username,
                bootstrap.MustChangePassword);
        }

        coordinator = new OperationalServerCoordinator(operationalStore);
        addonFactories.Add(coordinator.CreateRoomAddonFactory());
        Func<PlayerJoinAccessContext, PlayerJoinAccessDecision> operationalJoinValidator = coordinator.CreateJoinAccessValidator();
        Func<PlayerJoinAccessContext, PlayerJoinAccessDecision>? previousJoinValidator = joinValidator;
        joinValidator = previousJoinValidator is null
            ? operationalJoinValidator
            : ctx =>
            {
                PlayerJoinAccessDecision first = previousJoinValidator(ctx);
                return !first.Allowed || first.HandledCustomSession ? first : operationalJoinValidator(ctx);
            };
    }

    ServerOptions options = baseOptions with
    {
        ChatCommands = cliCommands,
        AddonFactories = addonFactories,
        JoinAccessValidator = joinValidator
    };

    using NativeOcgRuntime runtime = new NativeOcgRuntime();
    logger.LogInformation("Starting YGOProSharp server.");

    await using YGOProSharpServerHost serverHost = YGOProSharpServerHost.Create(options, runtime);
    activeHost = serverHost;
    serverHost.StartBackgroundLoop(tickDelayMilliseconds: 1, embedded: !options.EnableNetworkListener, cancellationToken);

    try
    {
        if (plannedAdminOptions is not null && operationalStore is not null && coordinator is not null)
        {
            webHost = new WebControlHost(serverHost, operationalStore, coordinator, plannedAdminOptions, logRingBuffer);
            await webHost.StartAsync(cancellationToken).ConfigureAwait(false);
        }

        if (plannedPortalOptions is not null && operationalStore is not null && coordinator is not null)
        {
            portalHost = new UserPortalHost(serverHost, operationalStore, coordinator, plannedPortalOptions);
            await portalHost.StartAsync(cancellationToken).ConfigureAwait(false);
        }

        if (CliOptionsFactory.ShouldStartServerBot(configuration))
        {
            WindBotService? botService = GetOrCreateBotService();
            if (botService is null)
            {
                logger.LogError("Can't find cards database file for embedded WindBot.");
                return 2;
            }

            WindBotInfo botInfo = CliOptionsFactory.CreateServerBotInfo(configuration, options);
            botService.StartBot(botInfo, serverHost.CreateLocalTransport(), cancellationToken);
            logger.LogInformation(
                "Started default in-process WindBot '{BotName}' (Deck={Deck}) on local transport.",
                botInfo.Name,
                botInfo.Deck ?? "<default>");
        }

        while (serverHost.IsRunning && !cancellationToken.IsCancellationRequested)
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
    }
    finally
    {
        if (portalHost is not null)
            await portalHost.DisposeAsync().ConfigureAwait(false);

        if (webHost is not null)
            await webHost.DisposeAsync().ConfigureAwait(false);

        if (sharedBotService is not null)
        {
            await sharedBotService.StopAllAsync().ConfigureAwait(false);
            await sharedBotService.DisposeAsync().ConfigureAwait(false);
        }

        await serverHost.StopAsync().ConfigureAwait(false);

        if (operationalStore is not null)
            await operationalStore.DisposeAsync().ConfigureAwait(false);
    }

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

static void ValidateDistinctPorts(ServerOptions serverOptions, WebControlOptions? adminOptions, UserPortalOptions? portalOptions)
{
    int gamePort = serverOptions.EnableNetworkListener ? serverOptions.Port : -1;
    int adminPort = adminOptions?.Port ?? -1;
    int portalPort = portalOptions?.Port ?? -1;

    if (gamePort > 0 && adminPort > 0 && gamePort == adminPort)
        throw new InvalidOperationException($"游戏服端口 ({gamePort}) 与后台管理面板端口 ({adminPort}) 不能相同，请分配独立端口。");

    if (gamePort > 0 && portalPort > 0 && gamePort == portalPort)
        throw new InvalidOperationException($"游戏服端口 ({gamePort}) 与前台用户面板端口 ({portalPort}) 不能相同，请分配独立端口。");

    if (adminPort > 0 && portalPort > 0 && adminPort == portalPort)
        throw new InvalidOperationException($"后台管理面板端口 ({adminPort}) 与前台用户面板端口 ({portalPort}) 必须相互独立，禁止共用同一端口。");
}

static ILoggerFactory CreateLoggerFactory(LogLevel logLevel, LogRingBuffer logRingBuffer)
{
    return LoggerFactory.Create(builder =>
    {
        builder.SetMinimumLevel(logLevel);
        builder.AddProvider(new RingBufferLoggerProvider(logRingBuffer));
        builder.AddSimpleConsole(options =>
        {
            options.SingleLine = true;
            options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
        });
    });
}
