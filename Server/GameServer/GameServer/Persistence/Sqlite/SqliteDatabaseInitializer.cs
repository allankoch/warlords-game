using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;

namespace GameServer.Persistence.Sqlite;

public sealed class SqliteDatabaseInitializer(SqliteStorageOptions options) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(options.DbPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var connection = new SqliteConnection($"Data Source={options.DbPath}");
        await connection.OpenAsync(cancellationToken);

        var commands = new[]
        {
            """
            CREATE TABLE IF NOT EXISTS identity (
              playerId TEXT PRIMARY KEY,
              reconnectToken TEXT NOT NULL UNIQUE,
              createdAt INTEGER NOT NULL,
              lastSeenAt INTEGER NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS matches (
              gameId TEXT PRIMARY KEY,
              schemaVersion INTEGER NOT NULL,
              savedAt INTEGER NOT NULL,
              version INTEGER NOT NULL,
              serverActionSequence INTEGER NOT NULL,
              snapshotJson TEXT NOT NULL
            );
            """,
            """
            CREATE TABLE IF NOT EXISTS match_actions (
              gameId TEXT NOT NULL,
              serverActionSequence INTEGER NOT NULL,
              resultingStateVersion INTEGER NOT NULL,
              playerId TEXT NOT NULL,
              actionId TEXT NOT NULL,
              createdAt INTEGER NOT NULL,
              actionJson TEXT NOT NULL,
              PRIMARY KEY (gameId, serverActionSequence)
            );
            """
        };

        foreach (var sql in commands)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await EnsureIdentityDisplayNameColumnAsync(connection, cancellationToken);
        await EnsureIdentityActiveGameIdColumnAsync(connection, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static async Task EnsureIdentityDisplayNameColumnAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (await IdentityColumnExistsAsync(connection, "displayName", cancellationToken))
        {
            return;
        }

        await using var alter = connection.CreateCommand();
        alter.CommandText = "ALTER TABLE identity ADD COLUMN displayName TEXT NULL;";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureIdentityActiveGameIdColumnAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (await IdentityColumnExistsAsync(connection, "activeGameId", cancellationToken))
        {
            return;
        }

        await using var alter = connection.CreateCommand();
        alter.CommandText = "ALTER TABLE identity ADD COLUMN activeGameId TEXT NULL;";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<bool> IdentityColumnExistsAsync(SqliteConnection connection, string columnName, CancellationToken cancellationToken)
    {
        await using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA table_info(identity);";

        await using var reader = await pragma.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
