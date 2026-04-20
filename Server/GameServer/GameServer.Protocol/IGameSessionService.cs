namespace GameServer.Protocol;

public interface IGameSessionService
{
    Task<ConnectedDto> ConnectAsync(string connectionId, string? reconnectToken, string? displayName, CancellationToken cancellationToken);
    Task<ConnectedDto> GetConnectionInfoAsync(string connectionId, CancellationToken cancellationToken);
    Task<GameStateDto?> DisconnectAsync(string connectionId, CancellationToken cancellationToken);
    Task<GameStateDto> CreateMatchAsync(string connectionId, CreateMatchRequestDto request, CancellationToken cancellationToken);
    Task<GameStateDto> CreateAndJoinMatchAsync(string connectionId, CreateMatchRequestDto request, CancellationToken cancellationToken);
    Task<IReadOnlyList<MatchSummaryDto>> ListMatchesAsync(string connectionId, int limit, CancellationToken cancellationToken);
    Task<GameStateDto> JoinGameAsync(string connectionId, string gameId, CancellationToken cancellationToken);
    Task<GameStateDto> ClaimSeatAsync(string connectionId, string gameId, string seatId, CancellationToken cancellationToken);
    Task<GameStateDto> GetStateAsync(string connectionId, string gameId, CancellationToken cancellationToken);
    Task<GameStateDto?> LeaveGameAsync(string connectionId, CancellationToken cancellationToken);
    Task<GameStateDto> SetReadyAsync(string connectionId, string gameId, bool isReady, CancellationToken cancellationToken);
    Task<GameStateDto> StartMatchAsync(string connectionId, string gameId, CancellationToken cancellationToken);
    Task<SubmitActionResultDto> SubmitActionAsync(
        string connectionId,
        string gameId,
        PlayerActionDto action,
        CancellationToken cancellationToken);
}
