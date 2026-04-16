using Microsoft.AspNetCore.SignalR;
using GameServer.Protocol;

namespace GameServer.Networking;

public class GameHub(IGameSessionService gameSession) : Hub<IGameClient>
{
    private const string PlayerIdKey = "playerId";
    private const string GameIdKey = "gameId";
    private const string ReconnectTokenQuery = "reconnectToken";

    public override async Task OnConnectedAsync()
    {
        var reconnectToken = Context.GetHttpContext()?.Request.Query[ReconnectTokenQuery].ToString();
        if (string.IsNullOrWhiteSpace(reconnectToken))
        {
            reconnectToken = null;
        }

        var connected = await gameSession.ConnectAsync(Context.ConnectionId, reconnectToken, Context.ConnectionAborted);
        Context.Items[PlayerIdKey] = connected.PlayerId;

        await Clients.Caller.Connected(connected);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var gameId = GetCurrentGameId();
        var state = await gameSession.DisconnectAsync(Context.ConnectionId, Context.ConnectionAborted);

        if (gameId is not null && state is not null)
        {
            await Clients.Group(GameGroup(gameId)).GameState(state);
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinGame(JoinGameRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.GameId))
        {
            throw new HubException("GameIdRequired");
        }

        var connectionId = Context.ConnectionId;
        var playerId = GetPlayerId() ?? throw new HubException("NotConnected");
        var previousGameId = GetCurrentGameId();

        if (previousGameId is not null && !string.Equals(previousGameId, request.GameId, StringComparison.Ordinal))
        {
            await Groups.RemoveFromGroupAsync(connectionId, GameGroup(previousGameId), Context.ConnectionAborted);
        }

        await Groups.AddToGroupAsync(connectionId, GameGroup(request.GameId), Context.ConnectionAborted);
        Context.Items[GameIdKey] = request.GameId;

        var state = await gameSession.JoinGameAsync(connectionId, request.GameId, Context.ConnectionAborted);
        await Clients.Caller.GameState(state);
        await Clients.OthersInGroup(GameGroup(request.GameId)).GameState(state);
        await Clients.OthersInGroup(GameGroup(request.GameId)).PlayerJoined(new PlayerJoinedDto(request.GameId, playerId));
    }

    public async Task<IReadOnlyList<MatchSummaryDto>> ListMatches(int limit = 50)
    {
        var results = await gameSession.ListMatchesAsync(Context.ConnectionId, limit, Context.ConnectionAborted);
        return results;
    }

    public async Task JoinByCode(JoinByCodeRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            throw new HubException("CodeRequired");
        }

        await JoinGame(new JoinGameRequestDto(request.Code));
    }

    public async Task CreateMatch(CreateMatchRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.GameId))
        {
            throw new HubException("GameIdRequired");
        }

        var connectionId = Context.ConnectionId;
        var previousGameId = GetCurrentGameId();
        if (previousGameId is not null && !string.Equals(previousGameId, request.GameId, StringComparison.Ordinal))
        {
            await Groups.RemoveFromGroupAsync(connectionId, GameGroup(previousGameId), Context.ConnectionAborted);
        }

        await Groups.AddToGroupAsync(connectionId, GameGroup(request.GameId), Context.ConnectionAborted);
        Context.Items[GameIdKey] = request.GameId;

        await gameSession.CreateMatchAsync(connectionId, request, Context.ConnectionAborted);

        var state = await gameSession.JoinGameAsync(connectionId, request.GameId, Context.ConnectionAborted);
        await Clients.Caller.GameState(state);
        await Clients.OthersInGroup(GameGroup(request.GameId)).GameState(state);
    }

    public async Task ReadyUp(ReadyUpRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var gameId = GetCurrentGameId();
        if (gameId is null)
        {
            throw new HubException("NotInGame");
        }

        var state = await gameSession.SetReadyAsync(Context.ConnectionId, gameId, request.IsReady, Context.ConnectionAborted);
        await Clients.Group(GameGroup(gameId)).GameState(state);
    }

    public async Task StartMatch()
    {
        var gameId = GetCurrentGameId();
        if (gameId is null)
        {
            throw new HubException("NotInGame");
        }

        var state = await gameSession.StartMatchAsync(Context.ConnectionId, gameId, Context.ConnectionAborted);
        await Clients.Group(GameGroup(gameId)).GameState(state);
    }

    public async Task GetState()
    {
        var gameId = GetCurrentGameId();
        if (gameId is null)
        {
            throw new HubException("NotInGame");
        }

        var state = await gameSession.GetStateAsync(Context.ConnectionId, gameId, Context.ConnectionAborted);
        await Clients.Caller.GameState(state);
    }

    public async Task LeaveGame()
    {
        var connectionId = Context.ConnectionId;
        var playerId = GetPlayerId();
        var gameId = GetCurrentGameId();

        if (gameId is null)
        {
            return;
        }

        await Groups.RemoveFromGroupAsync(connectionId, GameGroup(gameId), Context.ConnectionAborted);
        Context.Items.Remove(GameIdKey);

        var state = await gameSession.LeaveGameAsync(connectionId, Context.ConnectionAborted);

        if (state is not null)
        {
            await Clients.Group(GameGroup(gameId)).GameState(state);
        }

        if (playerId is not null)
        {
            await Clients.Group(GameGroup(gameId)).PlayerLeft(new PlayerLeftDto(gameId, playerId));
        }
    }

    public async Task SubmitAction(PlayerActionDto action)
    {
        ArgumentNullException.ThrowIfNull(action);

        var gameId = GetCurrentGameId();
        if (gameId is null)
        {
            throw new HubException("NotInGame");
        }

        var result = await gameSession.SubmitActionAsync(Context.ConnectionId, gameId, action, Context.ConnectionAborted);
        if (!result.Accepted)
        {
            await Clients.Caller.ActionRejected(new ActionRejectedDto(result.ActionId, result.Reason ?? "Rejected", result.StateVersion, result.ServerActionSequence));
            return;
        }

        if (result.GameState is not null)
        {
            await Clients.Caller.ActionAccepted(new ActionAcceptedDto(result.ActionId, result.StateVersion, result.ServerActionSequence));
            await Clients.Group(GameGroup(gameId)).GameState(result.GameState);
        }
    }

    private string? GetPlayerId() =>
        Context.Items.TryGetValue(PlayerIdKey, out var value) ? value as string : null;

    private string? GetCurrentGameId() =>
        Context.Items.TryGetValue(GameIdKey, out var value) ? value as string : null;

    private static string GameGroup(string gameId) => $"game:{gameId}";
}
