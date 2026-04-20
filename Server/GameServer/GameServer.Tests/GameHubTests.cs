using System.Security.Claims;
using GameServer.Networking;
using GameServer.Protocol;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace GameServer.Tests;

[TestClass]
public sealed class GameHubTests
{
    [TestMethod]
    public async Task JoinGame_WhenServiceRejects_ReturnsFailureResultInsteadOfThrowing()
    {
        var gameSession = Substitute.For<IGameSessionService>();
        gameSession
            .JoinGameAsync("c1", "game-1", Arg.Any<CancellationToken>())
            .Returns<Task<GameStateDto>>(_ => throw new InvalidOperationException("MatchAlreadyStarted"));

        var caller = Substitute.For<IGameClient>();
        var others = Substitute.For<IGameClient>();
        var group = Substitute.For<IGameClient>();
        var clients = CreateClients(caller, others, group);
        var groups = Substitute.For<IGroupManager>();

        var hub = CreateHub(gameSession, clients, groups, "c1", "p1", "Allan");

        var result = await hub.JoinGame(new JoinGameRequestDto("game-1"));

        Assert.IsFalse(result.Joined);
        Assert.AreEqual("MatchAlreadyStarted", result.Reason);
        Assert.IsNull(result.GameState);
        await groups.DidNotReceive().AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await caller.DidNotReceive().GameState(Arg.Any<GameStateDto>());
        await others.DidNotReceive().PlayerJoined(Arg.Any<PlayerJoinedDto>());
    }

    [TestMethod]
    public async Task JoinGame_WhenSeatClaimIsRequired_ReturnsPausedState()
    {
        var pausedState = CreateState(
            "game-1",
            new PlayerPresenceDto("p2", true, "Bob"));
        var gameSession = Substitute.For<IGameSessionService>();
        gameSession
            .JoinGameAsync("c1", "game-1", Arg.Any<CancellationToken>())
            .Returns<Task<GameStateDto>>(_ => throw new InvalidOperationException("SeatClaimRequired"));
        gameSession
            .TryGetMatchStateAsync("game-1", Arg.Any<CancellationToken>())
            .Returns(pausedState);

        var caller = Substitute.For<IGameClient>();
        var others = Substitute.For<IGameClient>();
        var group = Substitute.For<IGameClient>();
        var clients = CreateClients(caller, others, group);
        var groups = Substitute.For<IGroupManager>();

        var hub = CreateHub(gameSession, clients, groups, "c1", "p3", "Cara");

        var result = await hub.JoinGame(new JoinGameRequestDto("game-1"));

        Assert.IsFalse(result.Joined);
        Assert.AreEqual("SeatClaimRequired", result.Reason);
        Assert.AreEqual(pausedState, result.GameState);
        await gameSession.Received(1).TryGetMatchStateAsync("game-1", Arg.Any<CancellationToken>());
        await caller.DidNotReceive().GameState(Arg.Any<GameStateDto>());
    }

    [TestMethod]
    public async Task JoinGame_WhenSuccessful_BroadcastsStateAndJoinedEvent()
    {
        var state = CreateState("game-1",
            new PlayerPresenceDto("p1", true, "Allan"),
            new PlayerPresenceDto("p2", true, "Bob"));
        var gameSession = Substitute.For<IGameSessionService>();
        gameSession.JoinGameAsync("c1", "game-1", Arg.Any<CancellationToken>())
            .Returns(state);

        var caller = Substitute.For<IGameClient>();
        var others = Substitute.For<IGameClient>();
        var group = Substitute.For<IGameClient>();
        var clients = CreateClients(caller, others, group);
        var groups = Substitute.For<IGroupManager>();

        var hub = CreateHub(gameSession, clients, groups, "c1", "p1", "Allan");

        var result = await hub.JoinGame(new JoinGameRequestDto("game-1"));

        Assert.IsTrue(result.Joined);
        Assert.AreEqual(state, result.GameState);
        await groups.Received(1).AddToGroupAsync("c1", "game:game-1", Arg.Any<CancellationToken>());
        await caller.Received(1).GameState(state);
        await others.Received(1).GameState(state);
        await others.Received(1).PlayerJoined(Arg.Is<PlayerJoinedDto>(dto =>
            dto.GameId == "game-1" &&
            dto.PlayerId == "p1" &&
            dto.DisplayName == "Allan"));
    }

    [TestMethod]
    public async Task JoinByCode_WhenSuccessful_UsesJoinContract()
    {
        var state = CreateState("game-1",
            new PlayerPresenceDto("p1", true, "Allan"),
            new PlayerPresenceDto("p2", true, "Bob"));
        var gameSession = Substitute.For<IGameSessionService>();
        gameSession.JoinGameAsync("c1", "game-1", Arg.Any<CancellationToken>())
            .Returns(state);

        var caller = Substitute.For<IGameClient>();
        var others = Substitute.For<IGameClient>();
        var group = Substitute.For<IGameClient>();
        var clients = CreateClients(caller, others, group);
        var groups = Substitute.For<IGroupManager>();

        var hub = CreateHub(gameSession, clients, groups, "c1", "p1", "Allan");

        var result = await hub.JoinByCode(new JoinByCodeRequestDto("game-1"));

        Assert.IsTrue(result.Joined);
        Assert.AreEqual(state, result.GameState);
        await gameSession.Received(1).JoinGameAsync("c1", "game-1", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task JoinByCode_WhenCodeMissing_ReturnsFailureResult()
    {
        var gameSession = Substitute.For<IGameSessionService>();
        var caller = Substitute.For<IGameClient>();
        var others = Substitute.For<IGameClient>();
        var group = Substitute.For<IGameClient>();
        var clients = CreateClients(caller, others, group);
        var groups = Substitute.For<IGroupManager>();

        var hub = CreateHub(gameSession, clients, groups, "c1", "p1", "Allan");

        var result = await hub.JoinByCode(new JoinByCodeRequestDto(""));

        Assert.IsFalse(result.Joined);
        Assert.AreEqual("CodeRequired", result.Reason);
        await gameSession.DidNotReceive().JoinGameAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ResumeGame_WhenSuccessful_DoesNotBroadcastJoinedEvent()
    {
        var state = CreateState("game-1",
            new PlayerPresenceDto("p1", true, "Allan"),
            new PlayerPresenceDto("p2", true, "Bob"));
        var gameSession = Substitute.For<IGameSessionService>();
        gameSession.JoinGameAsync("c1", "game-1", Arg.Any<CancellationToken>())
            .Returns(state);

        var caller = Substitute.For<IGameClient>();
        var others = Substitute.For<IGameClient>();
        var group = Substitute.For<IGameClient>();
        var clients = CreateClients(caller, others, group);
        var groups = Substitute.For<IGroupManager>();

        var hub = CreateHub(gameSession, clients, groups, "c1", "p1", "Allan");

        var result = await hub.ResumeGame(new JoinGameRequestDto("game-1"));

        Assert.IsTrue(result.Resumed);
        Assert.AreEqual(state, result.GameState);
        await caller.Received(1).GameState(state);
        await others.Received(1).GameState(state);
        await others.DidNotReceive().PlayerJoined(Arg.Any<PlayerJoinedDto>());
    }

    [TestMethod]
    public async Task ResumeGame_WhenSeatClaimIsRequired_ReturnsPausedState()
    {
        var pausedState = CreateState(
            "game-1",
            new PlayerPresenceDto("p2", true, "Bob"));
        var gameSession = Substitute.For<IGameSessionService>();
        gameSession
            .JoinGameAsync("c1", "game-1", Arg.Any<CancellationToken>())
            .Returns<Task<GameStateDto>>(_ => throw new InvalidOperationException("SeatClaimRequired"));
        gameSession
            .TryGetMatchStateAsync("game-1", Arg.Any<CancellationToken>())
            .Returns(pausedState);

        var caller = Substitute.For<IGameClient>();
        var others = Substitute.For<IGameClient>();
        var group = Substitute.For<IGameClient>();
        var clients = CreateClients(caller, others, group);
        var groups = Substitute.For<IGroupManager>();

        var hub = CreateHub(gameSession, clients, groups, "c1", "p3", "Cara");

        var result = await hub.ResumeGame(new JoinGameRequestDto("game-1"));

        Assert.IsFalse(result.Resumed);
        Assert.AreEqual("SeatClaimRequired", result.Reason);
        Assert.AreEqual(pausedState, result.GameState);
        await gameSession.Received(1).TryGetMatchStateAsync("game-1", Arg.Any<CancellationToken>());
        await caller.DidNotReceive().GameState(Arg.Any<GameStateDto>());
        await others.DidNotReceive().PlayerJoined(Arg.Any<PlayerJoinedDto>());
    }

    [TestMethod]
    public async Task ClaimSeat_WhenServiceRejects_ReturnsFailureResultInsteadOfThrowing()
    {
        var gameSession = Substitute.For<IGameSessionService>();
        gameSession
            .ClaimSeatAsync("c1", "game-1", "white", Arg.Any<CancellationToken>())
            .Returns<Task<GameStateDto>>(_ => throw new InvalidOperationException("SeatAlreadyClaimed"));

        var caller = Substitute.For<IGameClient>();
        var others = Substitute.For<IGameClient>();
        var group = Substitute.For<IGameClient>();
        var clients = CreateClients(caller, others, group);
        var groups = Substitute.For<IGroupManager>();

        var hub = CreateHub(gameSession, clients, groups, "c1", "p3", "Cara");

        var result = await hub.ClaimSeat(new ClaimSeatRequestDto("game-1", "white"));

        Assert.IsFalse(result.Claimed);
        Assert.AreEqual("SeatAlreadyClaimed", result.Reason);
        Assert.IsNull(result.GameState);
        await groups.DidNotReceive().AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await caller.DidNotReceive().GameState(Arg.Any<GameStateDto>());
    }

    [TestMethod]
    public async Task ClaimSeat_WhenSuccessful_BroadcastsStateAndJoinedEvent()
    {
        var state = CreateState("game-1",
            new PlayerPresenceDto("p2", true, "Bob"),
            new PlayerPresenceDto("p3", true, "Cara"));
        var gameSession = Substitute.For<IGameSessionService>();
        gameSession.ClaimSeatAsync("c1", "game-1", "white", Arg.Any<CancellationToken>())
            .Returns(state);

        var caller = Substitute.For<IGameClient>();
        var others = Substitute.For<IGameClient>();
        var group = Substitute.For<IGameClient>();
        var clients = CreateClients(caller, others, group);
        var groups = Substitute.For<IGroupManager>();

        var hub = CreateHub(gameSession, clients, groups, "c1", "p3", "Cara");

        var result = await hub.ClaimSeat(new ClaimSeatRequestDto("game-1", "white"));

        Assert.IsTrue(result.Claimed);
        Assert.AreEqual(state, result.GameState);
        await groups.Received(1).AddToGroupAsync("c1", "game:game-1", Arg.Any<CancellationToken>());
        await caller.Received(1).GameState(state);
        await others.Received(1).GameState(state);
        await others.Received(1).PlayerJoined(Arg.Is<PlayerJoinedDto>(dto =>
            dto.GameId == "game-1" &&
            dto.PlayerId == "p3" &&
            dto.DisplayName == "Cara"));
    }

    [TestMethod]
    public async Task LeaveGame_WhenStateExists_RemovesGroupAndBroadcastsStateAndLeftEvent()
    {
        var state = CreateState("game-1",
            new PlayerPresenceDto("p1", false, "Allan"),
            new PlayerPresenceDto("p2", true, "Bob"));
        var gameSession = Substitute.For<IGameSessionService>();
        gameSession.LeaveGameAsync("c1", Arg.Any<CancellationToken>())
            .Returns(state);

        var caller = Substitute.For<IGameClient>();
        var others = Substitute.For<IGameClient>();
        var group = Substitute.For<IGameClient>();
        var clients = CreateClients(caller, others, group);
        var groups = Substitute.For<IGroupManager>();

        var hub = CreateHub(gameSession, clients, groups, "c1", "p1", "Allan", "game-1");

        await hub.LeaveGame();

        await groups.Received(1).RemoveFromGroupAsync("c1", "game:game-1", Arg.Any<CancellationToken>());
        await group.Received(1).GameState(state);
        await group.Received(1).PlayerLeft(Arg.Is<PlayerLeftDto>(dto =>
            dto.GameId == "game-1" &&
            dto.PlayerId == "p1" &&
            dto.DisplayName == "Allan"));
    }

    [TestMethod]
    public async Task LeaveGame_WhenNoCurrentGame_DoesNothing()
    {
        var gameSession = Substitute.For<IGameSessionService>();
        var caller = Substitute.For<IGameClient>();
        var others = Substitute.For<IGameClient>();
        var group = Substitute.For<IGameClient>();
        var clients = CreateClients(caller, others, group);
        var groups = Substitute.For<IGroupManager>();

        var hub = CreateHub(gameSession, clients, groups, "c1", "p1", "Allan");

        await hub.LeaveGame();

        await groups.DidNotReceive().RemoveFromGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await gameSession.DidNotReceive().LeaveGameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await group.DidNotReceive().PlayerLeft(Arg.Any<PlayerLeftDto>());
    }

    [TestMethod]
    public async Task GetConnectionInfo_WhenServiceSucceeds_ReturnsConnectedDto()
    {
        var expected = new ConnectedDto("p1", "token-1", "Allan", "game-1");
        var gameSession = Substitute.For<IGameSessionService>();
        gameSession.GetConnectionInfoAsync("c1", Arg.Any<CancellationToken>())
            .Returns(expected);

        var caller = Substitute.For<IGameClient>();
        var others = Substitute.For<IGameClient>();
        var group = Substitute.For<IGameClient>();
        var clients = CreateClients(caller, others, group);
        var groups = Substitute.For<IGroupManager>();

        var hub = CreateHub(gameSession, clients, groups, "c1", "p1", "Allan");

        var result = await hub.GetConnectionInfo();

        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public async Task GetConnectionInfo_WhenServiceRejects_ThrowsHubException()
    {
        var gameSession = Substitute.For<IGameSessionService>();
        gameSession.GetConnectionInfoAsync("c1", Arg.Any<CancellationToken>())
            .Returns<Task<ConnectedDto>>(_ => throw new InvalidOperationException("UnknownPlayer"));

        var caller = Substitute.For<IGameClient>();
        var others = Substitute.For<IGameClient>();
        var group = Substitute.For<IGameClient>();
        var clients = CreateClients(caller, others, group);
        var groups = Substitute.For<IGroupManager>();

        var hub = CreateHub(gameSession, clients, groups, "c1", "p1", "Allan");

        var error = await Assert.ThrowsExceptionAsync<HubException>(() => hub.GetConnectionInfo());

        Assert.AreEqual("UnknownPlayer", error.Message);
    }

    [TestMethod]
    public async Task ListLobbyMessages_ReturnsRecentMessages()
    {
        var gameSession = Substitute.For<IGameSessionService>();
        var lobbyChat = new LobbyChatService();
        lobbyChat.AddMessage("p1", "Allan", "Join iron-ford-101");

        var caller = Substitute.For<IGameClient>();
        var others = Substitute.For<IGameClient>();
        var group = Substitute.For<IGameClient>();
        var clients = CreateClients(caller, others, group);
        var groups = Substitute.For<IGroupManager>();

        var hub = CreateHub(gameSession, clients, groups, "c1", "p1", "Allan", lobbyChat: lobbyChat);

        var result = hub.ListLobbyMessages();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("Join iron-ford-101", result[0].Message);
    }

    [TestMethod]
    public async Task SendLobbyChat_BroadcastsMessageToAllClients()
    {
        var gameSession = Substitute.For<IGameSessionService>();
        var lobbyChat = new LobbyChatService();

        var caller = Substitute.For<IGameClient>();
        var others = Substitute.For<IGameClient>();
        var group = Substitute.For<IGameClient>();
        var clients = CreateClients(caller, others, group);
        var groups = Substitute.For<IGroupManager>();

        var hub = CreateHub(gameSession, clients, groups, "c1", "p1", "Allan", lobbyChat: lobbyChat);

        var result = await hub.SendLobbyChat(new SendLobbyChatRequestDto("Anyone up for demo-10x10?"));

        Assert.AreEqual("Anyone up for demo-10x10?", result.Message);
        await group.Received(1).LobbyChatMessage(Arg.Is<LobbyChatMessageDto>(message =>
            message.PlayerId == "p1" &&
            message.DisplayName == "Allan" &&
            message.Message == "Anyone up for demo-10x10?"));
    }

    private static IHubCallerClients<IGameClient> CreateClients(
        IGameClient caller,
        IGameClient others,
        IGameClient group)
    {
        var clients = Substitute.For<IHubCallerClients<IGameClient>>();
        clients.Caller.Returns(caller);
        clients.All.Returns(group);
        clients.OthersInGroup("game:game-1").Returns(others);
        clients.Group("game:game-1").Returns(group);
        return clients;
    }

    private static GameHub CreateHub(
        IGameSessionService gameSession,
        IHubCallerClients<IGameClient> clients,
        IGroupManager groups,
        string connectionId,
        string playerId,
        string displayName,
        string? gameId = null,
        ILobbyChatService? lobbyChat = null)
    {
        var context = new TestHubCallerContext(connectionId);
        context.Items["playerId"] = playerId;
        context.Items["displayName"] = displayName;
        if (gameId is not null)
        {
            context.Items["gameId"] = gameId;
        }

        return new GameHub(gameSession, lobbyChat ?? new LobbyChatService())
        {
            Context = context,
            Clients = clients,
            Groups = groups
        };
    }

    private static GameStateDto CreateState(string gameId, params PlayerPresenceDto[] players) =>
        new(
            gameId,
            Version: 1,
            MapId: "demo-10x10",
            Phase: "Lobby",
            MinPlayers: 2,
            MaxPlayers: 2,
            HostPlayerId: players[0].PlayerId,
            Players: players,
            Ready: [],
            Slots: [],
            Entities: [],
            AvailableActions: [],
            CurrentTurnPlayerId: null,
            TurnNumber: 0,
            TurnEndsAt: null,
            ServerActionSequence: 0,
            LastAction: null,
            ServerTime: DateTimeOffset.UtcNow);

    private sealed class TestHubCallerContext(string connectionId) : HubCallerContext
    {
        private readonly IDictionary<object, object?> _items = new Dictionary<object, object?>();

        public override string ConnectionId { get; } = connectionId;
        public override string? UserIdentifier => null;
        public override ClaimsPrincipal? User => null;
        public override IDictionary<object, object?> Items => _items;
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted { get; } = CancellationToken.None;
        public override void Abort() { }
    }
}
