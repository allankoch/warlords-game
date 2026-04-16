using GameServer.Game.Engine;
using GameServer.Persistence;
using GameServer.Protocol;

namespace GameServer.Game;

public sealed class Match
{
    private readonly IGameEngine _engine;
    private MatchState _state;
    private static readonly System.Collections.Immutable.ImmutableDictionary<string, long> EmptyDisconnectedSince =
        System.Collections.Immutable.ImmutableDictionary<string, long>.Empty.WithComparers(StringComparer.Ordinal);

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

    public void AddOrReconnectPlayer(string playerId) =>
        ApplyOrThrow(_engine.AddOrReconnectPlayer(_state, playerId));

    public void SetConnected(string playerId, bool isConnected) =>
        ApplyOrThrow(_engine.SetConnected(_state, playerId, isConnected));

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
            _state.Players.Values.OrderBy(p => p.PlayerId, StringComparer.Ordinal).ToArray(),
            _state.Ready.Values.OrderBy(r => r.PlayerId, StringComparer.Ordinal).ToArray(),
            _state.SlotsByPlayerId.OrderBy(kvp => kvp.Key, StringComparer.Ordinal).Select(kvp => new PlayerSlotDto(kvp.Key, kvp.Value)).ToArray(),
            _state.Entities.Values.OrderBy(e => e.EntityId, StringComparer.Ordinal).ToArray(),
            _state.Turns.CurrentPlayerId,
            _state.Turns.TurnNumber,
            _state.TurnEndsAtUnixSeconds is long endsAt ? DateTimeOffset.FromUnixTimeSeconds(endsAt) : null,
            ServerActionSequence,
            _state.LastAction,
            DateTimeOffset.UtcNow);

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

    public PersistedMatchSnapshot CreatePersistedSnapshot(int schemaVersion = 1)
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
        if (!string.Equals(_state.Phase, MatchPhases.InProgress, StringComparison.Ordinal)) return;
        if (!_state.Turns.Started || _state.Turns.CurrentPlayerId is null) return;

        if (_state.TurnEndsAtUnixSeconds is not null and > 0) return;

        var limit = Math.Max(1, _state.Settings.TurnTimeLimitSeconds);
        _state = _state with { TurnEndsAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + limit };
    }

    private static MatchState Core(MatchState state) =>
        state with { TurnEndsAtUnixSeconds = null, DisconnectedSinceUnixSeconds = EmptyDisconnectedSince };
}

public readonly record struct MatchTickResult(bool Changed, bool IsSystemEndTurn)
{
    public static MatchTickResult NoChange => new(false, false);
}
