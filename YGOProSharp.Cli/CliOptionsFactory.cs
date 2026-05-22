using WindBot;
using YGOProSharp.Core;
using YGOProSharp.Core.Cards;
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

    public static WindBotInfo CreateWindBotInfo(CliConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        WindBotInfo info = new WindBotInfo();
        info.Name = configuration.GetString("Name", info.Name);
        info.Deck = configuration.GetString("Deck", info.Deck);
        info.DeckFile = configuration.GetString("DeckFile", info.DeckFile);
        info.Dialog = configuration.GetString("Dialog", info.Dialog);
        info.Host = configuration.GetString("Host", info.Host);
        info.Port = configuration.GetInt("Port", info.Port);
        info.HostInfo = configuration.GetString("HostInfo", info.HostInfo);
        info.Version = configuration.GetInt("Version", info.Version);
        info.Hand = configuration.GetInt("Hand", info.Hand);
        info.Debug = configuration.GetBool("Debug", info.Debug);
        info.Chat = configuration.GetBool("Chat", info.Chat);
        return info;
    }

    public static WindBotRuntimeOptions CreateWindBotRuntimeOptions(CliConfiguration configuration, INamedCardRepository cardRepository)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return new WindBotRuntimeOptions
        {
            CardRepository = cardRepository,
            TickDelayMilliseconds = configuration.GetInt("TickDelayMilliseconds", 30),
            DefaultBot = CreateWindBotInfo(configuration)
        };
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
    }
}
