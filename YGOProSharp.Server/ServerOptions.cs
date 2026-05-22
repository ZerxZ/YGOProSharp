using YGOProSharp.Core;

namespace YGOProSharp.Server;

public sealed record ServerOptions
{
    public string RootPath { get; init; } = ".";
    public string ScriptDirectory { get; init; } = "script";
    public string DatabaseFile { get; init; } = "cards.cdb";
    public string BanlistFile { get; init; } = "lflist.conf";
    public int Port { get; init; } = CoreServer.DefaultPort;
    public uint ClientVersion { get; init; } = YGOProSharpServer.DefaultClientVersion;
    public bool StandardStreamProtocol { get; init; }
    public GameOptions Game { get; init; } = new();
}

public sealed record GameOptions
{
    public int Mode { get; init; }
    public int Region { get; init; }
    public int MasterRule { get; init; } = 3;
    public int Banlist { get; init; }
    public int StartLp { get; init; } = Game.DEFAULT_LIFEPOINTS;
    public int StartHand { get; init; } = Game.DEFAULT_START_HAND;
    public int DrawCount { get; init; } = Game.DEFAULT_DRAW_COUNT;
    public int GameTimer { get; init; } = Game.DEFAULT_TIMER;
    public bool EnablePriority { get; init; }
    public bool NoCheckDeck { get; init; }
    public bool NoShuffleDeck { get; init; }
    public DeckRules DeckRules { get; init; } = DeckRules.Default;
}
