using System.Collections.Immutable;
using GameServer.Protocol;

namespace GameServer.Game.Engine;

public sealed class GameEngine(IMapProvider maps) : IGameEngine
{
    public EngineResult<MatchState> AddOrReconnectPlayer(MatchState state, string playerId, string? displayName)
    {
        var seatId = state.FindSeatIdByPlayerId(playerId);
        if (seatId is null)
        {
            if (!string.Equals(state.Phase, MatchPhases.Lobby, StringComparison.Ordinal))
            {
                return state.FindClaimableSeat() is not null
                    ? EngineResult<MatchState>.Fail(state, "SeatClaimRequired")
                    : EngineResult<MatchState>.Fail(state, "MatchAlreadyStarted");
            }

            seatId = state.FindFirstOpenLobbySeatId();
        }

        if (seatId is null)
        {
            return EngineResult<MatchState>.Fail(state, "GameFull");
        }

        return ApplySeatClaim(state, playerId, displayName, seatId);
    }

    public EngineResult<MatchState> ClaimSeat(MatchState state, string playerId, string? displayName, string seatId)
    {
        if (!state.Seats.TryGetValue(seatId, out var seat))
        {
            return EngineResult<MatchState>.Fail(state, "UnknownSeat");
        }

        if (state.FindSeatIdByPlayerId(playerId) is not null)
        {
            return EngineResult<MatchState>.Fail(state, "SeatAlreadyClaimed");
        }

        if (seat.IsClaimed)
        {
            return EngineResult<MatchState>.Fail(state, "SeatAlreadyClaimed");
        }

        if (string.Equals(state.Phase, MatchPhases.InProgress, StringComparison.Ordinal) && !seat.IsActive)
        {
            return EngineResult<MatchState>.Fail(state, "SeatNotClaimable");
        }

        return ApplySeatClaim(state, playerId, displayName, seatId);
    }

    public EngineResult<MatchState> SetConnected(MatchState state, string playerId, bool isConnected)
    {
        var seatId = state.FindSeatIdByPlayerId(playerId);
        if (seatId is null || !state.Seats.TryGetValue(seatId, out var existing))
        {
            return EngineResult<MatchState>.Ok(state);
        }

        var updatedSeat = existing with
        {
            IsConnected = isConnected,
            IsReady = isConnected ? existing.IsReady : false,
            DisconnectedSinceUnixSeconds = isConnected ? null : existing.DisconnectedSinceUnixSeconds ?? 0
        };

        var updated = state with { Seats = state.Seats.SetItem(seatId, updatedSeat) };
        if (!isConnected && string.Equals(state.Turns.CurrentPlayerId, seatId, StringComparison.Ordinal))
        {
            updated = updated with { TurnEndsAtUnixSeconds = null };
        }

        return EngineResult<MatchState>.Ok(updated);
    }

    public EngineResult<MatchState> RemovePlayer(MatchState state, string playerId)
    {
        var seatId = state.FindSeatIdByPlayerId(playerId);
        return seatId is null
            ? EngineResult<MatchState>.Ok(state)
            : EngineResult<MatchState>.Ok(UnclaimSeatCore(
                state,
                seatId,
                keepEntities: false,
                keepSeatActive: !string.Equals(state.Phase, MatchPhases.Lobby, StringComparison.Ordinal)));
    }

    public EngineResult<MatchState> SetReady(MatchState state, string playerId, bool isReady)
    {
        if (!string.Equals(state.Phase, MatchPhases.Lobby, StringComparison.Ordinal))
        {
            return EngineResult<MatchState>.Fail(state, "MatchAlreadyStarted");
        }

        var seatId = state.FindSeatIdByPlayerId(playerId);
        if (seatId is null || !state.Seats.TryGetValue(seatId, out var seat) || !seat.IsClaimed)
        {
            return EngineResult<MatchState>.Fail(state, "UnknownPlayer");
        }

        if (!seat.IsConnected)
        {
            return EngineResult<MatchState>.Fail(state, "PlayerDisconnected");
        }

        var updated = state with { Seats = state.Seats.SetItem(seatId, seat with { IsReady = isReady }) };
        return MaybeAutoStart(updated);
    }

    public EngineResult<MatchState> StartMatch(MatchState state, string requestingPlayerId)
    {
        if (!string.Equals(state.Phase, MatchPhases.Lobby, StringComparison.Ordinal))
        {
            return EngineResult<MatchState>.Fail(state, "MatchAlreadyStarted");
        }

        var requestingSeatId = state.FindSeatIdByPlayerId(requestingPlayerId);
        if (!string.Equals(requestingSeatId, state.HostSeatId, StringComparison.Ordinal))
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

        if (state.HasUnclaimedRequiredSeats)
        {
            return EngineResult<MatchState>.Fail(state, "SeatUnclaimed");
        }

        var seatId = state.FindSeatIdByPlayerId(playerId);
        if (seatId is null || !state.Seats.TryGetValue(seatId, out var seat) || !seat.IsClaimed)
        {
            return EngineResult<MatchState>.Fail(state, "UnknownPlayer");
        }

        if (!seat.IsConnected)
        {
            return EngineResult<MatchState>.Fail(state, "PlayerDisconnected");
        }

        if (state.Turns.CurrentPlayerId is not null && !state.Turns.IsPlayersTurn(seatId))
        {
            return EngineResult<MatchState>.Fail(state, "NotYourTurn");
        }

        var validationResult = ValidateAndApply(state, seatId, action);
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

    public IReadOnlyList<AvailableActionDto> GetAvailableActions(MatchState state)
    {
        if (!string.Equals(state.Phase, MatchPhases.InProgress, StringComparison.Ordinal) || state.HasUnclaimedRequiredSeats)
        {
            return [];
        }

        var currentSeatId = state.Turns.CurrentPlayerId;
        if (currentSeatId is null)
        {
            return [];
        }

        if (!state.Seats.TryGetValue(currentSeatId, out var seat) || !seat.IsClaimed || !seat.IsConnected)
        {
            return [];
        }

        var actions = new List<AvailableActionDto>
        {
            new AvailableEndTurnActionDto()
        };

        foreach (var entity in state.Entities.Values.Where(e => string.Equals(e.OwnerPlayerId, currentSeatId, StringComparison.Ordinal)))
        {
            foreach (var candidate in EnumerateOrthogonalNeighbors(entity))
            {
                if (!IsInBounds(state, candidate.X, candidate.Y))
                {
                    continue;
                }

                if (TryGetEntityAt(state, candidate.X, candidate.Y, out var target))
                {
                    if (!string.Equals(target.OwnerPlayerId, currentSeatId, StringComparison.Ordinal))
                    {
                        actions.Add(new AvailableAttackActionDto(entity.EntityId, target.EntityId, candidate.X, candidate.Y));
                    }

                    continue;
                }

                if (IsBlocked(state, candidate.X, candidate.Y))
                {
                    continue;
                }

                actions.Add(new AvailableMoveActionDto(entity.EntityId, candidate.X, candidate.Y));
            }
        }

        return actions;
    }

    private EngineResult<MatchState> ValidateAndApply(MatchState state, string actingSeatId, PlayerActionDto action) =>
        action switch
        {
            EndTurnActionDto => EngineResult<MatchState>.Ok(AdvanceTurnForEndTurn(state)),
            MoveEntityActionDto move => ApplyMove(state, actingSeatId, move),
            AttackEntityActionDto attack => ApplyAttack(state, actingSeatId, attack),
            _ => EngineResult<MatchState>.Fail(state, "UnknownAction")
        };

    private static MatchState AdvanceTurnForEndTurn(MatchState state)
    {
        var turns = state.Turns.AdvanceTurn(id => IsSeatEligibleForTurn(state, id));
        return state with { Turns = turns, TurnEndsAtUnixSeconds = null };
    }

    private EngineResult<MatchState> ApplyMove(MatchState state, string actingSeatId, MoveEntityActionDto move)
    {
        if (string.IsNullOrWhiteSpace(move.EntityId))
        {
            return EngineResult<MatchState>.Fail(state, "EntityIdRequired");
        }

        if (!state.Entities.TryGetValue(move.EntityId, out var entity))
        {
            return EngineResult<MatchState>.Fail(state, "UnknownEntity");
        }

        if (!string.Equals(entity.OwnerPlayerId, actingSeatId, StringComparison.Ordinal))
        {
            return EngineResult<MatchState>.Fail(state, "NotEntityOwner");
        }

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

    private EngineResult<MatchState> ApplyAttack(MatchState state, string actingSeatId, AttackEntityActionDto attack)
    {
        if (string.IsNullOrWhiteSpace(attack.EntityId))
        {
            return EngineResult<MatchState>.Fail(state, "EntityIdRequired");
        }

        if (string.IsNullOrWhiteSpace(attack.TargetEntityId))
        {
            return EngineResult<MatchState>.Fail(state, "TargetEntityIdRequired");
        }

        if (!state.Entities.TryGetValue(attack.EntityId, out var attacker))
        {
            return EngineResult<MatchState>.Fail(state, "UnknownEntity");
        }

        if (!string.Equals(attacker.OwnerPlayerId, actingSeatId, StringComparison.Ordinal))
        {
            return EngineResult<MatchState>.Fail(state, "NotEntityOwner");
        }

        if (!state.Entities.TryGetValue(attack.TargetEntityId, out var target))
        {
            return EngineResult<MatchState>.Fail(state, "UnknownTargetEntity");
        }

        if (string.Equals(target.OwnerPlayerId, actingSeatId, StringComparison.Ordinal))
        {
            return EngineResult<MatchState>.Fail(state, "FriendlyTarget");
        }

        var dx = Math.Abs(target.X - attacker.X);
        var dy = Math.Abs(target.Y - attacker.Y);
        if (dx + dy != 1)
        {
            return EngineResult<MatchState>.Fail(state, "TargetOutOfRange");
        }

        var entities = state.Entities.Remove(target.EntityId);
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

    private static IEnumerable<GridPoint> EnumerateOrthogonalNeighbors(EntityStateDto entity)
    {
        yield return new GridPoint(entity.X, entity.Y - 1);
        yield return new GridPoint(entity.X - 1, entity.Y);
        yield return new GridPoint(entity.X + 1, entity.Y);
        yield return new GridPoint(entity.X, entity.Y + 1);
    }

    private static bool TryGetEntityAt(MatchState state, int x, int y, out EntityStateDto entity)
    {
        entity = state.Entities.Values.FirstOrDefault(candidate => candidate.X == x && candidate.Y == y)!;
        return entity is not null;
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
        var connectedClaimedSeats = state.Seats.Values.Where(seat => seat.IsActive && seat.IsClaimed && seat.IsConnected).ToArray();
        if (connectedClaimedSeats.Length < state.Settings.MinPlayers)
        {
            return false;
        }

        return connectedClaimedSeats.All(seat => seat.IsReady);
    }

    private MatchState StartMatchInternal(MatchState state)
    {
        var activeSeatIds = MatchState.SeatOrder
            .Where(seatId => state.Seats.TryGetValue(seatId, out var seat) && seat.IsActive && seat.IsClaimed && seat.IsConnected)
            .ToImmutableArray();
        var updated = state with
        {
            Phase = MatchPhases.InProgress,
            Turns = new TurnState(activeSeatIds, 0, false, 0)
        };
        var turns = updated.Turns.Start(id => IsSeatEligibleForTurn(updated, id));

        var entities = updated.Entities;
        var map = maps.Get(updated.Settings.MapId);
        foreach (var seatId in activeSeatIds)
        {
            if (!updated.Seats.TryGetValue(seatId, out var seat) || !seat.IsClaimed || !seat.IsConnected)
            {
                continue;
            }

            if (!map.SpawnTopLeftByOwner.TryGetValue(seatId, out var spawn))
            {
                continue;
            }

            var entityId = $"unit-{seat.ClaimedByPlayerId}";
            if (!entities.ContainsKey(entityId))
            {
                entities = entities.SetItem(entityId, new EntityStateDto(entityId, seatId, spawn.X, spawn.Y));
            }
        }

        return updated with { Turns = turns, Entities = entities, TurnEndsAtUnixSeconds = null };
    }

    private static MatchState UpdateTurnTimeout(MatchState state, long nowUnixSeconds)
    {
        if (state.HasUnclaimedRequiredSeats)
        {
            return state.TurnEndsAtUnixSeconds is null ? state : state with { TurnEndsAtUnixSeconds = null };
        }

        var currentSeatId = state.Turns.CurrentPlayerId;
        if (!state.Turns.Started || currentSeatId is null)
        {
            return state;
        }

        if (!state.Seats.TryGetValue(currentSeatId, out var currentSeat) || !currentSeat.IsClaimed || !currentSeat.IsConnected)
        {
            return state.TurnEndsAtUnixSeconds is null ? state : state with { TurnEndsAtUnixSeconds = null };
        }

        var limit = state.Settings.TurnTimeLimitSeconds;
        if (limit <= 0)
        {
            return state.TurnEndsAtUnixSeconds is null ? state : state with { TurnEndsAtUnixSeconds = null };
        }

        var endsAt = state.TurnEndsAtUnixSeconds;
        if (endsAt is null || endsAt <= 0)
        {
            return state;
        }

        if (nowUnixSeconds < endsAt.Value)
        {
            return state;
        }

        var advanced = AdvanceTurnForEndTurn(state);
        var sysActionId = $"sys:endTurn:{state.GameId}:{nowUnixSeconds}:{advanced.Turns.TurnNumber}";
        return advanced with
        {
            LastAction = new EndTurnActionDto { ActionId = sysActionId, ClientSequence = 0 },
            TurnEndsAtUnixSeconds = nowUnixSeconds + limit
        };
    }

    private static MatchState EnsureCurrentTurnIsEligible(MatchState state)
    {
        if (!string.Equals(state.Phase, MatchPhases.InProgress, StringComparison.Ordinal) || state.HasUnclaimedRequiredSeats)
        {
            return state;
        }

        var currentSeatId = state.Turns.CurrentPlayerId;
        if (currentSeatId is null)
        {
            return state;
        }

        if (state.Seats.TryGetValue(currentSeatId, out var seat) && seat.IsClaimed)
        {
            return state;
        }

        var turns = state.Turns.AdvanceTurn(id => IsSeatEligibleForTurn(state, id));
        return state with { Turns = turns, TurnEndsAtUnixSeconds = null };
    }

    private static MatchState UpdateDisconnectGrace(MatchState state, long nowUnixSeconds)
    {
        foreach (var seat in state.Seats.Values.Where(seat => seat.IsActive && seat.IsClaimed))
        {
            if (seat.IsConnected)
            {
                if (seat.DisconnectedSinceUnixSeconds is null)
                {
                    continue;
                }

                state = state with
                {
                    Seats = state.Seats.SetItem(seat.SeatId, seat with { DisconnectedSinceUnixSeconds = null })
                };
                continue;
            }

            if (seat.DisconnectedSinceUnixSeconds is null or <= 0)
            {
                state = state with
                {
                    Seats = state.Seats.SetItem(seat.SeatId, seat with { DisconnectedSinceUnixSeconds = nowUnixSeconds })
                };
                continue;
            }

            var grace = Math.Max(0, state.Settings.DisconnectGraceSeconds);
            if (grace > 0 && nowUnixSeconds - seat.DisconnectedSinceUnixSeconds.Value < grace)
            {
                continue;
            }

            return UnclaimSeatCore(state, seat.SeatId, keepEntities: true, keepSeatActive: true);
        }

        return state;
    }

    private static MatchState UnclaimSeatCore(MatchState state, string seatId, bool keepEntities, bool keepSeatActive)
    {
        if (!state.Seats.TryGetValue(seatId, out var seat))
        {
            return state;
        }

        var updatedEntities = state.Entities;
        if (!keepEntities)
        {
            foreach (var entity in updatedEntities.Values.Where(entity => string.Equals(entity.OwnerPlayerId, seatId, StringComparison.Ordinal)).ToArray())
            {
                updatedEntities = updatedEntities.Remove(entity.EntityId);
            }
        }

        var updatedSeat = seat with
        {
            ClaimedByPlayerId = null,
            DisplayName = null,
            IsConnected = false,
            IsReady = false,
            DisconnectedSinceUnixSeconds = null,
            IsActive = keepSeatActive
        };

        var updated = state with
        {
            Seats = state.Seats.SetItem(seatId, updatedSeat),
            Entities = updatedEntities
        };

        if (string.Equals(updated.HostSeatId, seatId, StringComparison.Ordinal))
        {
            updated = updated with { HostSeatId = ResolveReplacementHostSeatId(updated) };
        }

        if (!keepSeatActive)
        {
            var activeSeats = updated.ActiveSeatIds;
            updated = updated with
            {
                Turns = new TurnState(activeSeats, activeSeats.Length > 0 ? Math.Min(updated.Turns.CurrentIndex, activeSeats.Length - 1) : 0, updated.Turns.Started, updated.Turns.TurnNumber),
                TurnEndsAtUnixSeconds = null
            };
        }

        return EnsureCurrentTurnIsEligible(updated);
    }

    private static string ResolveReplacementHostSeatId(MatchState state)
    {
        foreach (var seatId in MatchState.SeatOrder.Take(state.Settings.MaxPlayers))
        {
            if (state.Seats.TryGetValue(seatId, out var seat) && seat.IsActive && seat.IsClaimed && seat.IsConnected)
            {
                return seatId;
            }
        }

        foreach (var seatId in MatchState.SeatOrder.Take(state.Settings.MaxPlayers))
        {
            if (state.Seats.TryGetValue(seatId, out var seat) && seat.IsActive && seat.IsClaimed)
            {
                return seatId;
            }
        }

        return state.HostSeatId;
    }

    private static bool IsSeatEligibleForTurn(MatchState state, string seatId) =>
        state.Seats.TryGetValue(seatId, out var seat) && seat.IsActive && seat.IsClaimed && seat.IsConnected;

    private EngineResult<MatchState> ApplySeatClaim(MatchState state, string playerId, string? displayName, string seatId)
    {
        if (!state.Seats.TryGetValue(seatId, out var existingSeat))
        {
            return EngineResult<MatchState>.Fail(state, "UnknownSeat");
        }

        var resolvedDisplayName = string.IsNullOrWhiteSpace(displayName)
            ? existingSeat.DisplayName
            : displayName.Trim();
        var updatedSeat = existingSeat with
        {
            ClaimedByPlayerId = playerId,
            DisplayName = resolvedDisplayName,
            IsConnected = true,
            IsReady = string.Equals(state.Phase, MatchPhases.Lobby, StringComparison.Ordinal) ? false : existingSeat.IsReady,
            DisconnectedSinceUnixSeconds = null,
            IsActive = true
        };

        var updated = state with { Seats = state.Seats.SetItem(seatId, updatedSeat) };

        if (string.Equals(state.Phase, MatchPhases.Lobby, StringComparison.Ordinal))
        {
            updated = updated.Turns.Order.IsDefaultOrEmpty
                ? updated with { Turns = new TurnState(updated.ActiveSeatIds, 0, false, 0) }
                : updated with { Turns = updated.Turns.EnsurePlayerInOrder(seatId) };
        }

        return MaybeAutoStart(EnsureCurrentTurnIsEligible(updated));
    }
}

