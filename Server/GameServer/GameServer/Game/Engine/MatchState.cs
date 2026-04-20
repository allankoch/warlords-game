using System.Collections.Immutable;
using GameServer.Protocol;

namespace GameServer.Game.Engine;

public sealed record MatchState(
    string GameId,
    MatchSettings Settings,
    string HostSeatId,
    string Phase,
    ImmutableDictionary<string, SeatState> Seats,
    ImmutableDictionary<string, EntityStateDto> Entities,
    TurnState Turns,
    long? TurnEndsAtUnixSeconds,
    PlayerActionDto? LastAction)
{
    public static readonly ImmutableArray<string> SeatOrder =
    [
        "white", "red", "green", "black", "orange", "lightblue", "darkblue", "yellow"
    ];

    public string HostPlayerId => ResolveExternalPlayerId(HostSeatId);

    public string? CurrentTurnPlayerId => ResolveExternalPlayerIdOrNull(Turns.CurrentPlayerId);

    public bool HasUnclaimedRequiredSeats => Seats.Values.Any(seat => seat.IsActive && !seat.IsClaimed);

    public ImmutableArray<string> ActiveSeatIds =>
        SeatOrder.Where(seatId => Seats.TryGetValue(seatId, out var seat) && seat.IsActive).ToImmutableArray();

    public ImmutableDictionary<string, PlayerPresenceDto> Players =>
        Seats.Values
            .Where(seat => seat.IsClaimed)
            .Select(seat => new PlayerPresenceDto(seat.ClaimedByPlayerId!, seat.IsConnected, seat.DisplayName))
            .ToImmutableDictionary(player => player.PlayerId, player => player, StringComparer.Ordinal);

    public ImmutableDictionary<string, PlayerReadyDto> Ready =>
        Seats.Values
            .Where(seat => seat.IsClaimed)
            .Select(seat => new PlayerReadyDto(seat.ClaimedByPlayerId!, seat.IsReady))
            .ToImmutableDictionary(player => player.PlayerId, player => player, StringComparer.Ordinal);

    public ImmutableDictionary<string, string> SlotsByPlayerId =>
        Seats.Values
            .Where(seat => seat.IsClaimed)
            .ToImmutableDictionary(seat => seat.ClaimedByPlayerId!, seat => seat.SeatId, StringComparer.Ordinal);

    public ImmutableDictionary<string, long> DisconnectedSinceUnixSeconds =>
        Seats.Values
            .Where(seat => seat.IsClaimed && seat.DisconnectedSinceUnixSeconds is long and > 0)
            .ToImmutableDictionary(seat => seat.ClaimedByPlayerId!, seat => seat.DisconnectedSinceUnixSeconds!.Value, StringComparer.Ordinal);

    public static MatchState CreateNew(string gameId, MatchSettings settings, string? hostPlayerId = null)
    {
        var seatIds = SeatOrder.Take(settings.MaxPlayers).ToArray();
        var seats = seatIds.ToImmutableDictionary(
            seatId => seatId,
            seatId => new SeatState(seatId, null, null, false, false, null, false),
            StringComparer.Ordinal);

        return new(
            gameId,
            settings,
            seatIds[0],
            MatchPhases.Lobby,
            seats,
            ImmutableDictionary<string, EntityStateDto>.Empty.WithComparers(StringComparer.Ordinal),
            new TurnState(ImmutableArray<string>.Empty, 0, false, 0),
            null,
            null);
    }

    public string? FindSeatIdByPlayerId(string playerId) =>
        Seats.Values.FirstOrDefault(seat => string.Equals(seat.ClaimedByPlayerId, playerId, StringComparison.Ordinal))?.SeatId;

    public SeatState? GetSeatByPlayerId(string playerId)
    {
        var seatId = FindSeatIdByPlayerId(playerId);
        return seatId is not null && Seats.TryGetValue(seatId, out var seat) ? seat : null;
    }

    public SeatState? FindClaimableSeat() =>
        SeatOrder
            .Select(seatId => Seats.TryGetValue(seatId, out var seat) ? seat : null)
            .FirstOrDefault(seat => seat is not null && seat.IsActive && !seat.IsClaimed);

    public string? FindFirstOpenLobbySeatId() =>
        SeatOrder
            .Take(Settings.MaxPlayers)
            .FirstOrDefault(id => Seats.TryGetValue(id, out var seat) && !seat.IsClaimed);

    public string ResolveExternalPlayerId(string seatId) =>
        Seats.TryGetValue(seatId, out var seat) && seat.IsClaimed
            ? seat.ClaimedByPlayerId!
            : seatId;

    public string? ResolveExternalPlayerIdOrNull(string? seatId) =>
        seatId is null
            ? null
            : Seats.TryGetValue(seatId, out var seat) && seat.IsClaimed
                ? seat.ClaimedByPlayerId
                : null;
}

public static class MatchPhases
{
    public const string Lobby = "Lobby";
    public const string InProgress = "InProgress";
}
