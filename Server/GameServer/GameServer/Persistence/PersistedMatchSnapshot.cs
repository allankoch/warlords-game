namespace GameServer.Persistence;

public record PersistedMatchSnapshot(
    int SchemaVersion,
    string GameId,
    int Version,
    long ServerActionSequence,
    DateTimeOffset SavedAt,
    string SnapshotJson);

