using GameServer.Protocol;

namespace GameServer.Persistence;

public record MatchSnapshotDto(
    int SchemaVersion,
    string GameId,
    MatchSettingsDto Settings,
    string Phase,
    TurnStateDto Turns,
    long? TurnEndsAtUnixSeconds,
    PlayerActionDto? LastAction,
    string? HostSeatId = null,
    IReadOnlyList<SeatSnapshotDto>? Seats = null,
    IReadOnlyList<EntityStateDto>? Entities = null,
    string? HostPlayerId = null,
    IReadOnlyList<PlayerPresenceDto>? Players = null,
    IReadOnlyList<PlayerReadyDto>? Ready = null,
    IReadOnlyList<PlayerSlotDto>? Slots = null,
    IReadOnlyList<PlayerDisconnectDto>? DisconnectedSince = null);

public record SeatSnapshotDto(
    string SeatId,
    string? ClaimedByPlayerId,
    string? DisplayName,
    bool IsConnected,
    bool IsReady,
    long? DisconnectedSinceUnixSeconds,
    bool IsActive);

public record MatchSettingsDto(string MapId, int MinPlayers, int MaxPlayers, bool AutoStart, int TurnTimeLimitSeconds, int DisconnectGraceSeconds);

public record TurnStateDto(IReadOnlyList<string> Order, int CurrentIndex, bool Started, int TurnNumber);

public record PlayerDisconnectDto(string PlayerId, long DisconnectedAtUnixSeconds);
