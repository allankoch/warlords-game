using GameServer.Protocol;

namespace GameServer.Persistence;

public record MatchSnapshotDto(
    int SchemaVersion,
    string GameId,
    MatchSettingsDto Settings,
    string HostPlayerId,
    string Phase,
    IReadOnlyList<PlayerPresenceDto> Players,
    IReadOnlyList<PlayerReadyDto> Ready,
    IReadOnlyList<PlayerSlotDto> Slots,
    IReadOnlyList<EntityStateDto> Entities,
    TurnStateDto Turns,
    long? TurnEndsAtUnixSeconds,
    IReadOnlyList<PlayerDisconnectDto> DisconnectedSince,
    PlayerActionDto? LastAction);

public record MatchSettingsDto(string MapId, int MinPlayers, int MaxPlayers, bool AutoStart, int TurnTimeLimitSeconds, int DisconnectGraceSeconds);

public record TurnStateDto(IReadOnlyList<string> Order, int CurrentIndex, bool Started, int TurnNumber);

public record PlayerDisconnectDto(string PlayerId, long DisconnectedAtUnixSeconds);
