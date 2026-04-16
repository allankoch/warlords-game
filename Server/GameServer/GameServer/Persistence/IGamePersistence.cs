namespace GameServer.Persistence;

public interface IGamePersistence
{
    Task PersistAcceptedActionAsync(PersistedMatchSnapshot snapshot, MatchActionRecord record, CancellationToken cancellationToken);
}

