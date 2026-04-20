namespace GameServer.Protocol;

public record ConnectedDto(string PlayerId, string ReconnectToken, string? DisplayName = null, string? ResumeGameId = null);

public record CreateMatchRequestDto(
    string GameId,
    string MapId,
    int MinPlayers = 2,
    int MaxPlayers = 2,
    bool AutoStart = true,
    int TurnTimeLimitSeconds = 0,
    int DisconnectGraceSeconds = 120);

public record JoinGameRequestDto(string GameId);

public record ClaimSeatRequestDto(string GameId, string SeatId);

public record JoinByCodeRequestDto(string Code);

public record ReadyUpRequestDto(bool IsReady);

public record SendLobbyChatRequestDto(string Message);

public record ActionAcceptedDto(string ActionId, int StateVersion, long ServerActionSequence);

public record ActionRejectedDto(string ActionId, string Reason, int StateVersion, long ServerActionSequence);

public record PlayerJoinedDto(string GameId, string PlayerId, string? DisplayName = null);

public record PlayerLeftDto(string GameId, string PlayerId, string? DisplayName = null);

public record LobbyChatMessageDto(
    string MessageId,
    string PlayerId,
    string? DisplayName,
    string Message,
    DateTimeOffset SentAt);

public record PlayerPresenceDto(string PlayerId, bool IsConnected, string? DisplayName = null);

public record PlayerReadyDto(string PlayerId, bool IsReady);

public record PlayerSlotDto(string PlayerId, string Slot);

public record SeatStatusDto(
    string SeatId,
    bool IsActive,
    bool IsClaimed,
    string? ClaimedByPlayerId,
    string? DisplayName,
    bool IsConnected,
    bool IsReady,
    bool HasUnits);

public record EntityStateDto(
    string EntityId,
    string OwnerPlayerId,
    int X,
    int Y);

[System.Text.Json.Serialization.JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(AvailableEndTurnActionDto), "endTurn")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(AvailableMoveActionDto), "move")]
[System.Text.Json.Serialization.JsonDerivedType(typeof(AvailableAttackActionDto), "attack")]
public abstract record AvailableActionDto;

public sealed record AvailableEndTurnActionDto() : AvailableActionDto;

public sealed record AvailableMoveActionDto(
    string EntityId,
    int X,
    int Y) : AvailableActionDto;

public sealed record AvailableAttackActionDto(
    string EntityId,
    string TargetEntityId,
    int X,
    int Y) : AvailableActionDto;

public record GameStateDto(
    string GameId,
    int Version,
    string MapId,
    string Phase,
    int MinPlayers,
    int MaxPlayers,
    string HostPlayerId,
    IReadOnlyList<PlayerPresenceDto> Players,
    IReadOnlyList<PlayerReadyDto> Ready,
    IReadOnlyList<PlayerSlotDto> Slots,
    IReadOnlyList<EntityStateDto> Entities,
    IReadOnlyList<AvailableActionDto> AvailableActions,
    string? CurrentTurnPlayerId,
    int TurnNumber,
    DateTimeOffset? TurnEndsAt,
    long ServerActionSequence,
    PlayerActionDto? LastAction,
    DateTimeOffset ServerTime,
    IReadOnlyList<SeatStatusDto>? Seats = null,
    bool IsPausedForSeatClaim = false);

public record SubmitActionResultDto(
    bool Accepted,
    string ActionId,
    string? Reason,
    int StateVersion,
    long ServerActionSequence,
    GameStateDto? GameState);

public record JoinGameResultDto(
    bool Joined,
    string? Reason,
    GameStateDto? GameState);

public record ResumeGameResultDto(
    bool Resumed,
    string? Reason,
    GameStateDto? GameState);

public record ClaimSeatResultDto(
    bool Claimed,
    string? Reason,
    GameStateDto? GameState);

public record MatchSummaryDto(
    string GameId,
    string MapId,
    string Phase,
    bool IsPausedForSeatClaim,
    string HostPlayerId,
    string? HostDisplayName,
    int MaxPlayers,
    int Version,
    long ServerActionSequence,
    int PlayerCount,
    int OpenSeatCount,
    DateTimeOffset SavedAt);
