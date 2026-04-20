using System.Collections.Immutable;
using GameServer.Game.Engine;
using GameServer.Persistence;
using GameServer.Protocol;

namespace GameServer.Game;

public sealed class Match
{
    private readonly IGameEngine _engine;
    private MatchState _state;

    public Match(string gameId, MatchSettings settings, string hostPlayerId, IGameEngine engine)
    {
        _engine = engine;
        _state = MatchState.CreateNew(gameId, settings, hostPlayerId);
    }

    private Match(MatchState state, int version, long serverActionSequence, IGameEngine engine)
    {
        _engine = engine;
        _state = state;
        Version = version;
        ServerActionSequence = serverActionSequence;
    }

    public string GameId => _state.GameId;
    public int Version { get; private set; }
    public long ServerActionSequence { get; private set; }

    public static Match FromPersisted(PersistedMatchSnapshot snapshot, IGameEngine engine)
    {
        var dto = MatchSnapshotMapper.Deserialize(snapshot.SnapshotJson);
        var state = MatchSnapshotMapper.ToState(dto, markAllDisconnected: true);
        return new Match(state, snapshot.Version, snapshot.ServerActionSequence, engine);
    }

    public void AddOrReconnectPlayer(string playerId, string? displayName) =>
        ApplyOrThrow(_engine.AddOrReconnectPlayer(_state, playerId, displayName));

    public void ClaimSeat(string playerId, string? displayName, string seatId) =>
        ApplyOrThrow(_engine.ClaimSeat(_state, playerId, displayName, seatId));

    public void SetConnected(string playerId, bool isConnected)
    {
        ApplyOrThrow(_engine.SetConnected(_state, playerId, isConnected));
        if (isConnected)
        {
            EnsureTurnDeadlineInitialized();
        }
    }

    public void RemovePlayer(string playerId) =>
        ApplyOrThrow(_engine.RemovePlayer(_state, playerId));

    public void SetReady(string playerId, bool isReady) =>
        ApplyOrThrow(_engine.SetReady(_state, playerId, isReady));

    public void StartMatch(string requestingPlayerId)
    {
        ApplyOrThrow(_engine.StartMatch(_state, requestingPlayerId));
        EnsureTurnDeadlineInitialized();
    }

    public SubmitActionResultDto ApplyAction(string playerId, PlayerActionDto action)
    {
        var result = _engine.ApplyAction(_state, playerId, action);
        if (!result.Success)
        {
            return new SubmitActionResultDto(false, action.ActionId, result.Error, Version, ServerActionSequence, null);
        }

        _state = result.State;
        ServerActionSequence++;
        Version++;

        if (action is EndTurnActionDto)
        {
            EnsureTurnDeadlineInitialized();
        }

        var snapshot = Snapshot();
        return new SubmitActionResultDto(true, action.ActionId, null, snapshot.Version, snapshot.ServerActionSequence, snapshot);
    }

    public GameStateDto Snapshot() =>
        new(
            _state.GameId,
            Version,
            _state.Settings.MapId,
            _state.Phase,
            _state.Settings.MinPlayers,
            _state.Settings.MaxPlayers,
            _state.HostPlayerId,
            _state.Players.Values.OrderBy(p => p.PlayerId, StringComparer.Ordinal).ToArray(),
            _state.Ready.Values.OrderBy(r => r.PlayerId, StringComparer.Ordinal).ToArray(),
            _state.SlotsByPlayerId.OrderBy(kvp => kvp.Key, StringComparer.Ordinal).Select(kvp => new PlayerSlotDto(kvp.Key, kvp.Value)).ToArray(),
            _state.Entities.Values
                .OrderBy(e => e.EntityId, StringComparer.Ordinal)
                .Select(entity => entity with { OwnerPlayerId = _state.ResolveExternalPlayerId(entity.OwnerPlayerId) })
                .ToArray(),
            _engine.GetAvailableActions(_state)
                .OrderBy(ActionSortKey, StringComparer.Ordinal)
                .ThenBy(ActionSortEntityId, StringComparer.Ordinal)
                .ThenBy(ActionSortY)
                .ThenBy(ActionSortX)
                .ToArray(),
            _state.CurrentTurnPlayerId,
            _state.Turns.TurnNumber,
            _state.TurnEndsAtUnixSeconds is long endsAt ? DateTimeOffset.FromUnixTimeSeconds(endsAt) : null,
            ServerActionSequence,
            _state.LastAction,
            DateTimeOffset.UtcNow,
            _state.Seats.Values
                .OrderBy(seat => seat.SeatId, StringComparer.Ordinal)
                .Select(seat => new SeatStatusDto(
                    seat.SeatId,
                    seat.IsActive,
                    seat.IsClaimed,
                    seat.ClaimedByPlayerId,
                    seat.DisplayName,
                    seat.IsConnected,
                    seat.IsReady,
                    _state.Entities.Values.Any(entity => string.Equals(entity.OwnerPlayerId, seat.SeatId, StringComparison.Ordinal))))
                .ToArray(),
            _state.HasUnclaimedRequiredSeats);

    public MatchTickResult Tick(long nowUnixSeconds)
    {
        var before = _state;
        var result = _engine.Tick(_state, nowUnixSeconds);
        _state = result.State;

        if (ReferenceEquals(before, _state))
        {
            return MatchTickResult.NoChange;
        }

        var coreChanged = !Equals(Core(before), Core(_state));
        if (!coreChanged)
        {
            return MatchTickResult.NoChange;
        }

        EnsureTurnDeadlineInitialized();

        var isSystemEndTurn = _state.LastAction is EndTurnActionDto endTurn &&
                              endTurn.ActionId.StartsWith("sys:endTurn:", StringComparison.Ordinal);

        if (isSystemEndTurn)
        {
            ServerActionSequence++;
        }

        Version++;
        return new MatchTickResult(Changed: true, IsSystemEndTurn: isSystemEndTurn);
    }

    public PersistedMatchSnapshot CreatePersistedSnapshot(int schemaVersion = 2)
    {
        var dto = MatchSnapshotMapper.FromState(_state, schemaVersion);
        var json = MatchSnapshotMapper.Serialize(dto);
        return new PersistedMatchSnapshot(schemaVersion, _state.GameId, Version, ServerActionSequence, DateTimeOffset.UtcNow, json);
    }

    private void ApplyOrThrow(EngineResult<MatchState> result)
    {
        if (!result.Success)
        {
            throw new InvalidOperationException(result.Error);
        }

        if (ReferenceEquals(_state, result.State))
        {
            return;
        }

        _state = result.State;
        Version++;
    }

    private void EnsureTurnDeadlineInitialized()
    {
        if (!string.Equals(_state.Phase, MatchPhases.InProgress, StringComparison.Ordinal) || _state.HasUnclaimedRequiredSeats) 
        {
            _state = _state with { TurnEndsAtUnixSeconds = null };
            return;
        }

        var currentSeatId = _state.Turns.CurrentPlayerId;
        if (!_state.Turns.Started || currentSeatId is null) return;
        if (!_state.Seats.TryGetValue(currentSeatId, out var current) || !current.IsClaimed || !current.IsConnected)
        {
            _state = _state with { TurnEndsAtUnixSeconds = null };
            return;
        }

        var limit = _state.Settings.TurnTimeLimitSeconds;
        if (limit <= 0)
        {
            _state = _state with { TurnEndsAtUnixSeconds = null };
            return;
        }

        if (_state.TurnEndsAtUnixSeconds is not null and > 0) return;

        _state = _state with { TurnEndsAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + limit };
    }

    private static MatchState Core(MatchState state) =>
        state with
        {
            TurnEndsAtUnixSeconds = null,
            Seats = state.Seats.ToImmutableDictionary(
                seat => seat.Key,
                seat => seat.Value with { DisconnectedSinceUnixSeconds = null },
                StringComparer.Ordinal)
        };

    private static string ActionSortKey(AvailableActionDto action) =>
        action switch
        {
            AvailableEndTurnActionDto => "endTurn",
            AvailableAttackActionDto => "attack",
            AvailableMoveActionDto => "move",
            _ => "unknown"
        };

    private static string ActionSortEntityId(AvailableActionDto action) =>
        action switch
        {
            AvailableMoveActionDto move => move.EntityId,
            AvailableAttackActionDto attack => attack.EntityId,
            _ => string.Empty
        };

    private static int ActionSortX(AvailableActionDto action) =>
        action switch
        {
            AvailableMoveActionDto move => move.X,
            AvailableAttackActionDto attack => attack.X,
            _ => int.MinValue
        };

    private static int ActionSortY(AvailableActionDto action) =>
        action switch
        {
            AvailableMoveActionDto move => move.Y,
            AvailableAttackActionDto attack => attack.Y,
            _ => int.MinValue
        };
}

public readonly record struct MatchTickResult(bool Changed, bool IsSystemEndTurn)
{
    public static MatchTickResult NoChange => new(false, false);
}
