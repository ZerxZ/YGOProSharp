using WindBot;
using WindBot.Game.AI.Lua;
using YGOProSharp.Abstractions.Scripting;
using YGOProSharp.Core;
using YGOProSharp.Core.Cards;
using YGOProSharp.Protocol;
using YGOProSharp.Server;

namespace YGOProSharp.Cli;

public static class CliOptionsFactory
{
    public static ServerOptions CreateServerOptions(CliConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        int region = configuration.Has("Region")
            ? configuration.GetInt("Region")
            : configuration.GetInt("Rule", 0);

        return new ServerOptions
        {
            RootPath = configuration.GetString("RootPath", "."),
            ScriptDirectory = configuration.GetString("ScriptDirectory", "script"),
            DatabaseFile = configuration.GetString("DatabaseFile", "cards.cdb"),
            BanlistFile = configuration.GetString("BanlistFile", "lflist.conf"),
            Port = configuration.GetInt("Port", CoreServer.DefaultPort),
            ClientVersion = configuration.GetUInt("ClientVersion", YGOProSharpServer.DefaultClientVersion),
            StandardStreamProtocol = configuration.GetBool("StandardStreamProtocol"),
            Game = new GameOptions
            {
                Mode = configuration.GetInt("Mode"),
                Region = region,
                MasterRule = configuration.GetInt("MasterRule", 3),
                Banlist = configuration.GetInt("Banlist"),
                StartLp = configuration.GetInt("StartLp", Game.DEFAULT_LIFEPOINTS),
                StartHand = configuration.GetInt("StartHand", Game.DEFAULT_START_HAND),
                DrawCount = configuration.GetInt("DrawCount", Game.DEFAULT_DRAW_COUNT),
                EnablePriority = configuration.GetBool("EnablePriority"),
                NoCheckDeck = configuration.GetBool("NoCheckDeck"),
                NoShuffleDeck = configuration.GetBool("NoShuffleDeck"),
                GameTimer = configuration.GetInt("GameTimer", Game.DEFAULT_TIMER),
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
