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
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

