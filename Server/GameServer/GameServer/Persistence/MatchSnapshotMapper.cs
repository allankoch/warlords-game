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

    public static MatchSnapshotDto FromState(MatchState state, int schemaVersion = 1) =>
        new(
            schemaVersion,
            state.GameId,
            new MatchSettingsDto(
                state.Settings.MapId,
                state.Settings.MinPlayers,
                state.Settings.MaxPlayers,
                state.Settings.AutoStart,
                state.Settings.TurnTimeLimitSeconds,
                state.Settings.DisconnectGraceSeconds),
            state.HostPlayerId,
            state.Phase,
            state.Players.Values.OrderBy(p => p.PlayerId, StringComparer.Ordinal).ToArray(),
            state.Ready.Values.OrderBy(r => r.PlayerId, StringComparer.Ordinal).ToArray(),
            state.SlotsByPlayerId.OrderBy(kvp => kvp.Key, StringComparer.Ordinal).Select(kvp => new PlayerSlotDto(kvp.Key, kvp.Value)).ToArray(),
            state.Entities.Values.OrderBy(e => e.EntityId, StringComparer.Ordinal).ToArray(),
            new TurnStateDto(state.Turns.Order.ToArray(), state.Turns.CurrentIndex, state.Turns.Started, state.Turns.TurnNumber),
            state.TurnEndsAtUnixSeconds,
            state.DisconnectedSinceUnixSeconds.Select(kvp => new PlayerDisconnectDto(kvp.Key, kvp.Value)).OrderBy(d => d.PlayerId, StringComparer.Ordinal).ToArray(),
            state.LastAction);

    public static MatchState ToState(MatchSnapshotDto snapshot, bool markAllDisconnected = true)
    {
        var settings = new MatchSettings(
            snapshot.Settings.MapId,
            snapshot.Settings.MinPlayers,
            snapshot.Settings.MaxPlayers,
            snapshot.Settings.AutoStart,
            snapshot.Settings.TurnTimeLimitSeconds,
            snapshot.Settings.DisconnectGraceSeconds);

        var disconnectedSince = markAllDisconnected
            ? ImmutableDictionary<string, long>.Empty.WithComparers(StringComparer.Ordinal)
            : snapshot.DisconnectedSince.ToImmutableDictionary(d => d.PlayerId, d => d.DisconnectedAtUnixSeconds, StringComparer.Ordinal);

        var players = snapshot.Players
            .Select(p => markAllDisconnected ? p with { IsConnected = false } : p)
            .ToImmutableDictionary(p => p.PlayerId, p => p, StringComparer.Ordinal);

        var ready = snapshot.Ready
            .Select(r => new PlayerReadyDto(r.PlayerId, false))
            .ToImmutableDictionary(r => r.PlayerId, r => r, StringComparer.Ordinal);

        var slots = (snapshot.Slots ?? Array.Empty<PlayerSlotDto>())
            .ToImmutableDictionary(s => s.PlayerId, s => s.Slot, StringComparer.Ordinal);

        var entities = snapshot.Entities.ToImmutableDictionary(e => e.EntityId, e => e, StringComparer.Ordinal);
        var turns = new TurnState(
            snapshot.Turns.Order.ToImmutableArray(),
            snapshot.Turns.CurrentIndex,
            snapshot.Turns.Started,
            snapshot.Turns.TurnNumber);

        return new MatchState(
            snapshot.GameId,
            settings,
            snapshot.HostPlayerId,
            snapshot.Phase,
            players,
            ready,
            entities,
            slots,
            turns,
            snapshot.TurnEndsAtUnixSeconds,
            disconnectedSince,
            snapshot.LastAction);
    }

    public static string Serialize(MatchSnapshotDto dto) =>
        JsonSerializer.Serialize(dto, JsonOptions);

    public static MatchSnapshotDto Deserialize(string json) =>
        JsonSerializer.Deserialize<MatchSnapshotDto>(json, JsonOptions) ??
        throw new InvalidOperationException("Invalid snapshot JSON.");
}
