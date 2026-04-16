using GameServer.Networking;
using GameServer.Protocol;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;

namespace GameServer.Game;

public sealed class MatchTimeoutService(
    GameService gameService,
    IHubContext<GameHub, IGameClient> hubContext) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var updates = await gameService.TickAsync(stoppingToken);
            foreach (var update in updates)
            {
                await hubContext.Clients.Group($"game:{update.GameId}").GameState(update.State);
            }
        }
    }
}

