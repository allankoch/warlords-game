namespace GameServer.Protocol;

public interface IGameClient
{
    Task Connected(ConnectedDto connected);
    Task GameState(GameStateDto state);
    Task ActionAccepted(ActionAcceptedDto accepted);
    Task ActionRejected(ActionRejectedDto rejected);
    Task PlayerJoined(PlayerJoinedDto joined);
    Task PlayerLeft(PlayerLeftDto left);
    Task LobbyChatMessage(LobbyChatMessageDto message);
    Task LobbyPlayersUpdated(IReadOnlyList<PlayerPresenceDto> players);
}
