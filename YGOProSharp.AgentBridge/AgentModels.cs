namespace YGOProSharp.AgentBridge;

public enum AgentSessionState
{
    Connecting = 0,
    Lobby = 1,
    InDuel = 2,
    SideDecking = 3,
    DuelEnded = 4,
    Disconnected = 5
}

public sealed record AgentCardView(
    int Index,
    int Code,
    string? Name,
    string Location,
    int Sequence,
    string Position,
    bool IsHidden,
    int? Attack,
    int? Defense,
    int? Level,
    int OverlayCount,
    string? ShortEffect);

public sealed record AgentFieldSideView(
    int LifePoints,
    int DeckCount,
    int HandCount,
    int ExtraCount,
    int FaceUpExtraCount,
    IReadOnlyList<AgentCardView> Hand,
    IReadOnlyList<AgentCardView> MonsterZone,
    IReadOnlyList<AgentCardView> SpellZone,
    IReadOnlyList<AgentCardView> Graveyard,
    IReadOnlyList<AgentCardView> Banished,
    IReadOnlyList<AgentCardView> KnownExtraDeck);

public sealed record AgentLobbySlotView(
    int SeatIndex,
    int TeamId,
    string? PlayerName,
    bool IsReady);

public sealed record AgentChainLinkView(
    int ChainIndex,
    string Side,
    int CardCode,
    string? CardName,
    string Location,
    int EffectDescription);

public sealed record AgentLegalActionChoice(
    int ActionIndex,
    string Kind,
    string Label,
    int? CardCode = null,
    string? CardName = null,
    string? Zone = null,
    int? EffectDescription = null);

public sealed record AgentPendingPrompt(
    long PromptId,
    string PromptType,
    string HintText,
    bool CanCancel,
    int MinSelect,
    int MaxSelect,
    bool SupportsMultiSelect,
    IReadOnlyList<AgentLegalActionChoice> Choices);

public sealed record AgentDeckCountSummary(
    int CardCode,
    string? CardName,
    int Copies,
    string Section);

public sealed record AgentObservation(
    string SessionId,
    string PlayerName,
    string RoomName,
    string JoinSpec,
    AgentSessionState State,
    bool IsConnected,
    bool IsObserver,
    bool IsHost,
    int SeatIndex,
    bool IsTag,
    int RoomMode,
    int MasterRule,
    string? TeammateName,
    bool ActiveTagDeckIsMine,
    bool TeammateSurrenderPending,
    int TurnCount,
    string CurrentPhase,
    string CurrentTurnOwner,
    string? DuelResult,
    string? LastError,
    IReadOnlyList<AgentLobbySlotView> LobbySlots,
    AgentFieldSideView SelfSide,
    AgentFieldSideView OpponentSide,
    IReadOnlyList<AgentChainLinkView> CurrentChain,
    IReadOnlyList<AgentDeckCountSummary> OwnSubmittedDeckList,
    IReadOnlyList<string> RecentEvents,
    bool HasPendingAction,
    AgentPendingPrompt? PendingPrompt);

public sealed record AgentActionResult(
    bool Success,
    string Message,
    long? HandledPromptId,
    AgentObservation Observation);

public sealed record AgentSessionSummary(
    string SessionId,
    Guid OwnerTokenId,
    string PlayerName,
    string RoomName,
    AgentSessionState State,
    bool IsConnected,
    bool IsObserver,
    int SeatIndex,
    bool HasPendingAction,
    DateTime CreatedAtUtc);

public sealed record AgentThoughtNotification(
    Guid OwnerTokenId,
    string SessionId,
    string PlayerName,
    string RoomName,
    int TurnCount,
    string Phase,
    string Summary,
    DateTime TimestampUtc);
