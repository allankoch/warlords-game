using System.Collections.Immutable;
using GameServer.Protocol;

namespace GameServer.Game.Engine;

public sealed class GameEngine(IMapProvider maps) : IGameEngine
{
    private static readonly string[] SlotOrder =
    {
        "white", "red", "green", "black", "orange", "lightblue", "darkblue", "yellow"
    };

    public EngineResult<MatchState> AddOrReconnectPlayer(MatchState state, string playerId)
    {
        if (!string.Equals(state.Phase, MatchPhases.Lobby, StringComparison.Ordinal) &&
            !state.Players.ContainsKey(playerId))
        {
            return EngineResult<MatchState>.Fail(state, "MatchAlreadyStarted");
        }

        if (!state.Players.ContainsKey(playerId) && state.Players.Count >= state.Settings.MaxPlayers)
        {
            return EngineResult<MatchState>.Fail(state, "GameFull");
        }

        var players = state.Players.SetItem(playerId, new PlayerPresenceDto(playerId, true));
        var ready = state.Ready;
        if (string.Equals(state.Phase, MatchPhases.Lobby, StringComparison.Ordinal))
        {
            ready = ready.SetItem(playerId, new PlayerReadyDto(playerId, false));
        }

        var turns = state.Turns.EnsurePlayerInOrder(playerId);
        var slots = state.SlotsByPlayerId;
        if (!slots.ContainsKey(playerId))
        {
            var next = SlotOrder.FirstOrDefault(s => !slots.Values.Contains(s, StringComparer.Ordinal));
            if (next is null)
            {
                return EngineResult<MatchState>.Fail(state, "NoAvailableSlots");
            }
            slots = slots.SetItem(playerId, next);
        }

        var updated = state with { Players = players, Ready = ready, Turns = turns, SlotsByPlayerId = slots };
        updated = EnsureCurrentTurnIsEligible(updated);

        return MaybeAutoStart(updated);
    }

    public EngineResult<MatchState> SetConnected(MatchState state, string playerId, bool isConnected)
    {
        if (!state.Players.TryGetValue(playerId, out var existing))
        {
            return EngineResult<MatchState>.Ok(state);
        }

        var players = state.Players.SetItem(playerId, existing with { IsConnected = isConnected });
        var ready = state.Ready;
        if (!isConnected && ready.ContainsKey(playerId))
        {
            ready = ready.SetItem(playerId, new PlayerReadyDto(playerId, false));
        }

        var disconnectedSince = state.DisconnectedSinceUnixSeconds;
        if (isConnected)
        {
            disconnectedSince = disconnectedSince.Remove(playerId);
        }
        else if (!disconnectedSince.ContainsKey(playerId))
        {
            disconnectedSince = disconnectedSince.SetItem(playerId, 0);
        }

        var updated = state with { Players = players, Ready = ready, DisconnectedSinceUnixSeconds = disconnectedSince };
        updated = EnsureCurrentTurnIsEligible(updated);
        return EngineResult<MatchState>.Ok(updated);
    }

    public EngineResult<MatchState> RemovePlayer(MatchState state, string playerId)
    {
        if (!state.Players.ContainsKey(playerId))
        {
            return EngineResult<MatchState>.Ok(state);
        }

        var players = state.Players.Remove(playerId);
        var ready = state.Ready.Remove(playerId);
        var turns = state.Turns.RemovePlayer(playerId);

        var entities = state.Entities;
        foreach (var entity in entities.Values.Where(e => string.Equals(e.OwnerPlayerId, playerId, StringComparison.Ordinal)).ToArray())
        {
            entities = entities.Remove(entity.EntityId);
        }

        var updated = state with
        {
            Players = players,
            Ready = ready,
            Entities = entities,
            Turns = turns,
            DisconnectedSinceUnixSeconds = state.DisconnectedSinceUnixSeconds.Remove(playerId)
        };
        updated = EnsureCurrentTurnIsEligible(updated);
        return EngineResult<MatchState>.Ok(updated);
    }

    public EngineResult<MatchState> SetReady(MatchState state, string playerId, bool isReady)
    {
        if (!string.Equals(state.Phase, MatchPhases.Lobby, StringComparison.Ordinal))
        {
            return EngineResult<MatchState>.Fail(state, "MatchAlreadyStarted");
        }

        if (!state.Players.TryGetValue(playerId, out var presence))
        {
            return EngineResult<MatchState>.Fail(state, "UnknownPlayer");
        }

        if (!presence.IsConnected)
        {
            return EngineResult<MatchState>.Fail(state, "PlayerDisconnected");
        }

        var ready = state.Ready.SetItem(playerId, new PlayerReadyDto(playerId, isReady));
        var updated = state with { Ready = ready };
        return MaybeAutoStart(updated);
    }

    public EngineResult<MatchState> StartMatch(MatchState state, string requestingPlayerId)
    {
        if (!string.Equals(state.Phase, MatchPhases.Lobby, StringComparison.Ordinal))
        {
            return EngineResult<MatchState>.Fail(state, "MatchAlreadyStarted");
        }

        if (!string.Equals(requestingPlayerId, state.HostPlayerId, StringComparison.Ordinal))
        {
            return EngineResult<MatchState>.Fail(state, "HostOnly");
        }

        if (!CanStart(state))
        {
            return EngineResult<MatchState>.Fail(state, "NotReady");
        }

        return EngineResult<MatchState>.Ok(StartMatchInternal(state));
    }

    public EngineResult<MatchState> ApplyAction(MatchState state, string playerId, PlayerActionDto action)
    {
        if (!string.Equals(state.Phase, MatchPhases.InProgress, StringComparison.Ordinal))
        {
            return EngineResult<MatchState>.Fail(state, "MatchNotStarted");
        }

        if (!state.Players.TryGetValue(playerId, out var presence))
        {
            return EngineResult<MatchState>.Fail(state, "UnknownPlayer");
        }

        if (!presence.IsConnected)
        {
            return EngineResult<MatchState>.Fail(state, "PlayerDisconnected");
        }

        if (state.Turns.CurrentPlayerId is not null && !state.Turns.IsPlayersTurn(playerId))
        {
            return EngineResult<MatchState>.Fail(state, "NotYourTurn");
        }

        var validationResult = ValidateAndApply(state, playerId, action);
        if (!validationResult.Success)
        {
            return validationResult;
        }

        return EngineResult<MatchState>.Ok(validationResult.State with { LastAction = action });
    }

    public EngineResult<MatchState> Tick(MatchState state, long nowUnixSeconds)
    {
        if (!string.Equals(state.Phase, MatchPhases.InProgress, StringComparison.Ordinal))
        {
            return EngineResult<MatchState>.Ok(state);
        }

        var updated = state;

        updated = UpdateDisconnectGrace(updated, nowUnixSeconds);
        updated = EnsureCurrentTurnIsEligible(updated);
        updated = UpdateTurnTimeout(updated, nowUnixSeconds);

        return EngineResult<MatchState>.Ok(updated);
    }

    private EngineResult<MatchState> ValidateAndApply(MatchState state, string playerId, PlayerActionDto action) =>
        action switch
        {
            EndTurnActionDto => EngineResult<MatchState>.Ok(AdvanceTurnForEndTurn(state)),
            MoveEntityActionDto move => ApplyMove(state, playerId, move),
            _ => EngineResult<MatchState>.Fail(state, "UnknownAction")
        };

    private static MatchState AdvanceTurnForEndTurn(MatchState state)
    {
        var turns = state.Turns.AdvanceTurn(id => state.Players.TryGetValue(id, out var p) && p.IsConnected);
        return state with { Turns = turns, TurnEndsAtUnixSeconds = null };
    }

    private EngineResult<MatchState> ApplyMove(MatchState state, string playerId, MoveEntityActionDto move)
    {
        if (string.IsNullOrWhiteSpace(move.EntityId))
        {
            return EngineResult<MatchState>.Fail(state, "EntityIdRequired");
        }

        if (!state.Entities.TryGetValue(move.EntityId, out var entity))
        {
            return EngineResult<MatchState>.Fail(state, "UnknownEntity");
        }

        if (!string.Equals(entity.OwnerPlayerId, playerId, StringComparison.Ordinal))
        {
            return EngineResult<MatchState>.Fail(state, "NotEntityOwner");
        }

        // Bounds and blocked tiles are map-driven.
        // Map tiles are deterministic by Settings.MapId, which is persisted in state/settings.
        // Engine stays testable by swapping IMapProvider.
        // Note: this throws if map is missing; callers should ensure map exists when creating a match.
        // We treat missing maps as invalid configuration rather than a gameplay rejection.
        if (!IsInBounds(state, move.X, move.Y))
        {
            return EngineResult<MatchState>.Fail(state, "OutOfBounds");
        }

        if (IsBlocked(state, move.X, move.Y))
        {
            return EngineResult<MatchState>.Fail(state, "BlockedTile");
        }

        var dx = Math.Abs(move.X - entity.X);
        var dy = Math.Abs(move.Y - entity.Y);
        if (dx + dy != 1)
        {
            return EngineResult<MatchState>.Fail(state, "InvalidMoveRange");
        }

        if (state.Entities.Values.Any(e => e.EntityId != entity.EntityId && e.X == move.X && e.Y == move.Y))
        {
            return EngineResult<MatchState>.Fail(state, "CellOccupied");
        }

        var entities = state.Entities.SetItem(entity.EntityId, entity with { X = move.X, Y = move.Y });
        return EngineResult<MatchState>.Ok(state with { Entities = entities });
    }

    private bool IsInBounds(MatchState state, int x, int y)
    {
        var map = maps.Get(state.Settings.MapId);
        return x >= 0 && y >= 0 && x < map.Width && y < map.Height;
    }

    private bool IsBlocked(MatchState state, int x, int y)
    {
        var map = maps.Get(state.Settings.MapId);
        var i = y * map.Width + x;
        return map.Blocked[i];
    }

    private EngineResult<MatchState> MaybeAutoStart(MatchState state)
    {
        if (state.Settings.AutoStart && string.Equals(state.Phase, MatchPhases.Lobby, StringComparison.Ordinal) && CanStart(state))
        {
            return EngineResult<MatchState>.Ok(StartMatchInternal(state));
        }

        return EngineResult<MatchState>.Ok(state);
    }

    private static bool CanStart(MatchState state)
    {
        var connectedPlayers = state.Players.Values.Where(p => p.IsConnected).Select(p => p.PlayerId).ToArray();
        if (connectedPlayers.Length < state.Settings.MinPlayers)
        {
            return false;
        }

        return connectedPlayers.All(playerId => state.Ready.TryGetValue(playerId, out var r) && r.IsReady);
    }

    private MatchState StartMatchInternal(MatchState state)
    {
        var updated = state with { Phase = MatchPhases.InProgress };

        var connected = updated.Players.Values.Where(p => p.IsConnected).Select(p => p.PlayerId).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        var turns = updated.Turns.Start(id => updated.Players.TryGetValue(id, out var p) && p.IsConnected);

        // Map-driven spawn: each player spawns at the top-left tile of their faction's castle.
        var entities = updated.Entities;
        var map = maps.Get(updated.Settings.MapId);
        foreach (var playerId in connected)
        {
            if (!updated.SlotsByPlayerId.TryGetValue(playerId, out var slot))
            {
                continue;
            }

            if (!map.SpawnTopLeftByOwner.TryGetValue(slot, out var spawn))
            {
                continue;
            }

            var entityId = $"unit-{playerId}";
            if (!entities.ContainsKey(entityId))
            {
                entities = entities.SetItem(entityId, new EntityStateDto(entityId, playerId, spawn.X, spawn.Y));
            }
        }

        updated = updated with { Turns = turns, Entities = entities, TurnEndsAtUnixSeconds = null };
        return updated;
    }

    private static MatchState UpdateTurnTimeout(MatchState state, long nowUnixSeconds)
    {
        if (!state.Turns.Started || state.Turns.CurrentPlayerId is null)
        {
            return state;
        }

        var limit = Math.Max(1, state.Settings.TurnTimeLimitSeconds);
        var endsAt = state.TurnEndsAtUnixSeconds;
        if (endsAt is null || endsAt <= 0) return state;

        if (nowUnixSeconds < endsAt.Value)
        {
            return state;
        }

        var advanced = AdvanceTurnForEndTurn(state);
        var sysActionId = $"sys:endTurn:{state.GameId}:{nowUnixSeconds}:{advanced.Turns.TurnNumber}";
        advanced = advanced with
        {
            LastAction = new EndTurnActionDto { ActionId = sysActionId, ClientSequence = 0 },
            TurnEndsAtUnixSeconds = nowUnixSeconds + limit
        };

        return advanced;
    }

    private static MatchState EnsureCurrentTurnIsEligible(MatchState state)
    {
        if (!string.Equals(state.Phase, MatchPhases.InProgress, StringComparison.Ordinal))
        {
            return state;
        }

        var current = state.Turns.CurrentPlayerId;
        if (current is null)
        {
            return state;
        }

        if (state.Players.TryGetValue(current, out var presence) && presence.IsConnected)
        {
            return state;
        }

        var turns = state.Turns.AdvanceTurn(id => state.Players.TryGetValue(id, out var p) && p.IsConnected);
        return state with { Turns = turns, TurnEndsAtUnixSeconds = null };
    }

    private static MatchState UpdateDisconnectGrace(MatchState state, long nowUnixSeconds)
    {
        var grace = Math.Max(0, state.Settings.DisconnectGraceSeconds);
        var disconnectedSince = state.DisconnectedSinceUnixSeconds;

        foreach (var (playerId, presence) in state.Players)
        {
            if (presence.IsConnected)
            {
                if (disconnectedSince.ContainsKey(playerId))
                {
                    disconnectedSince = disconnectedSince.Remove(playerId);
                }

                continue;
            }

            if (!disconnectedSince.TryGetValue(playerId, out var since) || since <= 0)
            {
                disconnectedSince = disconnectedSince.SetItem(playerId, nowUnixSeconds);
                continue;
            }

            if (grace > 0 && nowUnixSeconds - since < grace)
            {
                continue;
            }

            var removed = RemovePlayerCore(state with { DisconnectedSinceUnixSeconds = disconnectedSince }, playerId);
            return removed;
        }

        return state with { DisconnectedSinceUnixSeconds = disconnectedSince };
    }

    private static MatchState RemovePlayerCore(MatchState state, string playerId)
    {
        if (!state.Players.ContainsKey(playerId))
        {
            return state;
        }

        var players = state.Players.Remove(playerId);
        var ready = state.Ready.Remove(playerId);
        var turns = state.Turns.RemovePlayer(playerId);

        var entities = state.Entities;
        foreach (var entity in entities.Values.Where(e => string.Equals(e.OwnerPlayerId, playerId, StringComparison.Ordinal)).ToArray())
        {
            entities = entities.Remove(entity.EntityId);
        }

        var updated = state with
        {
            Players = players,
            Ready = ready,
            Entities = entities,
            Turns = turns,
            DisconnectedSinceUnixSeconds = state.DisconnectedSinceUnixSeconds.Remove(playerId)
        };

        return EnsureCurrentTurnIsEligible(updated);
    }
}
