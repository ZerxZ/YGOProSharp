using Microsoft.Extensions.Logging;
using WindBot;
using WindBot.Game.AI;
using YGOProSharp.Abstractions.Logging;
using YGOProSharp.Cli;
using YGOProSharp.Core.Cards;
using YGOProSharp.NativeApi;
using YGOProSharp.Protocol.Enums;
using YGOProSharp.Server;
using YGOProSharp.Server.Addons;

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
    ServerOptions baseOptions = CliOptionsFactory.CreateServerOptions(configuration);

    if (configuration.Has("Rule") && !configuration.Has("Region"))
        logger.LogWarning("'Rule' is deprecated, please use 'Region' instead.");

    YGOProSharpServerHost? activeHost = null;
    WindBotService? sharedBotService = null;
    object botGate = new();
    int spawnedBotCounter = 0;

    WindBotService? GetOrCreateBotService()
    {
        lock (botGate)
        {
            if (sharedBotService is not null)
                return sharedBotService;

            List<string> botDbPaths = new();
            foreach (string serverDbPath in YGOProSharpServer.ResolveDatabasePaths(baseOptions))
            {
                if (File.Exists(serverDbPath) && !botDbPaths.Contains(serverDbPath, StringComparer.OrdinalIgnoreCase))
                    botDbPaths.Add(serverDbPath);
            }
            foreach (string cliDbPath in CliOptionsFactory.ResolveWindBotDatabasePaths(configuration))
            {
                if (File.Exists(cliDbPath) && !botDbPaths.Contains(cliDbPath, StringComparer.OrdinalIgnoreCase))
                    botDbPaths.Add(cliDbPath);
            }

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
            Path.Combine(baseOptions.RootPath, "Decks", "Lua")
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

    List<ChatCommandDefinition> cliCommands = new(baseOptions.ChatCommands);
    if (baseOptions.EnableChatCommands)
    {
        cliCommands.Add(new ChatCommandDefinition(
            "ai",
            "召唤内置 WindBot 加入当前房间（支持 random 随机卡组与 fill 一键补齐 2v2 空位）",
            ctx =>
            {
                if (ctx.Game.State != GameState.Lobby)
                {
                    ctx.Reply("[AI] 仅可在大厅阶段召唤 AI。");
                    return;
                }
                int emptySlots = ctx.Game.Players.Count(p => p is null);
                if (emptySlots == 0)
                {
                    ctx.Reply("[AI] 当前房间决斗席位已满。");
                    return;
                }
                if (activeHost is null || !activeHost.IsRunning)
                {
                    ctx.Reply("[AI] 服务端尚未就绪。");
                    return;
                }

                WindBotService? botService = GetOrCreateBotService();
                if (botService is null)
                {
                    ctx.Reply("[AI] 未找到可用卡库，无法启动内置 WindBot。");
                    return;
                }

                bool fillAll = ctx.Arguments.Count >= 1 &&
                    (ctx.Arguments[0].Equals("fill", StringComparison.OrdinalIgnoreCase) ||
                     ctx.Arguments[0].Equals("all", StringComparison.OrdinalIgnoreCase));
                int argOffset = fillAll ? 1 : 0;
                int spawnCount = fillAll ? emptySlots : 1;

                WindBotInfo templateInfo = CliOptionsFactory.CreateServerBotInfo(configuration, baseOptions);
                string? requestedDeck = ctx.Arguments.Count > argOffset ? ctx.Arguments[argOffset] : templateInfo.Deck;
                string? requestedDialog = ctx.Arguments.Count > argOffset + 1 ? ctx.Arguments[argOffset + 1] : templateInfo.Dialog;
                bool pickRandomDeck = requestedDeck is not null &&
                    (requestedDeck.Equals("random", StringComparison.OrdinalIgnoreCase) ||
                     requestedDeck.Equals("rand", StringComparison.OrdinalIgnoreCase));
                List<string>? availablePool = pickRandomDeck ? CollectAvailableBotDeckNames() : null;

                try
                {
                    List<string> spawnedNames = new(spawnCount);
                    for (int i = 0; i < spawnCount; i++)
                    {
                        WindBotInfo botInfo = CliOptionsFactory.CreateServerBotInfo(configuration, baseOptions);
                        int botSeq = Interlocked.Increment(ref spawnedBotCounter);
                        string? chosenDeck = pickRandomDeck && availablePool is { Count: > 0 }
                            ? availablePool[Random.Shared.Next(availablePool.Count)]
                            : (pickRandomDeck ? null : requestedDeck);

                        botInfo.Deck = chosenDeck;
                        if (!string.IsNullOrWhiteSpace(requestedDialog))
                            botInfo.Dialog = requestedDialog;
                        if (!string.IsNullOrWhiteSpace(chosenDeck))
                            botInfo.Name = $"{chosenDeck}#{botSeq}";
                        else if (botSeq > 1 || spawnCount > 1)
                            botInfo.Name = $"{botInfo.Name}#{botSeq}";
                        botInfo.HostInfo = ctx.Game.RoomName;

                        botService.StartBot(botInfo, activeHost.CreateLocalTransport(), cancellationToken);
                        spawnedNames.Add(botInfo.Name);
                    }

                    ctx.Broadcast($"[AI] 正在召唤 {string.Join(", ", spawnedNames)} 加入房间...");
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

    ServerOptions options = baseOptions with { ChatCommands = cliCommands };

    using NativeOcgRuntime runtime = new NativeOcgRuntime();
    logger.LogInformation("Starting YGOProSharp server.");

    await using YGOProSharpServerHost serverHost = YGOProSharpServerHost.Create(options, runtime);
    activeHost = serverHost;
    serverHost.StartBackgroundLoop(tickDelayMilliseconds: 1, embedded: !options.EnableNetworkListener, cancellationToken);

    if (CliOptionsFactory.ShouldStartServerBot(configuration))
    {
        WindBotService? botService = GetOrCreateBotService();
        if (botService is null)
        {
            logger.LogError("Can't find cards database file for embedded WindBot.");
            await serverHost.StopAsync().ConfigureAwait(false);
            return 2;
        }

        WindBotInfo botInfo = CliOptionsFactory.CreateServerBotInfo(configuration, options);
        botService.StartBot(botInfo, serverHost.CreateLocalTransport(), cancellationToken);
        logger.LogInformation(
            "Started default in-process WindBot '{BotName}' (Deck={Deck}) on local transport.",
            botInfo.Name,
            botInfo.Deck ?? "<default>");
    }

    try
    {
        while (serverHost.IsRunning && !cancellationToken.IsCancellationRequested)
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
    }
    finally
    {
        if (sharedBotService is not null)
        {
            await sharedBotService.StopAllAsync().ConfigureAwait(false);
            await sharedBotService.DisposeAsync().ConfigureAwait(false);
        }
        await serverHost.StopAsync().ConfigureAwait(false);
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
