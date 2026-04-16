namespace GameServer.Persistence;

public interface IMatchActionLog
{
    Task AppendAcceptedAsync(MatchActionRecord record, CancellationToken cancellationToken);
    Task<IReadOnlyList<MatchActionRecord>> GetAcceptedAsync(string gameId, long afterServerSequence, CancellationToken cancellationToken);
}

public record MatchActionRecord(
    string GameId,
    long ServerActionSequence,
    int ResultingStateVersion,
    string PlayerId,
    string ActionId,
    DateTimeOffset CreatedAt,
    string ActionJson);

