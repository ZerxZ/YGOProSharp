namespace YGOProSharp.Web;

// ==================== 管理后台与共享请求 DTO ====================

public sealed record LoginRequestDto(string? Username, string? Password);

public sealed record ChangePasswordRequestDto(
    string? CurrentPassword,
    string? NewPassword,
    string? NewUsername = null,
    string? DisplayName = null);

public sealed record FlushCacheRequestDto(string? Namespace);

public sealed record MaintenanceRequestDto(bool Enabled, string? Reason);

public sealed record MessageRequestDto(string? Message);

public sealed record CreateRoomRequestDto(
    string? RoomName,
    string? Template,
    string? RulePrefix,
    bool IssuePlayerTokens,
    int? AiTokenCount = null,
    string[]? AiPlayerNames = null,
    int? Mode = null,
    int? Region = null,
    int? MasterRule = null,
    int? BanlistIndex = null,
    bool? NoBanlist = null,
    int? StartLp = null,
    int? StartHand = null,
    int? DrawCount = null,
    int? GameTimer = null,
    string? RandomDeckMode = null,
    int? ChaosMainDeckSize = null,
    bool? NoCheckDeck = null,
    bool? NoShuffleDeck = null,
    bool? EnablePriority = null,
    bool? SuddenDeath = null);

public sealed record UpdateRoomRulesRequestDto(
    string? PresetRuleString,
    int? Mode,
    int? Region,
    int? MasterRule,
    int? StartLp,
    int? StartHand,
    int? DrawCount,
    int? GameTimer,
    int? BanlistIndex,
    bool? NoCheckDeck,
    bool? NoShuffleDeck,
    string? RandomDeckMode);

public sealed record SuddenDeathRequestDto(bool Enabled, int? ExtraTurns);

public sealed record SpawnBotRequestDto(string? Deck, string? Dialog, int? Count, bool FillAll);

public sealed record KickPlayerRequestDto(int? SlotIndex, string? PlayerName);

public sealed record CreateBanRequestDto(string? TargetType, string? TargetValue, string? Reason, int? DurationMinutes);

public sealed record UpsertTournamentDeckDto(string? TournamentTag, string? PlayerName, string? YdkContent);

public sealed record UploadPoolDeckDto(string? Name, string? Category, string? YdkContent, string[]? Tags);

public sealed record ChaosPreviewRequestDto(int? MainSize, bool? PureChaos, bool? DiyOnly);

public sealed record UpsertDiyManifestDto(string? Id, string? Name, string? Version, string? Author, bool? Enabled, int? Priority);

public sealed record UpsertRoleRequestDto(string? Name, string? Description, string[]? Permissions);

public sealed record CreateUserRequestDto(
    string? Username,
    string? DisplayName,
    string? InitialPassword,
    string[]? RoleCodes,
    string[]? GrantPermissions,
    string[]? RevokePermissions,
    bool? MustChangePassword);

public sealed record UpdateUserRequestDto(
    string? Username,
    string? DisplayName,
    bool? Enabled,
    bool? UnlockAccount,
    string[]? RoleCodes,
    string[]? GrantPermissions,
    string[]? RevokePermissions);

public sealed record ResetUserPasswordRequestDto(
    string? CustomPassword,
    bool? MustChangePassword);

public sealed record IssueMcpTokenRequestDto(
    string? Name,
    string? McpRole,
    string[]? AllowedRooms,
    int? MaxSessions,
    bool? RequireApproval,
    int? ExpiresInHours,
    string? PreferredPlayerName = null,
    string? PersonaStyle = null);

public sealed record UpdateMcpTokenNicknameDto(
    string? PreferredPlayerName,
    string? PersonaStyle = null);

public sealed record DiyCardEntryDto(
    int? Id,
    int? Ot,
    int? Alias,
    long? Setcode,
    string? SetcodeHex,
    int? Type,
    int? Level,
    int? LScale,
    int? RScale,
    int? Race,
    int? Attribute,
    int? Attack,
    int? Defense,
    int? LinkMarker,
    int? Category,
    string? Name,
    string? Desc,
    string[]? Strings);

public sealed record UpsertDiyCardRequestDto(
    DiyCardEntryDto? Card,
    string? LuaScript,
    bool? AutoReload);

public sealed record SaveStringsConfDto(string? Content);

public sealed record SandboxTestCardRequestDto(
    DiyCardEntryDto? Card,
    string? LuaScript,
    bool? TestExecuteFirstActivation);

public sealed record ProposedCardChangeDto(
    DiyCardEntryDto? Card,
    string? LuaScript);

public sealed record ProposedScriptFileDto(
    string? FileName,
    string? Content);

public sealed record ProposedDeckFileDto(
    string? Name,
    string? YdkContent);

public sealed record ProposedCardImageDto(
    int CardId,
    string Base64Data);

public sealed record DiyProposalPayloadDto(
    ProposedCardChangeDto[]? Cards,
    ProposedScriptFileDto[]? Scripts,
    ProposedDeckFileDto[]? Decks,
    string? StringsConf,
    ProposedCardImageDto[]? Images = null,
    string? StagedYpkFileName = null,
    string? AuthorName = null);

public sealed record ConfirmYpkPreviewImportDto(
    string? PreviewToken,
    string? PackageId = null,
    string? Name = null,
    string? AuthorName = null,
    bool? Merge = null,
    bool? AutoReload = null);

public sealed record SubmitDiyProposalRequestDto(
    string? PackageId,
    string? Title,
    DiyProposalPayloadDto? Payload);

public sealed record ReviewDiyProposalRequestDto(
    bool Approve,
    string? Comment);

// ==================== 前台玩家门户专属 DTO ====================

public sealed record PortalRegisterRequestDto(string? Username, string? Password, string? DisplayName = null);

public sealed record PortalVpassRequestDto(string? PlayerName, string? Vpass);

public sealed record PortalSubmitProposalDto(
    string? PackageId,
    string? AuthorName,
    DiyCardEntryDto? Card,
    string? LuaScript,
    string? ImageBase64 = null);

public sealed record PortalPartnerTacticRequestDto(int? TeamId, string? Stance, string? Note);

public sealed record PortalAnalyzeDeckRequestDto(
    string? PresetName = null,
    string? YdkContent = null,
    string? DeckName = null,
    string? DeckHash = null);

public sealed record PortalRoomObserverDto(
    string PlayerName,
    string SeatOrigin);

public sealed record PortalRoomSlotDto(
    int SeatIndex,
    int TeamId,
    string? PlayerName,
    bool IsReady,
    bool IsHost,
    string SeatOrigin);

public sealed record PortalRoomSummaryDto(
    string RoomName,
    string HostPassword,
    string FullHostPassword,
    string? OwnerUsername,
    string State,
    int Mode,
    int Region,
    int MasterRule,
    int StartLp,
    int Team0Lp,
    int Team1Lp,
    int TurnCount,
    int DuelCount,
    int DuelistCount,
    int MaxDuelists,
    int ObserverCount,
    string? HostPlayerName,
    string RandomDeckMode,
    bool IsSuddenDeath,
    string? BanlistName,
    IReadOnlyList<PortalRoomSlotDto> Slots,
    IReadOnlyList<PortalRoomObserverDto> Observers);

public sealed record PortalCardItemDto(
    int Id,
    string Name,
    string Desc,
    int Ot,
    bool IsDiy,
    int Type,
    int Level,
    int Attack,
    int Defense);
