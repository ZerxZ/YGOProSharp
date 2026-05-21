namespace YGOProSharp.Core;

public sealed record DeckRules(
    int MainDeckMinSize = 40,
    int MainDeckMaxSize = 60,
    int ExtraDeckMaxSize = 15,
    int SideDeckMaxSize = 15)
{
    public static DeckRules Default { get; } = new();
}
