using System.Collections.Immutable;
using GameServer.Protocol;

namespace GameServer.Game.Engine;

public sealed record MatchState(
    string GameId,
    MatchSettings Settings,
    string HostPlayerId,
    string Phase,
    ImmutableDictionary<string, PlayerPresenceDto> Players,
    ImmutableDictionary<string, PlayerReadyDto> Ready,
    ImmutableDictionary<string, EntityStateDto> Entities,
    ImmutableDictionary<string, string> SlotsByPlayerId,
    TurnState Turns,
    long? TurnEndsAtUnixSeconds,
    ImmutableDictionary<string, long> DisconnectedSinceUnixSeconds,
    PlayerActionDto? LastAction)
{
    public static MatchState CreateNew(string gameId, MatchSettings settings, string hostPlayerId) =>
        new(
            gameId,
            settings,
            hostPlayerId,
            MatchPhases.Lobby,
            ImmutableDictionary<string, PlayerPresenceDto>.Empty.WithComparers(StringComparer.Ordinal),
            ImmutableDictionary<string, PlayerReadyDto>.Empty.WithComparers(StringComparer.Ordinal),
            ImmutableDictionary<string, EntityStateDto>.Empty.WithComparers(StringComparer.Ordinal),
            ImmutableDictionary<string, string>.Empty.WithComparers(StringComparer.Ordinal),
            TurnState.Empty,
            null,
            ImmutableDictionary<string, long>.Empty.WithComparers(StringComparer.Ordinal),
            null);
}

public static class MatchPhases
{
    public const string Lobby = "Lobby";
    public const string InProgress = "InProgress";
}
