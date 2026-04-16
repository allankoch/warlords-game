namespace GameServer.Protocol;

public record ConnectedDto(string PlayerId, string ReconnectToken);

public record CreateMatchRequestDto(
    string GameId,
    string MapId,
    int MinPlayers = 2,
    int MaxPlayers = 2,
    bool AutoStart = true,
    int TurnTimeLimitSeconds = 60,
    int DisconnectGraceSeconds = 120);

public record JoinGameRequestDto(string GameId);

public record JoinByCodeRequestDto(string Code);

public record ReadyUpRequestDto(bool IsReady);

public record ActionAcceptedDto(string ActionId, int StateVersion, long ServerActionSequence);

public record ActionRejectedDto(string ActionId, string Reason, int StateVersion, long ServerActionSequence);

public record PlayerJoinedDto(string GameId, string PlayerId);

public record PlayerLeftDto(string GameId, string PlayerId);

public record PlayerPresenceDto(string PlayerId, bool IsConnected);

public record PlayerReadyDto(string PlayerId, bool IsReady);

public record PlayerSlotDto(string PlayerId, string Slot);

public record EntityStateDto(
    string EntityId,
    string OwnerPlayerId,
    int X,
    int Y);

public record GameStateDto(
    string GameId,
    int Version,
    string MapId,
    string Phase,
    int MinPlayers,
    int MaxPlayers,
    IReadOnlyList<PlayerPresenceDto> Players,
    IReadOnlyList<PlayerReadyDto> Ready,
    IReadOnlyList<PlayerSlotDto> Slots,
    IReadOnlyList<EntityStateDto> Entities,
    string? CurrentTurnPlayerId,
    int TurnNumber,
    DateTimeOffset? TurnEndsAt,
    long ServerActionSequence,
    PlayerActionDto? LastAction,
    DateTimeOffset ServerTime);

public record SubmitActionResultDto(
    bool Accepted,
    string ActionId,
    string? Reason,
    int StateVersion,
    long ServerActionSequence,
    GameStateDto? GameState);

public record MatchSummaryDto(
    string GameId,
    string MapId,
    string Phase,
    int Version,
    long ServerActionSequence,
    int PlayerCount,
    DateTimeOffset SavedAt);
