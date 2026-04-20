namespace GameServer.Persistence;

public interface IIdentityStore
{
    Task<PlayerIdentity> ResolveAsync(string? reconnectToken, string? displayName, CancellationToken cancellationToken);
    Task<PlayerIdentity?> TryGetByPlayerIdAsync(string playerId, CancellationToken cancellationToken);
    Task SetActiveGameAsync(string playerId, string? activeGameId, CancellationToken cancellationToken);
}

public record PlayerIdentity(string PlayerId, string ReconnectToken, string? DisplayName, string? ActiveGameId = null);
