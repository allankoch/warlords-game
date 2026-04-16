namespace GameServer.Persistence;

public interface IMatchStore
{
    Task SaveSnapshotAsync(PersistedMatchSnapshot snapshot, CancellationToken cancellationToken);
    Task<PersistedMatchSnapshot?> LoadSnapshotAsync(string gameId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PersistedMatchSnapshot>> ListSnapshotsAsync(int limit, CancellationToken cancellationToken);
}
