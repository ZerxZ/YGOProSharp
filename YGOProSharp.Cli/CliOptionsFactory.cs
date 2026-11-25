using WindBot;
using WindBot.Game.AI.Lua;
using YGOProSharp.Abstractions.Scripting;
using YGOProSharp.Core;
using YGOProSharp.Core.Cards;
using YGOProSharp.Persistence;
using YGOProSharp.Protocol;
using YGOProSharp.Server;
using YGOProSharp.Web;

namespace YGOProSharp.Cli;

public static class CliOptionsFactory
{
    public static ServerOptions CreateServerOptions(CliConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        int region = configuration.Has("Region")
            ? configuration.GetInt("Region")
            : configuration.GetInt("Rule", 0);

        List<string> expansionDirs = new();
        AddSplitPaths(expansionDirs, configuration.GetString("ExpansionDir"));
        AddSplitPaths(expansionDirs, configuration.GetString("ExpansionDirs"));

        List<string> extraDatabases = new();
        AddSplitPaths(extraDatabases, configuration.GetString("ExtraDb"));
        AddSplitPaths(extraDatabases, configuration.GetString("ExtraDatabases"));

        List<string> extraScriptDirs = new();
        AddSplitPaths(extraScriptDirs, configuration.GetString("ExtraScriptDir"));
        AddSplitPaths(extraScriptDirs, configuration.GetString("ExtraScriptDirs"));

        List<string> extraScriptArchives = new();
        AddSplitPaths(extraScriptArchives, configuration.GetString("ExtraScriptArchives"));

        List<string> extraBanlists = new();
        AddSplitPaths(extraBanlists, configuration.GetString("ExtraBanlist"));
        AddSplitPaths(extraBanlists, configuration.GetString("ExtraBanlists"));
        AddSplitPaths(extraBanlists, configuration.GetString("ExtraBanlistFiles"));

        List<string> extraScripts = new();
        AddSplitPaths(extraScripts, configuration.GetString("ExtraScripts"));

        List<string> randomDeckDirs = new();
        AddSplitPaths(randomDeckDirs, configuration.GetString("RandomDeckDir"));
        AddSplitPaths(randomDeckDirs, configuration.GetString("RandomDeckDirs"));

        bool enableMultiRoom = configuration.Has("EnableMultiRoom")
            ? configuration.GetBool("EnableMultiRoom")
            : (configuration.Has("MultiRoom")
                ? configuration.GetBool("MultiRoom")
                : ParseEnvBool("YGOPRO_MULTI_ROOM"));

        string rootPath = configuration.GetString("RootPath")
                          ?? Environment.GetEnvironmentVariable("YGOPRO_ROOT_PATH")
                          ?? ".";

        int serverPort = configuration.Has("Port")
            ? configuration.GetInt("Port", CoreServer.DefaultPort)
            : ParseEnvInt("YGOPRO_GAME_PORT", CoreServer.DefaultPort);

        int mode = ParseMode(configuration.GetString("Mode") ?? Environment.GetEnvironmentVariable("YGOPRO_DEFAULT_MODE"));
        int defaultStartLp = mode == 2 ? Game.DEFAULT_TAG_LIFEPOINTS : Game.DEFAULT_LIFEPOINTS;
        RandomDeckMode randomDeckMode = ParseRandomDeckMode(configuration.GetString("RandomDeck") ?? Environment.GetEnvironmentVariable("YGOPRO_RANDOM_DECK"));

        return new ServerOptions
        {
            RootPath = rootPath,
            ScriptDirectory = configuration.GetString("ScriptDirectory", "script"),
            DatabaseFile = configuration.GetString("DatabaseFile", "cards.cdb"),
            BanlistFile = configuration.GetString("BanlistFile", "lflist.conf"),
            Port = serverPort,
            ClientVersion = configuration.GetUInt("ClientVersion", YGOProSharpServer.DefaultClientVersion),
            StandardStreamProtocol = configuration.GetBool("StandardStreamProtocol"),
            AutoLoadExpansions = configuration.GetBool("AutoLoadExpansions", true),
            ExpansionDirectories = expansionDirs,
            ExtraDatabases = extraDatabases,
            ExtraScriptDirectories = extraScriptDirs,
            ExtraScriptArchives = extraScriptArchives,
            ExtraBanlistFiles = extraBanlists,
            RandomDeckDirectories = randomDeckDirs,
            RandomDeckRerollPerMatchDuel = configuration.GetBool("RandomDeckRerollPerMatchDuel", true),
            EnableMultiRoom = enableMultiRoom,
            MaxRooms = configuration.GetInt("MaxRooms", 128),
            ApplyPasswordRulesInSingleRoom = configuration.GetBool("ApplyPasswordRulesInSingleRoom", true),
            EnableChatCommands = configuration.GetBool("EnableChatCommands", true),
            Game = new GameOptions
            {
                Mode = mode,
                Region = region,
                MasterRule = configuration.GetInt("MasterRule", 5),
                Banlist = configuration.GetInt("Banlist"),
                StartLp = configuration.GetInt("StartLp", defaultStartLp),
                StartHand = configuration.GetInt("StartHand", Game.DEFAULT_START_HAND),
                DrawCount = configuration.GetInt("DrawCount", Game.DEFAULT_DRAW_COUNT),
                EnablePriority = configuration.GetBool("EnablePriority"),
                NoCheckDeck = configuration.GetBool("NoCheckDeck"),
                NoShuffleDeck = configuration.GetBool("NoShuffleDeck"),
                GameTimer = configuration.GetInt("GameTimer", Game.DEFAULT_TIMER),
                MatchWins = configuration.GetInt("MatchWins", 2),
                RandomDeck = randomDeckMode,
                ChaosMainDeckSize = configuration.GetInt("ChaosMainDeckSize", 40),
                SuddenDeath = configuration.GetBool("SuddenDeath", false),
                ExtraScripts = extraScripts,
                DeckRules = new DeckRules(
                    configuration.GetInt("MainDeckMinSize", 40),
                    configuration.GetInt("MainDeckMaxSize", 60),
                    configuration.GetInt("ExtraDeckMaxSize", 15),
                    configuration.GetInt("SideDeckMaxSize", 15))
            }
        };
    }

    public static WindBotInfo CreateServerBotInfo(CliConfiguration configuration, ServerOptions serverOptions)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(serverOptions);

        WindBotInfo info = new WindBotInfo
        {
            Name = configuration.GetString("BotName", configuration.GetString("Name", "WindBot")),
            Deck = configuration.GetString("BotDeck") ?? configuration.GetString("Deck"),
            DeckFile = configuration.GetString("BotDeckFile") ?? configuration.GetString("DeckFile"),
            Dialog = configuration.GetString("BotDialog", configuration.GetString("Dialog", "default")),
            Locale = configuration.GetString("BotLocale") ?? configuration.GetString("Locale"),
            ChatIntervalMilliseconds = configuration.Has("BotChatIntervalMs")
                ? configuration.GetInt("BotChatIntervalMs")
                : (configuration.Has("ChatIntervalMs") ? configuration.GetInt("ChatIntervalMs") : null),
            DuelStartChatDelayMilliseconds = configuration.Has("BotDuelStartChatDelayMs")
                ? configuration.GetInt("BotDuelStartChatDelayMs")
                : (configuration.Has("DuelStartChatDelayMs") ? configuration.GetInt("DuelStartChatDelayMs") : null),
            MaxQueuedChatMessages = configuration.Has("MaxQueuedChatMessages")
                ? configuration.GetInt("MaxQueuedChatMessages")
                : null,
            Host = "127.0.0.1",
            Port = serverOptions.Port,
            Version = (int)serverOptions.ClientVersion,
            Hand = configuration.GetInt("BotHand", configuration.GetInt("Hand", 0)),
            Debug = configuration.GetBool("BotDebug", configuration.GetBool("Debug", false)),
            Chat = configuration.GetBool("BotChat", configuration.GetBool("Chat", true))
        };

        return info;
    }

    public static bool ShouldStartServerBot(CliConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return configuration.GetBool("AddBot", false) ||
               configuration.GetBool("WithBot", false) ||
               !string.IsNullOrWhiteSpace(configuration.GetString("BotDeck")) ||
               !string.IsNullOrWhiteSpace(configuration.GetString("BotDeckFile"));
    }

    public static WindBotInfo CreateWindBotInfo(CliConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        WindBotInfo info = new WindBotInfo();
        info.Name = configuration.GetString("Name", info.Name);
        info.Deck = configuration.GetString("Deck", info.Deck);
        info.DeckFile = configuration.GetString("DeckFile", info.DeckFile);
        info.Dialog = configuration.GetString("Dialog", info.Dialog);
        info.Locale = configuration.GetString("Locale", info.Locale);
        info.Host = configuration.GetString("Host", info.Host);
        info.Port = configuration.GetInt("Port", info.Port);
        info.HostInfo = configuration.GetString("HostInfo", info.HostInfo);
        info.Version = configuration.GetInt("Version", info.Version);
        info.Hand = configuration.GetInt("Hand", info.Hand);
        info.Debug = configuration.GetBool("Debug", info.Debug);
        info.Chat = configuration.GetBool("Chat", info.Chat);
        info.UseWebSocket = configuration.GetBool("UseWebSocket", info.UseWebSocket);
        info.WebSocketUseWss = configuration.GetBool("WebSocketUseWss", info.WebSocketUseWss);
        info.WebSocketPath = configuration.GetString("WebSocketPath", info.WebSocketPath);
        string? wsFraming = configuration.GetString("WebSocketFramingMode");
        if (!string.IsNullOrWhiteSpace(wsFraming) &&
            Enum.TryParse(wsFraming, ignoreCase: true, out WebSocketFramingMode parsedFraming))
        {
            info.WebSocketFramingMode = parsedFraming;
        }
        return info;
    }

    public static WindBotRuntimeOptions CreateWindBotRuntimeOptions(CliConfiguration configuration, INamedCardRepository cardRepository)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        bool enableLuaDecks = configuration.GetBool("EnableLuaDecks", true);
        bool enableLuaDialogs = configuration.Has("EnableLuaDialogs")
            ? configuration.GetBool("EnableLuaDialogs", true)
            : enableLuaDecks;

        ILuaRuntime? luaRuntime = (enableLuaDecks || enableLuaDialogs) ? NLuaRuntime.Instance : null;
        ILuaScriptSourceProvider? luaScriptProvider = enableLuaDecks
            ? CreateLuaDeckScriptProvider(configuration)
            : null;
        ILuaScriptSourceProvider? luaDialogScriptProvider = enableLuaDialogs
            ? CreateLuaDialogScriptProvider(configuration)
            : null;

        return new WindBotRuntimeOptions
        {
            CardRepository = cardRepository,
            TickDelayMilliseconds = configuration.GetInt("TickDelayMilliseconds", 30),
            DefaultBot = CreateWindBotInfo(configuration),
            Locale = configuration.GetString("BotLocale") ?? configuration.GetString("Locale"),
            ChatIntervalMilliseconds = configuration.GetInt("ChatIntervalMs", 800),
            DuelStartChatDelayMilliseconds = configuration.GetInt("DuelStartChatDelayMs", 4000),
            MaxQueuedChatMessages = configuration.GetInt("MaxQueuedChatMessages", 6),
            UsePreErrataEffects = configuration.GetBool("UsePreErrataEffects", false),
            ServerMode = configuration.GetBool("ServerMode", false),
            LuaRuntime = luaRuntime,
            LuaScriptProvider = luaScriptProvider,
            PreferLuaDecks = configuration.GetBool("PreferLuaDecks", false),
            LuaDialogScriptProvider = luaDialogScriptProvider,
            PreferLuaDialogs = configuration.GetBool("PreferLuaDialogs", false),
            ThrowOnLuaScriptError = configuration.GetBool("ThrowOnLuaScriptError", false)
        };
    }

    private static FileSystemLuaDeckScriptProvider CreateLuaDeckScriptProvider(CliConfiguration configuration)
    {
        List<string> directories = new();
        AddSplitPaths(directories, configuration.GetString("LuaDeckDir"));
        AddSplitPaths(directories, configuration.GetString("LuaDeckDirs"));

        if (directories.Count == 0)
            return new FileSystemLuaDeckScriptProvider();

        // 自定义目录优先，再回退集中目录 Decks/Lua（及兼容路径）
        directories.Add(Path.Combine("Decks", "Lua"));
        directories.Add(Path.Combine("Deck", "Lua"));
        directories.Add(Path.Combine(AppContext.BaseDirectory, "Decks", "Lua"));
        directories.Add(Path.Combine(AppContext.BaseDirectory, "Deck", "Lua"));
        directories.Add("LuaDecks");
        directories.Add(Path.Combine(AppContext.BaseDirectory, "LuaDecks"));
        return new FileSystemLuaDeckScriptProvider(directories.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static FileSystemLuaDialogScriptProvider CreateLuaDialogScriptProvider(CliConfiguration configuration)
    {
        List<string> directories = new();
        AddSplitPaths(directories, configuration.GetString("LuaDialogDir"));
        AddSplitPaths(directories, configuration.GetString("LuaDialogDirs"));

        if (directories.Count == 0)
            return new FileSystemLuaDialogScriptProvider();

        // 自定义目录优先，再回退集中目录 Dialogs/Lua（及兼容路径）
        directories.Add(Path.Combine("Dialogs", "Lua"));
        directories.Add(Path.Combine("Dialog", "Lua"));
        directories.Add("Dialogs");
        directories.Add(Path.Combine(AppContext.BaseDirectory, "Dialogs", "Lua"));
        directories.Add(Path.Combine(AppContext.BaseDirectory, "Dialog", "Lua"));
        directories.Add(Path.Combine(AppContext.BaseDirectory, "Dialogs"));
        directories.Add("LuaDialogs");
        directories.Add(Path.Combine(AppContext.BaseDirectory, "LuaDialogs"));
        return new FileSystemLuaDialogScriptProvider(directories.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static int ParseMode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return 0;

        string trimmed = raw.Trim();
        if (int.TryParse(trimmed, out int numeric) && numeric >= 0 && numeric <= 2)
            return numeric;

        return trimmed.ToLowerInvariant() switch
        {
            "s" or "single" or "1v1" or "1vs1" => 0,
            "m" or "match" => 1,
            "t" or "tag" or "2v2" or "2vs2" => 2,
            _ => 0
        };
    }

    private static RandomDeckMode ParseRandomDeckMode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return RandomDeckMode.None;

        return raw.Trim().ToLowerInvariant() switch
        {
            "preset" or "rd" or "rand" => RandomDeckMode.PresetRandom,
            "mirror" or "rdm" => RandomDeckMode.MirrorRandom,
            "chaos" or "chaos60" => RandomDeckMode.ChaosDraft,
            "purechaos" => RandomDeckMode.PureChaos,
            "diy" or "diyrd" => RandomDeckMode.DiyRandom,
            "diychaos" => RandomDeckMode.DiyChaos,
            _ => RandomDeckMode.None
        };
    }

    private static void AddSplitPaths(List<string> target, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return;

        string[] parts = raw.Split([';', ','], StringSplitOptions.RemoveEmptyEntries);
        foreach (string part in parts)
        {
            string trimmed = part.Trim();
            if (!string.IsNullOrWhiteSpace(trimmed))
                target.Add(trimmed);
        }
    }

    public static bool ShouldEnableAdminPanel(CliConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration.Has("EnableAdmin"))
            return configuration.GetBool("EnableAdmin");

        if (configuration.Has("EnableWeb"))
            return configuration.GetBool("EnableWeb");

        if (configuration.Has("AdminPort") || configuration.Has("AdminHost") ||
            configuration.Has("WebPort") || configuration.Has("WebHost"))
        {
            return true;
        }

        return ParseEnvBool("YGOPRO_ENABLE_ADMIN") || ParseEnvBool("YGOPRO_ENABLE_WEB");
    }

    public static bool ShouldEnableUserPortal(CliConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration.Has("EnablePortal"))
            return configuration.GetBool("EnablePortal");

        // 默认单端口融合模式（EnableWeb=true 时前台与后台共用 WebPort，通过 / 与 /admin 路径区分）；
        // 仅当用户显式指定了独立的 PortalPort / PortalHost / YGOPRO_ENABLE_PORTAL 时才额外启动第二个独立门户端口。
        if (configuration.Has("PortalPort") || configuration.Has("PortalHost"))
            return true;

        return ParseEnvBool("YGOPRO_ENABLE_PORTAL") ||
               !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("YGOPRO_PORTAL_PORT"));
    }

    public static bool ShouldEnableWeb(CliConfiguration configuration)
    {
        return ShouldEnableAdminPanel(configuration) || ShouldEnableUserPortal(configuration);
    }

    public static bool ShouldEnablePersistence(CliConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (ShouldEnableAdminPanel(configuration) || ShouldEnableUserPortal(configuration))
            return true;

        if (configuration.Has("EnablePersistence"))
            return configuration.GetBool("EnablePersistence");

        return configuration.Has("DbProvider") ||
               configuration.Has("DbConnectionString") ||
               configuration.Has("RedisUrl") ||
               configuration.Has("AdminUser") ||
               configuration.Has("AdminPassword") ||
               configuration.Has("BootstrapAdminUser") ||
               configuration.Has("BootstrapAdminPassword") ||
               !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("YGOPRO_DB_PROVIDER")) ||
               !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("YGOPRO_DB_CONNECTION_STRING")) ||
               !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("YGOPRO_REDIS_URL")) ||
               !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("YGOPRO_ADMIN_USER")) ||
               !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("YGOPRO_ADMIN_PASSWORD")) ||
               !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("YGOPRO_BOOTSTRAP_ADMIN_USER")) ||
               !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("YGOPRO_BOOTSTRAP_ADMIN_PASSWORD"));
    }

    private static bool ParseEnvBool(string envVarName)
    {
        string? env = Environment.GetEnvironmentVariable(envVarName);
        return !string.IsNullOrWhiteSpace(env) &&
               (env.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                env.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                env.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }

    private static int ParseEnvInt(string envVarName, int defaultValue)
    {
        string? env = Environment.GetEnvironmentVariable(envVarName);
        return int.TryParse(env, out int parsed) ? parsed : defaultValue;
    }

    public static PersistenceOptions CreatePersistenceOptions(CliConfiguration configuration, ServerOptions serverOptions)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(serverOptions);

        string? rawProvider = configuration.GetString("DbProvider") ?? Environment.GetEnvironmentVariable("YGOPRO_DB_PROVIDER");
        OperationalDbProvider provider = PersistenceOptions.ParseProvider(rawProvider);

        string defaultSqlitePath = Path.Combine(serverOptions.RootPath, "data", "db", "ygoprosharp.db");
        string connectionString = configuration.GetString("DbConnectionString")
                                  ?? Environment.GetEnvironmentVariable("YGOPRO_DB_CONNECTION_STRING")
                                  ?? $"Data Source={defaultSqlitePath}";

        string? redisUrl = configuration.GetString("RedisUrl") ?? Environment.GetEnvironmentVariable("YGOPRO_REDIS_URL");
        string replayDirectory = configuration.GetString("ReplayDirectory")
                                 ?? Environment.GetEnvironmentVariable("YGOPRO_REPLAY_DIR")
                                 ?? Path.Combine(serverOptions.RootPath, "data", "replays");

        string? adminUser = configuration.GetString("AdminUser")
                            ?? configuration.GetString("BootstrapAdminUser")
                            ?? Environment.GetEnvironmentVariable("YGOPRO_ADMIN_USER")
                            ?? Environment.GetEnvironmentVariable("YGOPRO_BOOTSTRAP_ADMIN_USER");
        string? adminPassword = configuration.GetString("AdminPassword")
                                ?? configuration.GetString("BootstrapAdminPassword")
                                ?? Environment.GetEnvironmentVariable("YGOPRO_ADMIN_PASSWORD")
                                ?? Environment.GetEnvironmentVariable("YGOPRO_BOOTSTRAP_ADMIN_PASSWORD");

        return new PersistenceOptions
        {
            DbProvider = provider,
            ConnectionString = connectionString,
            RedisUrl = string.IsNullOrWhiteSpace(redisUrl) ? null : redisUrl.Trim(),
            ReplayDirectory = replayDirectory,
            BootstrapAdminUser = string.IsNullOrWhiteSpace(adminUser) ? null : adminUser.Trim(),
            BootstrapAdminPassword = string.IsNullOrWhiteSpace(adminPassword) ? null : adminPassword.Trim(),
            SessionTtlHours = configuration.Has("SessionTtlHours")
                ? configuration.GetInt("SessionTtlHours", 24)
                : ParseEnvInt("YGOPRO_SESSION_TTL_HOURS", 24),
            MaxLoginFailures = configuration.GetInt("MaxLoginFailures", 5),
            LockoutMinutes = configuration.GetInt("LockoutMinutes", 15)
        };
    }

    public static WebControlOptions CreateWebControlOptions(
        CliConfiguration configuration,
        Func<Game, string?, string?, int, IReadOnlyList<string>>? windBotSpawner = null,
        Func<IReadOnlyList<string>>? availableBotDecksProvider = null,
        Func<INamedCardRepository>? namedCardRepositoryProvider = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        string host = configuration.GetString("AdminHost")
                      ?? configuration.GetString("WebHost")
                      ?? Environment.GetEnvironmentVariable("YGOPRO_ADMIN_HOST")
                      ?? Environment.GetEnvironmentVariable("YGOPRO_WEB_HOST")
                      ?? "127.0.0.1";

        int port = 7912;
        if (configuration.Has("AdminPort"))
            port = configuration.GetInt("AdminPort", 7912);
        else if (configuration.Has("WebPort"))
            port = configuration.GetInt("WebPort", 7912);
        else if (int.TryParse(Environment.GetEnvironmentVariable("YGOPRO_ADMIN_PORT") ?? Environment.GetEnvironmentVariable("YGOPRO_WEB_PORT"), out int envPort))
            port = envPort;

        int? portalPort = ShouldEnableUserPortal(configuration)
            ? ResolvePortalPort(configuration)
            : null;

        bool requireHttps = configuration.Has("RequireHttpsCookie")
            ? configuration.GetBool("RequireHttpsCookie", false)
            : ParseEnvBool("YGOPRO_REQUIRE_HTTPS_COOKIE");

        return new WebControlOptions
        {
            Host = host,
            Port = port,
            PortalPort = portalPort,
            PublicGameHost = configuration.GetString("PublicGameHost") ?? Environment.GetEnvironmentVariable("YGOPRO_PUBLIC_GAME_HOST"),
            RequireHttpsCookie = requireHttps,
            StaticFilesRoot = configuration.GetString("WebStaticRoot") ?? Environment.GetEnvironmentVariable("YGOPRO_WEB_STATIC_ROOT"),
            WindBotSpawner = windBotSpawner,
            AvailableBotDecksProvider = availableBotDecksProvider,
            NamedCardRepositoryProvider = namedCardRepositoryProvider
        };
    }

    public static UserPortalOptions CreateUserPortalOptions(
        CliConfiguration configuration,
        Func<INamedCardRepository>? namedCardRepositoryProvider = null,
        Func<Game, string?, string?, int, IReadOnlyList<string>>? windBotSpawner = null,
        Func<IReadOnlyList<string>>? availableBotDecksProvider = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        string host = configuration.GetString("PortalHost")
                      ?? Environment.GetEnvironmentVariable("YGOPRO_PORTAL_HOST")
                      ?? "127.0.0.1";

        int port = ResolvePortalPort(configuration);

        int? adminPortHint = null;
        if (ShouldEnableAdminPanel(configuration))
        {
            if (configuration.Has("AdminPort"))
                adminPortHint = configuration.GetInt("AdminPort", 7912);
            else if (configuration.Has("WebPort"))
                adminPortHint = configuration.GetInt("WebPort", 7912);
            else if (int.TryParse(Environment.GetEnvironmentVariable("YGOPRO_ADMIN_PORT") ?? Environment.GetEnvironmentVariable("YGOPRO_WEB_PORT"), out int envAdmPort))
                adminPortHint = envAdmPort;
            else
                adminPortHint = 7912;
        }

        bool requireHttps = configuration.Has("RequireHttpsCookie")
            ? configuration.GetBool("RequireHttpsCookie", false)
            : ParseEnvBool("YGOPRO_REQUIRE_HTTPS_COOKIE");

        return new UserPortalOptions
        {
            Host = host,
            Port = port,
            PublicGameHost = configuration.GetString("PublicGameHost") ?? Environment.GetEnvironmentVariable("YGOPRO_PUBLIC_GAME_HOST"),
            RequireHttpsCookie = requireHttps,
            McpPortHint = adminPortHint,
            StaticFilesRoot = configuration.GetString("WebStaticRoot") ?? Environment.GetEnvironmentVariable("YGOPRO_WEB_STATIC_ROOT"),
            NamedCardRepositoryProvider = namedCardRepositoryProvider,
            WindBotSpawner = windBotSpawner,
            AvailableBotDecksProvider = availableBotDecksProvider
        };
    }

    private static int ResolvePortalPort(CliConfiguration configuration)
    {
        if (configuration.Has("PortalPort"))
            return configuration.GetInt("PortalPort", 7913);
        if (int.TryParse(Environment.GetEnvironmentVariable("YGOPRO_PORTAL_PORT"), out int envPort))
            return envPort;
        return 7913;
    }

    public static WindBotServerModeOptions CreateWindBotServerModeOptions(CliConfiguration configuration, WindBotRuntimeOptions runtimeOptions)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return new WindBotServerModeOptions
        {
            ServerPort = configuration.GetInt("ServerPort", 2399),
            BotListPath = configuration.GetString("BotListPath"),
            RuntimeOptions = runtimeOptions
        };
    }

    public static List<string> ResolveWindBotDatabasePaths(CliConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        List<string> paths = new List<string>();
        AddDatabasePathCandidates(paths, configuration.GetString("DbPath", "cards.cdb"), configuration.GetString("Locale"));
        AddDatabasePathCandidates(paths, configuration.GetString("DbPaths"), configuration.GetString("Locale"));
        AddDatabasePathCandidates(paths, configuration.GetString("Databases"), configuration.GetString("Locale"));
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void AddDatabasePathCandidates(List<string> paths, string? value, string? locale)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        string[] parts = value.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string rawPath in parts)
        {
            string trimmedPath = rawPath.Trim();
            foreach (string candidate in ExpandDatabasePath(trimmedPath, locale))
            {
                if (File.Exists(candidate))
                {
                    paths.Add(candidate);
                    break;
                }
            }
        }
    }

    private static IEnumerable<string> ExpandDatabasePath(string databasePath, string? locale)
    {
        if (Path.IsPathRooted(databasePath))
        {
            yield return Path.GetFullPath(databasePath);
            yield break;
        }

        yield return Path.GetFullPath(databasePath);
        yield return Path.GetFullPath(Path.Combine("Data", databasePath));
        yield return Path.GetFullPath(Path.Combine("cdb", databasePath));

        if (!string.IsNullOrWhiteSpace(locale))
            yield return Path.GetFullPath(Path.Combine("Data", "locales", locale, databasePath));

        yield return Path.GetFullPath(Path.Combine("..", databasePath));
        yield return Path.GetFullPath(Path.Combine("..", "cdb", databasePath));

        string baseDir = AppContext.BaseDirectory;
        yield return Path.GetFullPath(Path.Combine(baseDir, databasePath));
        yield return Path.GetFullPath(Path.Combine(baseDir, "Data", databasePath));
        yield return Path.GetFullPath(Path.Combine(baseDir, "cdb", databasePath));
        if (!string.IsNullOrWhiteSpace(locale))
            yield return Path.GetFullPath(Path.Combine(baseDir, "Data", "locales", locale, databasePath));
    }
}
