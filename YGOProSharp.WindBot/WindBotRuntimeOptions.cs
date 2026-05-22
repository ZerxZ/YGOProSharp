using YGOProSharp.Core.Cards;

namespace WindBot;

public sealed class WindBotRuntimeOptions
{
    public INamedCardRepository CardRepository { get; init; } = EmptyNamedCardRepository.Instance;
    public int TickDelayMilliseconds { get; init; } = 30;
    public WindBotInfo DefaultBot { get; init; } = new WindBotInfo();
    public Random Random { get; init; }
}

public sealed class WindBotServerModeOptions
{
    public int ServerPort { get; init; } = 2399;
    public string BotListPath { get; init; }
    public WindBotRuntimeOptions RuntimeOptions { get; init; } = new WindBotRuntimeOptions();
}
