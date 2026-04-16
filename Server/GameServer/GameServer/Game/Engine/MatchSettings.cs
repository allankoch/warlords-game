namespace GameServer.Game.Engine;

public sealed record MatchSettings(
    string MapId,
    int MinPlayers,
    int MaxPlayers,
    bool AutoStart,
    int TurnTimeLimitSeconds,
    int DisconnectGraceSeconds);
