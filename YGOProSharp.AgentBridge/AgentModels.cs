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

/// <summary>
/// 控制返回给外部 MCP 客户端的观测快照详略级别，防止单次响应 Token 过大撑爆上下文。
/// </summary>
public enum AgentObservationDetail
{
    /// <summary>
    /// 仅返回动作执行结果与下一个待响应提示，不附带完整盘面快照（最省 Token，约 120~200 tokens）。
    /// </summary>
    None = 0,

    /// <summary>
    /// 极简盘面：仅含双方 LP、区域数量、手牌/场上卡名攻守、当前连锁与待决提示（省略墓地/除外列表、卡组表与卡片效果长文，约 250~400 tokens）。
    /// </summary>
    Minimal = 1,

    /// <summary>
    /// 标准精简盘面（默认）：含手牌/场上/墓地/除外基础卡片信息、增量事件与盘面变化摘要，省略 40 张初始卡组清单、里侧额外列表与场上卡片重复长效文（约 450~850 tokens）。
    /// </summary>
    Standard = 2,

    /// <summary>
    /// 全量盘面：额外包含本局初始卡组无序清单、完整已知额外卡组与所有可见卡片的简短效果文本。
    /// </summary>
    Full = 3
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
    int GraveCount,
    int BanishedCount,
    IReadOnlyList<AgentCardView> Hand,
    IReadOnlyList<AgentCardView> MonsterZone,
    IReadOnlyList<AgentCardView> SpellZone,
    IReadOnlyList<AgentCardView>? Graveyard,
    IReadOnlyList<AgentCardView>? Banished,
    IReadOnlyList<AgentCardView>? KnownExtraDeck);

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
    int EffectDescription,
    string? EffectText = null);

public sealed record AgentLegalActionChoice(
    int ActionIndex,
    string Kind,
    string Label,
    int? CardCode = null,
    string? CardName = null,
    string? Zone = null,
    int? EffectDescription = null,
    int? Attack = null,
    int? Defense = null,
    string? ShortEffect = null,
    string? EffectText = null);

public sealed record AgentPendingPrompt(
    long PromptId,
    string PromptType,
    string HintText,
    bool CanCancel,
    int MinSelect,
    int MaxSelect,
    bool SupportsMultiSelect,
    string SubmitField,
    IReadOnlyList<AgentLegalActionChoice> Choices,
    string? FormatKey = null);

public sealed record AgentDeckCountSummary(
    int CardCode,
    string? CardName,
    int Copies,
    int RemainingInDeck,
    string Section);

public sealed record AgentChatEntry(
    long Seq,
    string SpeakerName,
    string SpeakerRelation,
    int? SeatIndex,
    string Text,
    string? DetectedTacticalHint = null);

public sealed record AgentChatContext(
    string TrustLevel,
    string TrustNotice,
    IReadOnlyList<AgentChatEntry> Messages);

public sealed record AgentTagRotationSlot(
    int Order,
    int SeatIndex,
    string? PlayerName,
    string Relation);

public sealed record AgentTagNextActor(
    int SeatIndex,
    string? PlayerName,
    string Relation,
    string Note);

public sealed record AgentTagTeamContext(
    int MySeatIndex,
    int MyTeamId,
    int PartnerSeatIndex,
    string? PartnerName,
    IReadOnlyList<string> EnemyNames,
    string CurrentTurnRelation,
    string? NextTurnHint,
    IReadOnlyList<AgentTagRotationSlot>? TurnRotationOrder = null,
    string? SharedBoardHandoffNote = null,
    int? CurrentActiveSeatIndex = null,
    AgentTagNextActor? NextTurnActor = null,
    int? TeammateHandCount = null,
    bool? TeammateSurrenderRequested = null);

public sealed record AgentPartnerTactic(
    string Stance,
    string? Note,
    string UpdatedBy,
    DateTime UpdatedAtUtc,
    string? SharedBoardHandoffNote = null);

public sealed record AgentStrategicPlan(
    string? WinCondition,
    string? SuspectedOpponentDeck,
    string? TurnGoal,
    string? HandoffToPartner,
    string? Bo3SidePlan,
    DateTime UpdatedAtUtc);

public sealed record AgentBatchStep(
    string? PromptType = null,
    string? Kind = null,
    int? CardCode = null,
    string? LabelContains = null,
    bool Cancel = false,
    int? ActionIndex = null,
    string? Zone = null);

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
    int? RemainingTimeSeconds,
    string? DuelResult,
    string? LastError,
    IReadOnlyList<AgentLobbySlotView> LobbySlots,
    AgentFieldSideView SelfSide,
    AgentFieldSideView OpponentSide,
    IReadOnlyList<AgentChainLinkView> CurrentChain,
    IReadOnlyList<AgentDeckCountSummary>? OwnSubmittedDeckList,
    long LastEventSeq,
    IReadOnlyList<string>? NewEvents,
    IReadOnlyList<string>? BoardChanges,
    IReadOnlyList<string> RecentEvents,
    IReadOnlyList<AgentChatEntry>? RecentChats,
    string? ChatTrustNotice,
    AgentTagTeamContext? TagContext,
    AgentPartnerTactic? PartnerTactic,
    AgentStrategicPlan? StrategicPlan,
    bool HasPendingAction,
    AgentPendingPrompt? PendingPrompt,
    AgentChatContext? ChatContext = null);

public sealed record AgentActionResult(
    bool Success,
    string Message,
    long? HandledPromptId,
    AgentSessionState State,
    string? DuelResult,
    bool HasPendingAction,
    AgentPendingPrompt? NextPrompt,
    IReadOnlyList<string>? NewEvents,
    IReadOnlyList<string>? BoardChanges,
    AgentObservation? Observation,
    int AppliedSteps = 1,
    string? StopReason = null);

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
