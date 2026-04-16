namespace GameServer.Persistence;

public interface IIdentityStore
{
    Task<PlayerIdentity> ResolveAsync(string? reconnectToken, CancellationToken cancellationToken);
    Task<PlayerIdentity?> TryGetByPlayerIdAsync(string playerId, CancellationToken cancellationToken);
}

public record PlayerIdentity(string PlayerId, string ReconnectToken);

