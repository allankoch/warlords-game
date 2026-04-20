using System.Collections.Immutable;
using System.Text.Json;
using GameServer.Game.Engine;
using GameServer.Protocol;

namespace GameServer.Persistence;

public static class MatchSnapshotMapper
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static MatchSnapshotDto FromState(MatchState state, int schemaVersion = 2) =>
        new(
            SchemaVersion: schemaVersion,
            GameId: state.GameId,
            Settings: new MatchSettingsDto(
                state.Settings.MapId,
                state.Settings.MinPlayers,
                state.Settings.MaxPlayers,
                state.Settings.AutoStart,
                state.Settings.TurnTimeLimitSeconds,
                state.Settings.DisconnectGraceSeconds),
            Phase: state.Phase,
            Turns: new TurnStateDto(state.Turns.Order.ToArray(), state.Turns.CurrentIndex, state.Turns.Started, state.Turns.TurnNumber),
            TurnEndsAtUnixSeconds: state.TurnEndsAtUnixSeconds,
            LastAction: state.LastAction,
            HostSeatId: state.HostSeatId,
            Seats: state.Seats.Values
                .OrderBy(seat => seat.SeatId, StringComparer.Ordinal)
                .Select(seat => new SeatSnapshotDto(
                    seat.SeatId,
                    seat.ClaimedByPlayerId,
                    seat.DisplayName,
                    seat.IsConnected,
                    seat.IsReady,
                    seat.DisconnectedSinceUnixSeconds,
                    seat.IsActive))
                .ToArray(),
            Entities: state.Entities.Values.OrderBy(entity => entity.EntityId, StringComparer.Ordinal).ToArray(),
            HostPlayerId: state.HostPlayerId,
            Players: state.Players.Values.OrderBy(player => player.PlayerId, StringComparer.Ordinal).ToArray(),
            Ready: state.Ready.Values.OrderBy(player => player.PlayerId, StringComparer.Ordinal).ToArray(),
            Slots: state.SlotsByPlayerId.OrderBy(slot => slot.Key, StringComparer.Ordinal).Select(slot => new PlayerSlotDto(slot.Key, slot.Value)).ToArray(),
            DisconnectedSince: state.DisconnectedSinceUnixSeconds
                .Select(item => new PlayerDisconnectDto(item.Key, item.Value))
                .OrderBy(item => item.PlayerId, StringComparer.Ordinal)
                .ToArray());

    public static MatchState ToState(MatchSnapshotDto snapshot, bool markAllDisconnected = true)
    {
        var settings = new MatchSettings(
            snapshot.Settings.MapId,
            snapshot.Settings.MinPlayers,
            snapshot.Settings.MaxPlayers,
            snapshot.Settings.AutoStart,
            snapshot.Settings.TurnTimeLimitSeconds,
            snapshot.Settings.DisconnectGraceSeconds);

        if (snapshot.Seats is { Count: > 0 })
        {
            var loadedSeats = snapshot.Seats
                .ToImmutableDictionary(
                    seat => seat.SeatId,
                    seat => new SeatState(
                        seat.SeatId,
                        seat.ClaimedByPlayerId,
                        seat.DisplayName,
                        markAllDisconnected && !string.IsNullOrWhiteSpace(seat.ClaimedByPlayerId) ? false : seat.IsConnected,
                        markAllDisconnected ? false : seat.IsReady,
                        markAllDisconnected ? null : seat.DisconnectedSinceUnixSeconds,
                        seat.IsActive),
                    StringComparer.Ordinal);

            var loadedEntities = (snapshot.Entities ?? Array.Empty<EntityStateDto>())
                .ToImmutableDictionary(entity => entity.EntityId, entity => entity, StringComparer.Ordinal);

            var loadedTurns = new TurnState(
                snapshot.Turns.Order.ToImmutableArray(),
                snapshot.Turns.CurrentIndex,
                snapshot.Turns.Started,
                snapshot.Turns.TurnNumber);

            return new MatchState(
                snapshot.GameId,
                settings,
                snapshot.HostSeatId ?? MatchState.SeatOrder[0],
                snapshot.Phase,
                loadedSeats,
                loadedEntities,
                loadedTurns,
                snapshot.TurnEndsAtUnixSeconds,
                snapshot.LastAction);
        }

        var seatIds = MatchState.SeatOrder.Take(settings.MaxPlayers).ToArray();
        var seats = seatIds.ToImmutableDictionary(
            seatId => seatId,
            seatId => new SeatState(seatId, null, null, false, false, null, false),
            StringComparer.Ordinal);

        var slotByPlayerId = (snapshot.Slots ?? Array.Empty<PlayerSlotDto>())
            .ToDictionary(slot => slot.PlayerId, slot => slot.Slot, StringComparer.Ordinal);
        var readyByPlayerId = (snapshot.Ready ?? Array.Empty<PlayerReadyDto>())
            .ToDictionary(ready => ready.PlayerId, ready => ready.IsReady, StringComparer.Ordinal);
        var disconnectedByPlayerId = (snapshot.DisconnectedSince ?? Array.Empty<PlayerDisconnectDto>())
            .ToDictionary(player => player.PlayerId, player => player.DisconnectedAtUnixSeconds, StringComparer.Ordinal);

        foreach (var player in snapshot.Players ?? Array.Empty<PlayerPresenceDto>())
        {
            var seatId = slotByPlayerId.TryGetValue(player.PlayerId, out var mappedSeatId) ? mappedSeatId : MatchState.SeatOrder.First();
            if (!seats.TryGetValue(seatId, out var seat))
            {
                continue;
            }

            seats = seats.SetItem(seatId, new SeatState(
                seatId,
                player.PlayerId,
                player.DisplayName,
                markAllDisconnected ? false : player.IsConnected,
                markAllDisconnected ? false : readyByPlayerId.GetValueOrDefault(player.PlayerId),
                markAllDisconnected ? null : disconnectedByPlayerId.GetValueOrDefault(player.PlayerId),
                true));
        }

        var entities = (snapshot.Entities ?? Array.Empty<EntityStateDto>())
            .Select(entity =>
            {
                var ownerSeatId = slotByPlayerId.TryGetValue(entity.OwnerPlayerId, out var mappedSeatId) ? mappedSeatId : entity.OwnerPlayerId;
                return entity with { OwnerPlayerId = ownerSeatId };
            })
            .ToImmutableDictionary(entity => entity.EntityId, entity => entity, StringComparer.Ordinal);

        var turnOrder = snapshot.Turns.Order
            .Select(item => slotByPlayerId.TryGetValue(item, out var mappedSeatId) ? mappedSeatId : item)
            .Where(item => seats.ContainsKey(item))
            .ToImmutableArray();
        if (turnOrder.IsDefaultOrEmpty)
        {
            turnOrder = MatchState.SeatOrder.Take(settings.MaxPlayers).ToImmutableArray();
        }

        var turns = new TurnState(
            turnOrder,
            snapshot.Turns.CurrentIndex >= 0 && snapshot.Turns.CurrentIndex < turnOrder.Length ? snapshot.Turns.CurrentIndex : 0,
            snapshot.Turns.Started,
            snapshot.Turns.TurnNumber);

        var hostSeatId = snapshot.HostPlayerId is not null && slotByPlayerId.TryGetValue(snapshot.HostPlayerId, out var mappedHostSeatId)
            ? mappedHostSeatId
            : MatchState.SeatOrder[0];

        return new MatchState(
            snapshot.GameId,
            settings,
            hostSeatId,
            snapshot.Phase,
            seats,
            entities,
            turns,
            snapshot.TurnEndsAtUnixSeconds,
            snapshot.LastAction);
    }

    public static string Serialize(MatchSnapshotDto dto) =>
        JsonSerializer.Serialize(dto, JsonOptions);

    public static MatchSnapshotDto Deserialize(string json) =>
        JsonSerializer.Deserialize<MatchSnapshotDto>(json, JsonOptions) ??
        throw new InvalidOperationException("Invalid snapshot JSON.");
}
