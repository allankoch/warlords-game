using Microsoft.Data.Sqlite;
using GameServer.Persistence;

namespace GameServer.Persistence.Sqlite;

public sealed class SqliteGameRepository(SqliteStorageOptions options) : IIdentityStore, IMatchStore, IMatchActionLog, IGamePersistence
{
    public async Task<PlayerIdentity> ResolveAsync(string? reconnectToken, string? displayName, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var normalizedDisplayName = NormalizeDisplayName(displayName);
        if (!string.IsNullOrWhiteSpace(reconnectToken))
        {
            var existing = await TryGetByTokenAsync(connection, reconnectToken, cancellationToken);
            if (existing is not null)
            {
                var resolvedDisplayName = normalizedDisplayName ?? existing.DisplayName;
                await TouchAsync(connection, existing.PlayerId, resolvedDisplayName, cancellationToken);
                return existing with { DisplayName = resolvedDisplayName };
            }
        }

        var playerId = $"player-{Guid.NewGuid():N}";
        var token = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            INSERT INTO identity (playerId, reconnectToken, displayName, activeGameId, createdAt, lastSeenAt)
            VALUES ($playerId, $token, $displayName, NULL, $now, $now);
            """;
        cmd.Parameters.AddWithValue("$playerId", playerId);
        cmd.Parameters.AddWithValue("$token", token);
        cmd.Parameters.AddWithValue("$displayName", (object?)normalizedDisplayName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$now", now);
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        return new PlayerIdentity(playerId, token, normalizedDisplayName, null);
    }

    public async Task<PlayerIdentity?> TryGetByPlayerIdAsync(string playerId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT playerId, reconnectToken, displayName, activeGameId FROM identity WHERE playerId = $playerId;";
        cmd.Parameters.AddWithValue("$playerId", playerId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new PlayerIdentity(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    public async Task SetActiveGameAsync(string playerId, string? activeGameId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE identity SET activeGameId = $activeGameId WHERE playerId = $playerId;";
        cmd.Parameters.AddWithValue("$playerId", playerId);
        cmd.Parameters.AddWithValue("$activeGameId", (object?)activeGameId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveSnapshotAsync(PersistedMatchSnapshot snapshot, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await UpsertMatchSnapshotAsync(connection, transaction, snapshot, cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<PersistedMatchSnapshot?> LoadSnapshotAsync(string gameId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT schemaVersion, gameId, version, serverActionSequence, savedAt, snapshotJson
            FROM matches
            WHERE gameId = $gameId;
            """;
        cmd.Parameters.AddWithValue("$gameId", gameId);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new PersistedMatchSnapshot(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.GetInt64(3),
            DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(4)),
            reader.GetString(5));
    }

    public async Task<IReadOnlyList<PersistedMatchSnapshot>> ListSnapshotsAsync(int limit, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT schemaVersion, gameId, version, serverActionSequence, savedAt, snapshotJson
            FROM matches
            ORDER BY savedAt DESC
            LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$limit", limit <= 0 ? 50 : limit);

        var list = new List<PersistedMatchSnapshot>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new PersistedMatchSnapshot(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetInt64(3),
                DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(4)),
                reader.GetString(5)));
        }

        return list;
    }

    public async Task AppendAcceptedAsync(MatchActionRecord record, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText =
            """
            INSERT INTO match_actions (
              gameId, serverActionSequence, resultingStateVersion, playerId, actionId, createdAt, actionJson
            ) VALUES (
              $gameId, $seq, $ver, $playerId, $actionId, $createdAt, $actionJson
            );
            """;
        cmd.Parameters.AddWithValue("$gameId", record.GameId);
        cmd.Parameters.AddWithValue("$seq", record.ServerActionSequence);
        cmd.Parameters.AddWithValue("$ver", record.ResultingStateVersion);
        cmd.Parameters.AddWithValue("$playerId", record.PlayerId);
        cmd.Parameters.AddWithValue("$actionId", record.ActionId);
        cmd.Parameters.AddWithValue("$createdAt", record.CreatedAt.ToUnixTimeSeconds());
        cmd.Parameters.AddWithValue("$actionJson", record.ActionJson);
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task PersistAcceptedActionAsync(PersistedMatchSnapshot snapshot, MatchActionRecord record, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await UpsertMatchSnapshotAsync(connection, transaction, snapshot, cancellationToken);

        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText =
            """
            INSERT INTO match_actions (
              gameId, serverActionSequence, resultingStateVersion, playerId, actionId, createdAt, actionJson
            ) VALUES (
              $gameId, $seq, $ver, $playerId, $actionId, $createdAt, $actionJson
            );
            """;
        cmd.Parameters.AddWithValue("$gameId", record.GameId);
        cmd.Parameters.AddWithValue("$seq", record.ServerActionSequence);
        cmd.Parameters.AddWithValue("$ver", record.ResultingStateVersion);
        cmd.Parameters.AddWithValue("$playerId", record.PlayerId);
        cmd.Parameters.AddWithValue("$actionId", record.ActionId);
        cmd.Parameters.AddWithValue("$createdAt", record.CreatedAt.ToUnixTimeSeconds());
        cmd.Parameters.AddWithValue("$actionJson", record.ActionJson);
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MatchActionRecord>> GetAcceptedAsync(string gameId, long afterServerSequence, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT gameId, serverActionSequence, resultingStateVersion, playerId, actionId, createdAt, actionJson
            FROM match_actions
            WHERE gameId = $gameId AND serverActionSequence > $after
            ORDER BY serverActionSequence ASC;
            """;
        cmd.Parameters.AddWithValue("$gameId", gameId);
        cmd.Parameters.AddWithValue("$after", afterServerSequence);

        var list = new List<MatchActionRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            list.Add(new MatchActionRecord(
                reader.GetString(0),
                reader.GetInt64(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetString(4),
                DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(5)),
                reader.GetString(6)));
        }

        return list;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection($"Data Source={options.DbPath}");
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static async Task<PlayerIdentity?> TryGetByTokenAsync(SqliteConnection connection, string token, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT playerId, reconnectToken, displayName, activeGameId FROM identity WHERE reconnectToken = $token;";
        cmd.Parameters.AddWithValue("$token", token);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new PlayerIdentity(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    private static async Task TouchAsync(SqliteConnection connection, string playerId, string? displayName, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE identity SET lastSeenAt = $now, displayName = COALESCE($displayName, displayName) WHERE playerId = $playerId;";
        cmd.Parameters.AddWithValue("$playerId", playerId);
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        cmd.Parameters.AddWithValue("$displayName", (object?)displayName ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string? NormalizeDisplayName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        var trimmed = displayName.Trim();
        return trimmed.Length <= 32 ? trimmed : trimmed[..32];
    }

    private static async Task UpsertMatchSnapshotAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        PersistedMatchSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText =
            """
            INSERT INTO matches (gameId, schemaVersion, savedAt, version, serverActionSequence, snapshotJson)
            VALUES ($gameId, $schemaVersion, $savedAt, $version, $seq, $json)
            ON CONFLICT(gameId) DO UPDATE SET
              schemaVersion = excluded.schemaVersion,
              savedAt = excluded.savedAt,
              version = excluded.version,
              serverActionSequence = excluded.serverActionSequence,
              snapshotJson = excluded.snapshotJson;
            """;
        cmd.Parameters.AddWithValue("$gameId", snapshot.GameId);
        cmd.Parameters.AddWithValue("$schemaVersion", snapshot.SchemaVersion);
        cmd.Parameters.AddWithValue("$savedAt", snapshot.SavedAt.ToUnixTimeSeconds());
        cmd.Parameters.AddWithValue("$version", snapshot.Version);
        cmd.Parameters.AddWithValue("$seq", snapshot.ServerActionSequence);
        cmd.Parameters.AddWithValue("$json", snapshot.SnapshotJson);

        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
