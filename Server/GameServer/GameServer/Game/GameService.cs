using System.Collections.Concurrent;
using GameServer.Game.Engine;
using GameServer.Persistence;
using GameServer.Protocol;

namespace GameServer.Game;

public sealed class GameService(
    IGameEngine gameEngine,
    IMapProvider mapProvider,
    IIdentityStore identityStore,
    IMatchStore matchStore,
    IGamePersistence persistence) : IGameSessionService
{
    private readonly ConcurrentDictionary<string, ConnectionInfo> _connections = new();
    private readonly ConcurrentDictionary<string, GameSession> _sessions = new();

    public Task<ConnectedDto> ConnectAsync(string connectionId, string? reconnectToken, CancellationToken cancellationToken) =>
        ConnectInternalAsync(connectionId, reconnectToken, cancellationToken);

    public async Task<GameStateDto?> DisconnectAsync(string connectionId, CancellationToken cancellationToken)
    {
        if (!_connections.TryRemove(connectionId, out var connection))
        {
            return null;
        }

        if (connection.GameId is not null && _sessions.TryGetValue(connection.GameId, out var session))
        {
            PersistedMatchSnapshot? persisted = null;
            GameStateDto? state = null;
            lock (session.Sync)
            {
                session.Match.SetConnected(connection.PlayerId, false);
                state = session.Match.Snapshot();
                persisted = session.Match.CreatePersistedSnapshot();
            }

            if (persisted is not null)
            {
                await matchStore.SaveSnapshotAsync(persisted, cancellationToken);
            }

            return state;
        }

        return null;
    }

    public async Task<GameStateDto> CreateMatchAsync(string connectionId, CreateMatchRequestDto request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.GameId))
        {
            throw new InvalidOperationException("GameIdRequired");
        }

        if (string.IsNullOrWhiteSpace(request.MapId))
        {
            throw new InvalidOperationException("MapIdRequired");
        }

        if (!_connections.TryGetValue(connectionId, out var connection))
        {
            throw new InvalidOperationException("UnknownPlayer");
        }

        if (request.MinPlayers <= 0 || request.MaxPlayers <= 0 || request.MinPlayers > request.MaxPlayers)
        {
            throw new InvalidOperationException("InvalidPlayerLimits");
        }

        if (request.MaxPlayers > 8)
        {
            throw new InvalidOperationException("MaxPlayersExceeded");
        }

        var map = mapProvider.Get(request.MapId);
        var requiredOwners = new[] { "white", "red", "green", "black", "orange", "lightblue", "darkblue", "yellow" };
        for (var i = 0; i < request.MaxPlayers; i++)
        {
            if (!map.SpawnTopLeftByOwner.ContainsKey(requiredOwners[i]))
            {
                throw new InvalidOperationException("MissingSpawn");
            }
        }

        if (_sessions.ContainsKey(request.GameId))
        {
            throw new InvalidOperationException("GameAlreadyExists");
        }

        var settings = new MatchSettings(
            request.MapId,
            request.MinPlayers,
            request.MaxPlayers,
            request.AutoStart,
            request.TurnTimeLimitSeconds,
            request.DisconnectGraceSeconds);
        var session = new GameSession(new Match(request.GameId, settings, connection.PlayerId, gameEngine));
        if (!_sessions.TryAdd(request.GameId, session))
        {
            throw new InvalidOperationException("GameAlreadyExists");
        }

        PersistedMatchSnapshot persisted;
        GameStateDto snapshot;
        lock (session.Sync)
        {
            snapshot = session.Match.Snapshot();
            persisted = session.Match.CreatePersistedSnapshot();
        }

        await matchStore.SaveSnapshotAsync(persisted, cancellationToken);
        return snapshot;
    }

    public async Task<IReadOnlyList<MatchSummaryDto>> ListMatchesAsync(string connectionId, int limit, CancellationToken cancellationToken)
    {
        if (!_connections.ContainsKey(connectionId))
        {
            throw new InvalidOperationException("UnknownPlayer");
        }

        var snapshots = await matchStore.ListSnapshotsAsync(limit, cancellationToken);
        var list = new List<MatchSummaryDto>(snapshots.Count);
        foreach (var snapshot in snapshots)
        {
            var dto = MatchSnapshotMapper.Deserialize(snapshot.SnapshotJson);
            list.Add(new MatchSummaryDto(
                dto.GameId,
                dto.Settings.MapId,
                dto.Phase,
                snapshot.Version,
                snapshot.ServerActionSequence,
                dto.Players.Count,
                snapshot.SavedAt));
        }

        return list;
    }

    public async Task<GameStateDto> JoinGameAsync(string connectionId, string gameId, CancellationToken cancellationToken)
    {
        if (!_connections.TryGetValue(connectionId, out var connection))
        {
            throw new InvalidOperationException("Unknown connection.");
        }

        if (!_sessions.TryGetValue(gameId, out var session))
        {
            var loaded = await matchStore.LoadSnapshotAsync(gameId, cancellationToken);
            if (loaded is null)
            {
                throw new InvalidOperationException("UnknownGame");
            }

            var loadedSession = new GameSession(Match.FromPersisted(loaded, gameEngine));
            session = _sessions.GetOrAdd(gameId, _ => loadedSession);
        }

        connection.GameId = gameId;

        PersistedMatchSnapshot persisted;
        GameStateDto state;
        lock (session.Sync)
        {
            var snapshot = session.Match.Snapshot();
            if (string.Equals(snapshot.Phase, GameServer.Game.Engine.MatchPhases.InProgress, StringComparison.Ordinal) &&
                !snapshot.Players.Any(p => string.Equals(p.PlayerId, connection.PlayerId, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("MatchAlreadyStarted");
            }

            session.Match.AddOrReconnectPlayer(connection.PlayerId);
            state = session.Match.Snapshot();
            persisted = session.Match.CreatePersistedSnapshot();
        }

        await matchStore.SaveSnapshotAsync(persisted, cancellationToken);
        return state;
    }

    public Task<GameStateDto> GetStateAsync(string connectionId, string gameId, CancellationToken cancellationToken)
    {
        if (!_connections.TryGetValue(connectionId, out var connection))
        {
            throw new InvalidOperationException("UnknownPlayer");
        }

        if (!string.Equals(connection.GameId, gameId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("NotInGame");
        }

        if (!_sessions.TryGetValue(gameId, out var session))
        {
            throw new InvalidOperationException("UnknownGame");
        }

        lock (session.Sync)
        {
            return Task.FromResult(session.Match.Snapshot());
        }
    }

    public async Task<GameStateDto?> LeaveGameAsync(string connectionId, CancellationToken cancellationToken)
    {
        if (!_connections.TryGetValue(connectionId, out var connection))
        {
            return null;
        }

        if (connection.GameId is null)
        {
            return null;
        }

        GameStateDto? state = null;
        PersistedMatchSnapshot? persisted = null;
        if (_sessions.TryGetValue(connection.GameId, out var session))
        {
            lock (session.Sync)
            {
                session.Match.RemovePlayer(connection.PlayerId);
                state = session.Match.Snapshot();
                persisted = session.Match.CreatePersistedSnapshot();
            }
        }

        connection.GameId = null;

        if (persisted is not null)
        {
            await matchStore.SaveSnapshotAsync(persisted, cancellationToken);
        }

        return state;
    }

    public async Task<GameStateDto> SetReadyAsync(string connectionId, string gameId, bool isReady, CancellationToken cancellationToken)
    {
        if (!_connections.TryGetValue(connectionId, out var connection))
        {
            throw new InvalidOperationException("UnknownPlayer");
        }

        if (!string.Equals(connection.GameId, gameId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("NotInGame");
        }

        if (!_sessions.TryGetValue(gameId, out var session))
        {
            throw new InvalidOperationException("UnknownGame");
        }

        PersistedMatchSnapshot persisted;
        GameStateDto state;
        lock (session.Sync)
        {
            session.Match.SetReady(connection.PlayerId, isReady);
            state = session.Match.Snapshot();
            persisted = session.Match.CreatePersistedSnapshot();
        }

        await matchStore.SaveSnapshotAsync(persisted, cancellationToken);
        return state;
    }

    public async Task<GameStateDto> StartMatchAsync(string connectionId, string gameId, CancellationToken cancellationToken)
    {
        if (!_connections.TryGetValue(connectionId, out var connection))
        {
            throw new InvalidOperationException("UnknownPlayer");
        }

        if (!string.Equals(connection.GameId, gameId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("NotInGame");
        }

        if (!_sessions.TryGetValue(gameId, out var session))
        {
            throw new InvalidOperationException("UnknownGame");
        }

        PersistedMatchSnapshot persisted;
        GameStateDto state;
        lock (session.Sync)
        {
            session.Match.StartMatch(connection.PlayerId);
            state = session.Match.Snapshot();
            persisted = session.Match.CreatePersistedSnapshot();
        }

        await matchStore.SaveSnapshotAsync(persisted, cancellationToken);
        return state;
    }

    public async Task<SubmitActionResultDto> SubmitActionAsync(
        string connectionId,
        string gameId,
        PlayerActionDto action,
        CancellationToken cancellationToken)
    {
        if (!_connections.TryGetValue(connectionId, out var connection))
        {
            return new SubmitActionResultDto(false, action.ActionId, "UnknownPlayer", 0, 0, null);
        }

        if (!string.Equals(connection.GameId, gameId, StringComparison.Ordinal))
        {
            return new SubmitActionResultDto(false, action.ActionId, "NotInGame", 0, 0, null);
        }

        if (!_sessions.TryGetValue(gameId, out var session))
        {
            return new SubmitActionResultDto(false, action.ActionId, "UnknownGame", 0, 0, null);
        }

        if (string.IsNullOrWhiteSpace(action.ActionId))
        {
            lock (session.Sync)
            {
                return new SubmitActionResultDto(false, "", "ActionIdRequired", session.Match.Version, session.Match.ServerActionSequence, null);
            }
        }

        PersistedMatchSnapshot? persisted = null;
        MatchActionRecord? record = null;
        SubmitActionResultDto result;
        lock (session.Sync)
        {
            if (action.ExpectedStateVersion is int expected && expected != session.Match.Version)
            {
                return new SubmitActionResultDto(false, action.ActionId, "StaleState", session.Match.Version, session.Match.ServerActionSequence, null);
            }

            if (session.LastClientSequenceByPlayer.TryGetValue(connection.PlayerId, out var lastSeq) &&
                action.ClientSequence <= lastSeq)
            {
                return new SubmitActionResultDto(false, action.ActionId, "OutOfOrder", session.Match.Version, session.Match.ServerActionSequence, null);
            }

            if (session.LastActionIdByPlayer.TryGetValue(connection.PlayerId, out var lastActionId) &&
                string.Equals(lastActionId, action.ActionId, StringComparison.Ordinal))
            {
                var state = session.Match.Snapshot();
                return new SubmitActionResultDto(true, action.ActionId, null, state.Version, state.ServerActionSequence, state);
            }

            result = session.Match.ApplyAction(connection.PlayerId, action);
            if (!result.Accepted)
            {
                return result;
            }

            session.LastActionIdByPlayer[connection.PlayerId] = action.ActionId;
            session.LastClientSequenceByPlayer[connection.PlayerId] = action.ClientSequence;

            persisted = session.Match.CreatePersistedSnapshot();
            record = new MatchActionRecord(
                persisted.GameId,
                persisted.ServerActionSequence,
                persisted.Version,
                connection.PlayerId,
                action.ActionId,
                DateTimeOffset.UtcNow,
                System.Text.Json.JsonSerializer.Serialize(action, MatchSnapshotMapper.JsonOptions));
        }

        if (persisted is not null && record is not null)
        {
            await persistence.PersistAcceptedActionAsync(persisted, record, cancellationToken);
        }

        return result;
    }

    private sealed class ConnectionInfo(string playerId)
    {
        public string PlayerId { get; } = playerId;
        public string? GameId { get; set; }
    }

    private sealed class GameSession(Match match)
    {
        public object Sync { get; } = new();
        public Match Match { get; } = match;
        public ConcurrentDictionary<string, string> LastActionIdByPlayer { get; } = new();
        public ConcurrentDictionary<string, int> LastClientSequenceByPlayer { get; } = new();
    }

    private async Task<ConnectedDto> ConnectInternalAsync(string connectionId, string? reconnectToken, CancellationToken cancellationToken)
    {
        var identity = await identityStore.ResolveAsync(reconnectToken, cancellationToken);
        _connections[connectionId] = new ConnectionInfo(identity.PlayerId);
        return new ConnectedDto(identity.PlayerId, identity.ReconnectToken);
    }

    public async Task<IReadOnlyList<(string GameId, GameStateDto State, bool IsSystemEndTurn)>> TickAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var updates = new List<(string GameId, GameStateDto State, bool IsSystemEndTurn)>();
        var persists = new List<(PersistedMatchSnapshot Snapshot, MatchActionRecord? ActionRecord)>();

        foreach (var kvp in _sessions)
        {
            var gameId = kvp.Key;
            var session = kvp.Value;

            PersistedMatchSnapshot? persisted = null;
            MatchActionRecord? record = null;
            GameStateDto? state = null;
            var isSystemEndTurn = false;

            lock (session.Sync)
            {
                var tick = session.Match.Tick(now);
                if (!tick.Changed)
                {
                    continue;
                }

                state = session.Match.Snapshot();
                persisted = session.Match.CreatePersistedSnapshot();
                isSystemEndTurn = tick.IsSystemEndTurn;

                if (isSystemEndTurn && state.LastAction is EndTurnActionDto endTurn)
                {
                    record = new MatchActionRecord(
                        persisted.GameId,
                        persisted.ServerActionSequence,
                        persisted.Version,
                        "system",
                        endTurn.ActionId,
                        DateTimeOffset.UtcNow,
                        System.Text.Json.JsonSerializer.Serialize(endTurn, MatchSnapshotMapper.JsonOptions));
                }
            }

            if (persisted is not null)
            {
                persists.Add((persisted, record));
            }

            if (state is not null)
            {
                updates.Add((gameId, state, isSystemEndTurn));
            }
        }

        foreach (var item in persists)
        {
            if (item.ActionRecord is not null)
            {
                await persistence.PersistAcceptedActionAsync(item.Snapshot, item.ActionRecord, cancellationToken);
            }
            else
            {
                await matchStore.SaveSnapshotAsync(item.Snapshot, cancellationToken);
            }
        }

        return updates;
    }


}
