using GameServer.Game;
using GameServer.Game.Engine;
using GameServer.Persistence;
using GameServer.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;

namespace GameServer.Tests;

[TestClass]
public sealed class GameServiceTests
{
    [TestMethod]
    public async Task CreateJoinStartFlow_TransitionsMatchToInProgress()
    {
        var engine = new GameEngine(new TestMapProvider());
        var mapProvider = Substitute.For<IMapProvider>();
        mapProvider.Get("test-map").Returns(new TestMapProvider().Get("test-map"));

        var identityStore = Substitute.For<IIdentityStore>();
        identityStore.ResolveAsync(null, "Allan", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlayerIdentity("p1", "token-1", "Allan", null)));
        identityStore.ResolveAsync(null, "Bob", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlayerIdentity("p2", "token-2", "Bob", null)));

        var matchStore = Substitute.For<IMatchStore>();
        var persistence = Substitute.For<IGamePersistence>();
        var service = new GameService(engine, mapProvider, identityStore, matchStore, persistence);

        await service.ConnectAsync("c1", null, "Allan", CancellationToken.None);
        await service.ConnectAsync("c2", null, "Bob", CancellationToken.None);

        var created = await service.CreateAndJoinMatchAsync(
            "c1",
            new CreateMatchRequestDto("game-1", "test-map", MinPlayers: 2, MaxPlayers: 2, AutoStart: false, TurnTimeLimitSeconds: 0, DisconnectGraceSeconds: 120),
            CancellationToken.None);
        var joined = await service.JoinGameAsync("c2", "game-1", CancellationToken.None);
        await service.SetReadyAsync("c1", "game-1", true, CancellationToken.None);
        await service.SetReadyAsync("c2", "game-1", true, CancellationToken.None);
        var started = await service.StartMatchAsync("c1", "game-1", CancellationToken.None);

        Assert.AreEqual(MatchPhases.Lobby, created.Phase);
        Assert.AreEqual(1, created.Players.Count);
        Assert.AreEqual(2, joined.Players.Count);
        Assert.AreEqual(MatchPhases.InProgress, started.Phase);
        Assert.AreEqual(2, started.Entities.Count);
        Assert.AreEqual("p1", started.CurrentTurnPlayerId);

        await identityStore.Received().SetActiveGameAsync("p1", "game-1", Arg.Any<CancellationToken>());
        await identityStore.Received().SetActiveGameAsync("p2", "game-1", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ConnectAsync_WithReconnectToken_ReturnsResumeGameIdForActiveMatch()
    {
        var engine = new GameEngine(new TestMapProvider());
        var mapProvider = Substitute.For<IMapProvider>();
        mapProvider.Get("test-map").Returns(new TestMapProvider().Get("test-map"));

        var identityStore = Substitute.For<IIdentityStore>();
        identityStore.ResolveAsync(null, "Host", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlayerIdentity("p1", "token-1", "Host", null)));
        identityStore.ResolveAsync("token-1", "Host", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlayerIdentity("p1", "token-1", "Host", "game-1")));

        var matchStore = Substitute.For<IMatchStore>();
        var persistence = Substitute.For<IGamePersistence>();
        var service = new GameService(engine, mapProvider, identityStore, matchStore, persistence);

        await service.ConnectAsync("c1", null, "Host", CancellationToken.None);
        var create = new CreateMatchRequestDto("game-1", "test-map", MinPlayers: 2, MaxPlayers: 2, AutoStart: false, TurnTimeLimitSeconds: 0, DisconnectGraceSeconds: 120);
        await service.CreateAndJoinMatchAsync("c1", create, CancellationToken.None);
        await service.DisconnectAsync("c1", CancellationToken.None);

        var reconnected = await service.ConnectAsync("c2", "token-1", "Host", CancellationToken.None);

        Assert.AreEqual("p1", reconnected.PlayerId);
        Assert.AreEqual("game-1", reconnected.ResumeGameId);
        await identityStore.Received().SetActiveGameAsync("p1", "game-1", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task GetConnectionInfoAsync_ReturnsAuthoritativeResumeGameIdForCurrentConnection()
    {
        var engine = new GameEngine(new TestMapProvider());
        var mapProvider = Substitute.For<IMapProvider>();
        mapProvider.Get("test-map").Returns(new TestMapProvider().Get("test-map"));

        var identityStore = Substitute.For<IIdentityStore>();
        identityStore.ResolveAsync(null, "Host", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlayerIdentity("p1", "token-1", "Host", null)));
        identityStore.TryGetByPlayerIdAsync("p1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<PlayerIdentity?>(new PlayerIdentity("p1", "token-1", "Host", "game-1")));

        var matchStore = Substitute.For<IMatchStore>();
        var persistence = Substitute.For<IGamePersistence>();
        var service = new GameService(engine, mapProvider, identityStore, matchStore, persistence);

        await service.ConnectAsync("c1", null, "Host", CancellationToken.None);

        var info = await service.GetConnectionInfoAsync("c1", CancellationToken.None);

        Assert.AreEqual("p1", info.PlayerId);
        Assert.AreEqual("token-1", info.ReconnectToken);
        Assert.AreEqual("Host", info.DisplayName);
        Assert.AreEqual("game-1", info.ResumeGameId);
    }

    [TestMethod]
    public async Task JoinGameAsync_LoadsPersistedMatch_WhenSessionIsNotInMemory()
    {
        var engine = new GameEngine(new TestMapProvider());
        var mapProvider = Substitute.For<IMapProvider>();
        mapProvider.Get("test-map").Returns(new TestMapProvider().Get("test-map"));

        var seedIdentityStore = Substitute.For<IIdentityStore>();
        seedIdentityStore.ResolveAsync(null, "Allan", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlayerIdentity("p1", "token-1", "Allan", null)));
        seedIdentityStore.ResolveAsync(null, "Bob", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlayerIdentity("p2", "token-2", "Bob", null)));

        PersistedMatchSnapshot? storedSnapshot = null;
        var matchStore = Substitute.For<IMatchStore>();
        matchStore
            .When(store => store.SaveSnapshotAsync(Arg.Any<PersistedMatchSnapshot>(), Arg.Any<CancellationToken>()))
            .Do(callInfo => storedSnapshot = callInfo.Arg<PersistedMatchSnapshot>());
        var persistence = Substitute.For<IGamePersistence>();

        var seedService = new GameService(engine, mapProvider, seedIdentityStore, matchStore, persistence);
        await seedService.ConnectAsync("seed-c1", null, "Allan", CancellationToken.None);
        await seedService.ConnectAsync("seed-c2", null, "Bob", CancellationToken.None);
        await seedService.CreateAndJoinMatchAsync(
            "seed-c1",
            new CreateMatchRequestDto("game-1", "test-map", MinPlayers: 2, MaxPlayers: 2, AutoStart: false, TurnTimeLimitSeconds: 0, DisconnectGraceSeconds: 120),
            CancellationToken.None);
        await seedService.JoinGameAsync("seed-c2", "game-1", CancellationToken.None);
        await seedService.SetReadyAsync("seed-c1", "game-1", true, CancellationToken.None);
        await seedService.SetReadyAsync("seed-c2", "game-1", true, CancellationToken.None);
        await seedService.StartMatchAsync("seed-c1", "game-1", CancellationToken.None);
        await seedService.DisconnectAsync("seed-c1", CancellationToken.None);

        Assert.IsNotNull(storedSnapshot);

        var reconnectIdentityStore = Substitute.For<IIdentityStore>();
        reconnectIdentityStore.ResolveAsync("token-1", "Allan", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlayerIdentity("p1", "token-1", "Allan", "game-1")));
        matchStore.LoadSnapshotAsync("game-1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<PersistedMatchSnapshot?>(storedSnapshot));

        var reconnectService = new GameService(engine, mapProvider, reconnectIdentityStore, matchStore, persistence);
        await reconnectService.ConnectAsync("reconnect-c1", "token-1", "Allan", CancellationToken.None);

        var rejoined = await reconnectService.JoinGameAsync("reconnect-c1", "game-1", CancellationToken.None);

        Assert.AreEqual(MatchPhases.InProgress, rejoined.Phase);
        Assert.AreEqual(2, rejoined.Players.Count);
        Assert.IsTrue(rejoined.Players.Single(player => player.PlayerId == "p1").IsConnected);
        Assert.AreEqual("Allan", rejoined.Players.Single(player => player.PlayerId == "p1").DisplayName);
        await reconnectIdentityStore.Received().SetActiveGameAsync("p1", "game-1", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task JoinGameAsync_ThrowsForUnknownGame()
    {
        var engine = new GameEngine(new TestMapProvider());
        var mapProvider = Substitute.For<IMapProvider>();
        var identityStore = Substitute.For<IIdentityStore>();
        identityStore.ResolveAsync(null, "Allan", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlayerIdentity("p1", "token-1", "Allan", null)));

        var matchStore = Substitute.For<IMatchStore>();
        var persistence = Substitute.For<IGamePersistence>();
        var service = new GameService(engine, mapProvider, identityStore, matchStore, persistence);

        await service.ConnectAsync("c1", null, "Allan", CancellationToken.None);

        var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            service.JoinGameAsync("c1", "missing-game", CancellationToken.None));

        Assert.AreEqual("UnknownGame", error.Message);
    }

    [TestMethod]
    public async Task JoinGameAsync_RejectsNewPlayerAfterMatchHasStarted()
    {
        var engine = new GameEngine(new TestMapProvider());
        var mapProvider = Substitute.For<IMapProvider>();
        mapProvider.Get("test-map").Returns(new TestMapProvider().Get("test-map"));

        var identityStore = Substitute.For<IIdentityStore>();
        identityStore.ResolveAsync(null, "Allan", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlayerIdentity("p1", "token-1", "Allan", null)));
        identityStore.ResolveAsync(null, "Bob", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlayerIdentity("p2", "token-2", "Bob", null)));
        identityStore.ResolveAsync(null, "Cara", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlayerIdentity("p3", "token-3", "Cara", null)));

        var matchStore = Substitute.For<IMatchStore>();
        var persistence = Substitute.For<IGamePersistence>();
        var service = new GameService(engine, mapProvider, identityStore, matchStore, persistence);

        await service.ConnectAsync("c1", null, "Allan", CancellationToken.None);
        await service.ConnectAsync("c2", null, "Bob", CancellationToken.None);
        await service.CreateAndJoinMatchAsync(
            "c1",
            new CreateMatchRequestDto("game-1", "test-map", MinPlayers: 2, MaxPlayers: 2, AutoStart: false, TurnTimeLimitSeconds: 0, DisconnectGraceSeconds: 120),
            CancellationToken.None);
        await service.JoinGameAsync("c2", "game-1", CancellationToken.None);
        await service.SetReadyAsync("c1", "game-1", true, CancellationToken.None);
        await service.SetReadyAsync("c2", "game-1", true, CancellationToken.None);
        await service.StartMatchAsync("c1", "game-1", CancellationToken.None);

        await service.ConnectAsync("c3", null, "Cara", CancellationToken.None);

        var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            service.JoinGameAsync("c3", "game-1", CancellationToken.None));

        Assert.AreEqual("MatchAlreadyStarted", error.Message);
    }

    [TestMethod]
    public async Task LeaveGameAsync_FromLobby_RemovesPlayerAndClearsActiveGame()
    {
        var engine = new GameEngine(new TestMapProvider());
        var mapProvider = Substitute.For<IMapProvider>();
        mapProvider.Get("test-map").Returns(new TestMapProvider().Get("test-map"));

        var identityStore = Substitute.For<IIdentityStore>();
        identityStore.ResolveAsync(null, "Host", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlayerIdentity("p1", "token-1", "Host", null)));
        identityStore.ResolveAsync(null, "Guest", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlayerIdentity("p2", "token-2", "Guest", null)));

        var matchStore = Substitute.For<IMatchStore>();
        var persistence = Substitute.For<IGamePersistence>();
        var service = new GameService(engine, mapProvider, identityStore, matchStore, persistence);

        await service.ConnectAsync("c1", null, "Host", CancellationToken.None);
        await service.ConnectAsync("c2", null, "Guest", CancellationToken.None);
        await service.CreateAndJoinMatchAsync(
            "c1",
            new CreateMatchRequestDto("game-1", "test-map", MinPlayers: 2, MaxPlayers: 2, AutoStart: false, TurnTimeLimitSeconds: 0, DisconnectGraceSeconds: 120),
            CancellationToken.None);
        await service.JoinGameAsync("c2", "game-1", CancellationToken.None);

        var leftState = await service.LeaveGameAsync("c2", CancellationToken.None);

        Assert.IsNotNull(leftState);
        Assert.AreEqual(MatchPhases.Lobby, leftState.Phase);
        Assert.IsFalse(leftState.Players.Any(player => player.PlayerId == "p2"));
        await identityStore.Received().SetActiveGameAsync("p2", null, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task LeaveGame_MidMatch_KeepsPlayerEligibleToRejoin()
    {
        var engine = new GameEngine(new TestMapProvider());
        var mapProvider = Substitute.For<IMapProvider>();
        mapProvider.Get("test-map").Returns(new TestMapProvider().Get("test-map"));

        var identityStore = Substitute.For<IIdentityStore>();
        identityStore.ResolveAsync(null, "Host", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlayerIdentity("p1", "token-1", "Host", null)));
        identityStore.ResolveAsync(null, "Guest", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlayerIdentity("p2", "token-2", "Guest", null)));

        var matchStore = Substitute.For<IMatchStore>();
        var persistence = Substitute.For<IGamePersistence>();
        var service = new GameService(engine, mapProvider, identityStore, matchStore, persistence);

        await service.ConnectAsync("c1", null, "Host", CancellationToken.None);
        await service.ConnectAsync("c2", null, "Guest", CancellationToken.None);

        var create = new CreateMatchRequestDto("game-1", "test-map", MinPlayers: 2, MaxPlayers: 2, AutoStart: false, TurnTimeLimitSeconds: 0, DisconnectGraceSeconds: 120);
        await service.CreateAndJoinMatchAsync("c1", create, CancellationToken.None);
        await service.JoinGameAsync("c2", "game-1", CancellationToken.None);
        await service.SetReadyAsync("c1", "game-1", true, CancellationToken.None);
        await service.SetReadyAsync("c2", "game-1", true, CancellationToken.None);
        await service.StartMatchAsync("c1", "game-1", CancellationToken.None);

        var leftState = await service.LeaveGameAsync("c1", CancellationToken.None);
        Assert.IsNotNull(leftState);
        Assert.IsFalse(leftState.Players.Single(player => player.PlayerId == "p1").IsConnected);
        await identityStore.Received().SetActiveGameAsync("p1", null, Arg.Any<CancellationToken>());

        var rejoinedState = await service.JoinGameAsync("c1", "game-1", CancellationToken.None);
        Assert.IsTrue(rejoinedState.Players.Single(player => player.PlayerId == "p1").IsConnected);
        Assert.AreEqual(MatchPhases.InProgress, rejoinedState.Phase);
        await identityStore.Received().SetActiveGameAsync("p1", "game-1", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task StartMatchAsync_RejectsNonHost()
    {
        var engine = new GameEngine(new TestMapProvider());
        var mapProvider = Substitute.For<IMapProvider>();
        mapProvider.Get("test-map").Returns(new TestMapProvider().Get("test-map"));

        var identityStore = Substitute.For<IIdentityStore>();
        identityStore.ResolveAsync(null, "Host", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlayerIdentity("p1", "token-1", "Host", null)));
        identityStore.ResolveAsync(null, "Guest", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlayerIdentity("p2", "token-2", "Guest", null)));

        var matchStore = Substitute.For<IMatchStore>();
        var persistence = Substitute.For<IGamePersistence>();
        var service = new GameService(engine, mapProvider, identityStore, matchStore, persistence);

        await service.ConnectAsync("c1", null, "Host", CancellationToken.None);
        await service.ConnectAsync("c2", null, "Guest", CancellationToken.None);
        await service.CreateAndJoinMatchAsync(
            "c1",
            new CreateMatchRequestDto("game-1", "test-map", MinPlayers: 2, MaxPlayers: 2, AutoStart: false, TurnTimeLimitSeconds: 0, DisconnectGraceSeconds: 120),
            CancellationToken.None);
        await service.JoinGameAsync("c2", "game-1", CancellationToken.None);
        await service.SetReadyAsync("c1", "game-1", true, CancellationToken.None);
        await service.SetReadyAsync("c2", "game-1", true, CancellationToken.None);

        var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            service.StartMatchAsync("c2", "game-1", CancellationToken.None));

        Assert.AreEqual("HostOnly", error.Message);
    }

    [TestMethod]
    public async Task StartMatchAsync_RejectsWhenPlayersAreNotReady()
    {
        var engine = new GameEngine(new TestMapProvider());
        var mapProvider = Substitute.For<IMapProvider>();
        mapProvider.Get("test-map").Returns(new TestMapProvider().Get("test-map"));

        var identityStore = Substitute.For<IIdentityStore>();
        identityStore.ResolveAsync(null, "Host", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlayerIdentity("p1", "token-1", "Host", null)));
        identityStore.ResolveAsync(null, "Guest", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlayerIdentity("p2", "token-2", "Guest", null)));

        var matchStore = Substitute.For<IMatchStore>();
        var persistence = Substitute.For<IGamePersistence>();
        var service = new GameService(engine, mapProvider, identityStore, matchStore, persistence);

        await service.ConnectAsync("c1", null, "Host", CancellationToken.None);
        await service.ConnectAsync("c2", null, "Guest", CancellationToken.None);
        await service.CreateAndJoinMatchAsync(
            "c1",
            new CreateMatchRequestDto("game-1", "test-map", MinPlayers: 2, MaxPlayers: 2, AutoStart: false, TurnTimeLimitSeconds: 0, DisconnectGraceSeconds: 120),
            CancellationToken.None);
        await service.JoinGameAsync("c2", "game-1", CancellationToken.None);
        await service.SetReadyAsync("c1", "game-1", true, CancellationToken.None);

        var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            service.StartMatchAsync("c1", "game-1", CancellationToken.None));

        Assert.AreEqual("NotReady", error.Message);
    }

    [TestMethod]
    public async Task SubmitActionAsync_RejectsStaleStateAndOutOfOrderSequences()
    {
        var engine = new GameEngine(new TestMapProvider());
        var mapProvider = Substitute.For<IMapProvider>();
        mapProvider.Get("test-map").Returns(new TestMapProvider().Get("test-map"));

        var identityStore = Substitute.For<IIdentityStore>();
        identityStore.ResolveAsync(null, "Allan", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlayerIdentity("p1", "token-1", "Allan", null)));
        identityStore.ResolveAsync(null, "Bob", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlayerIdentity("p2", "token-2", "Bob", null)));

        var matchStore = Substitute.For<IMatchStore>();
        var persistence = Substitute.For<IGamePersistence>();
        var service = new GameService(engine, mapProvider, identityStore, matchStore, persistence);

        await service.ConnectAsync("c1", null, "Allan", CancellationToken.None);
        await service.ConnectAsync("c2", null, "Bob", CancellationToken.None);
        await service.CreateAndJoinMatchAsync(
            "c1",
            new CreateMatchRequestDto("game-1", "test-map", MinPlayers: 2, MaxPlayers: 2, AutoStart: false, TurnTimeLimitSeconds: 0, DisconnectGraceSeconds: 120),
            CancellationToken.None);
        await service.JoinGameAsync("c2", "game-1", CancellationToken.None);
        await service.SetReadyAsync("c1", "game-1", true, CancellationToken.None);
        await service.SetReadyAsync("c2", "game-1", true, CancellationToken.None);
        var started = await service.StartMatchAsync("c1", "game-1", CancellationToken.None);

        var accepted = await service.SubmitActionAsync(
            "c1",
            "game-1",
            new MoveEntityActionDto
            {
                ActionId = "move-1",
                ClientSequence = 1,
                ExpectedStateVersion = started.Version,
                EntityId = "unit-p1",
                X = 0,
                Y = 1
            },
            CancellationToken.None);

        var staleRejected = await service.SubmitActionAsync(
            "c1",
            "game-1",
            new MoveEntityActionDto
            {
                ActionId = "move-2",
                ClientSequence = 2,
                ExpectedStateVersion = started.Version,
                EntityId = "unit-p1",
                X = 0,
                Y = 2
            },
            CancellationToken.None);

        var outOfOrderRejected = await service.SubmitActionAsync(
            "c1",
            "game-1",
            new EndTurnActionDto
            {
                ActionId = "end-1",
                ClientSequence = 1,
                ExpectedStateVersion = accepted.StateVersion
            },
            CancellationToken.None);

        Assert.IsTrue(accepted.Accepted);
        Assert.AreEqual("StaleState", staleRejected.Reason);
        Assert.IsFalse(staleRejected.Accepted);
        Assert.AreEqual("OutOfOrder", outOfOrderRejected.Reason);
        Assert.IsFalse(outOfOrderRejected.Accepted);
        await persistence.Received(1).PersistAcceptedActionAsync(Arg.Any<PersistedMatchSnapshot>(), Arg.Any<MatchActionRecord>(), Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task DisconnectAndReconnect_PreservesCurrentPlayersTurnWithinGrace()
    {
        var engine = new GameEngine(new TestMapProvider());
        var mapProvider = Substitute.For<IMapProvider>();
        mapProvider.Get("test-map").Returns(new TestMapProvider().Get("test-map"));

        var identityStore = Substitute.For<IIdentityStore>();
        identityStore.ResolveAsync(null, "Allan", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlayerIdentity("p1", "token-1", "Allan", null)));
        identityStore.ResolveAsync(null, "Bob", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlayerIdentity("p2", "token-2", "Bob", null)));
        identityStore.ResolveAsync("token-1", "Allan", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlayerIdentity("p1", "token-1", "Allan", "game-1")));

        var matchStore = Substitute.For<IMatchStore>();
        var persistence = Substitute.For<IGamePersistence>();
        var service = new GameService(engine, mapProvider, identityStore, matchStore, persistence);

        await service.ConnectAsync("c1", null, "Allan", CancellationToken.None);
        await service.ConnectAsync("c2", null, "Bob", CancellationToken.None);
        await service.CreateAndJoinMatchAsync(
            "c1",
            new CreateMatchRequestDto("game-1", "test-map", MinPlayers: 2, MaxPlayers: 2, AutoStart: false, TurnTimeLimitSeconds: 60, DisconnectGraceSeconds: 120),
            CancellationToken.None);
        await service.JoinGameAsync("c2", "game-1", CancellationToken.None);
        await service.SetReadyAsync("c1", "game-1", true, CancellationToken.None);
        await service.SetReadyAsync("c2", "game-1", true, CancellationToken.None);
        await service.StartMatchAsync("c1", "game-1", CancellationToken.None);

        var disconnected = await service.DisconnectAsync("c1", CancellationToken.None);
        Assert.IsNotNull(disconnected);
        Assert.AreEqual("p1", disconnected.CurrentTurnPlayerId);
        Assert.IsFalse(disconnected.Players.Single(player => player.PlayerId == "p1").IsConnected);

        await service.ConnectAsync("c1b", "token-1", "Allan", CancellationToken.None);
        var resumed = await service.JoinGameAsync("c1b", "game-1", CancellationToken.None);

        Assert.AreEqual("p1", resumed.CurrentTurnPlayerId);
        Assert.IsTrue(resumed.Players.Single(player => player.PlayerId == "p1").IsConnected);
    }

    [TestMethod]
    public async Task JoinGameAsync_WhenSeatIsUnclaimedAfterGraceExpiry_RequiresExplicitClaim()
    {
        var engine = new GameEngine(new TestMapProvider());
        var mapProvider = Substitute.For<IMapProvider>();
        mapProvider.Get("test-map").Returns(new TestMapProvider().Get("test-map"));

        var identityStore = Substitute.For<IIdentityStore>();
        identityStore.ResolveAsync(null, "Allan", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlayerIdentity("p1", "token-1", "Allan", null)));
        identityStore.ResolveAsync(null, "Bob", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlayerIdentity("p2", "token-2", "Bob", null)));
        identityStore.ResolveAsync(null, "Cara", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlayerIdentity("p3", "token-3", "Cara", null)));
        identityStore.TryGetByPlayerIdAsync("p1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<PlayerIdentity?>(new PlayerIdentity("p1", "token-1", "Allan", "game-1")));

        var matchStore = Substitute.For<IMatchStore>();
        var persistence = Substitute.For<IGamePersistence>();
        var service = new GameService(engine, mapProvider, identityStore, matchStore, persistence);

        await service.ConnectAsync("c1", null, "Allan", CancellationToken.None);
        await service.ConnectAsync("c2", null, "Bob", CancellationToken.None);
        await service.CreateAndJoinMatchAsync(
            "c1",
            new CreateMatchRequestDto("game-1", "test-map", MinPlayers: 2, MaxPlayers: 2, AutoStart: false, TurnTimeLimitSeconds: 60, DisconnectGraceSeconds: 0),
            CancellationToken.None);
        await service.JoinGameAsync("c2", "game-1", CancellationToken.None);
        await service.SetReadyAsync("c1", "game-1", true, CancellationToken.None);
        await service.SetReadyAsync("c2", "game-1", true, CancellationToken.None);
        await service.StartMatchAsync("c1", "game-1", CancellationToken.None);

        await service.DisconnectAsync("c1", CancellationToken.None);
        await service.TickAsync(CancellationToken.None);
        await service.TickAsync(CancellationToken.None);

        await service.ConnectAsync("c3", null, "Cara", CancellationToken.None);
        var joinError = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            service.JoinGameAsync("c3", "game-1", CancellationToken.None));

        Assert.AreEqual("SeatClaimRequired", joinError.Message);

        var claimed = await service.ClaimSeatAsync("c3", "game-1", "white", CancellationToken.None);

        Assert.IsTrue(claimed.Players.Any(player => player.PlayerId == "p3" && player.IsConnected));
        Assert.IsFalse(claimed.IsPausedForSeatClaim);
        Assert.AreEqual("p3", claimed.Entities.Single(entity => entity.EntityId == "unit-p1").OwnerPlayerId);
        Assert.IsTrue(claimed.Seats!.Any(seat => seat.SeatId == "white" && seat.IsClaimed && seat.ClaimedByPlayerId == "p3"));
        await identityStore.Received().SetActiveGameAsync("p3", "game-1", Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task TickAsync_WhenDisconnectGraceExpires_ClearsRemovedPlayersActiveGame()
    {
        var engine = new GameEngine(new TestMapProvider());
        var mapProvider = Substitute.For<IMapProvider>();
        mapProvider.Get("test-map").Returns(new TestMapProvider().Get("test-map"));

        var identityStore = Substitute.For<IIdentityStore>();
        identityStore.ResolveAsync(null, "Allan", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlayerIdentity("p1", "token-1", "Allan", null)));
        identityStore.ResolveAsync(null, "Bob", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlayerIdentity("p2", "token-2", "Bob", null)));
        identityStore.TryGetByPlayerIdAsync("p1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<PlayerIdentity?>(new PlayerIdentity("p1", "token-1", "Allan", "game-1")));

        var matchStore = Substitute.For<IMatchStore>();
        var persistence = Substitute.For<IGamePersistence>();
        var service = new GameService(engine, mapProvider, identityStore, matchStore, persistence);

        await service.ConnectAsync("c1", null, "Allan", CancellationToken.None);
        await service.ConnectAsync("c2", null, "Bob", CancellationToken.None);
        await service.CreateAndJoinMatchAsync(
            "c1",
            new CreateMatchRequestDto("game-1", "test-map", MinPlayers: 2, MaxPlayers: 2, AutoStart: false, TurnTimeLimitSeconds: 0, DisconnectGraceSeconds: 0),
            CancellationToken.None);
        await service.JoinGameAsync("c2", "game-1", CancellationToken.None);
        await service.SetReadyAsync("c1", "game-1", true, CancellationToken.None);
        await service.SetReadyAsync("c2", "game-1", true, CancellationToken.None);
        await service.StartMatchAsync("c1", "game-1", CancellationToken.None);

        await service.DisconnectAsync("c1", CancellationToken.None);

        await service.TickAsync(CancellationToken.None);
        var updates = await service.TickAsync(CancellationToken.None);
        var update = updates.Single(item => item.GameId == "game-1");

        Assert.IsFalse(update.State.Players.Any(player => player.PlayerId == "p1"));
        Assert.IsTrue(update.State.IsPausedForSeatClaim);
        Assert.AreEqual(2, update.State.Entities.Count);
        Assert.IsTrue(update.State.Seats!.Any(seat => seat.SeatId == "white" && seat.IsActive && !seat.IsClaimed && seat.HasUnits));
        await identityStore.Received().SetActiveGameAsync("p1", null, Arg.Any<CancellationToken>());
    }

    [TestMethod]
    public async Task ClaimSeatAsync_RejectsAlreadyClaimedSeat()
    {
        var engine = new GameEngine(new TestMapProvider());
        var mapProvider = Substitute.For<IMapProvider>();
        mapProvider.Get("test-map").Returns(new TestMapProvider().Get("test-map"));

        var identityStore = Substitute.For<IIdentityStore>();
        identityStore.ResolveAsync(null, "Allan", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlayerIdentity("p1", "token-1", "Allan", null)));
        identityStore.ResolveAsync(null, "Bob", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlayerIdentity("p2", "token-2", "Bob", null)));
        identityStore.ResolveAsync(null, "Cara", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlayerIdentity("p3", "token-3", "Cara", null)));

        var matchStore = Substitute.For<IMatchStore>();
        var persistence = Substitute.For<IGamePersistence>();
        var service = new GameService(engine, mapProvider, identityStore, matchStore, persistence);

        await service.ConnectAsync("c1", null, "Allan", CancellationToken.None);
        await service.ConnectAsync("c2", null, "Bob", CancellationToken.None);
        await service.ConnectAsync("c3", null, "Cara", CancellationToken.None);
        await service.CreateAndJoinMatchAsync(
            "c1",
            new CreateMatchRequestDto("game-1", "test-map", MinPlayers: 2, MaxPlayers: 2, AutoStart: false, TurnTimeLimitSeconds: 0, DisconnectGraceSeconds: 120),
            CancellationToken.None);
        await service.JoinGameAsync("c2", "game-1", CancellationToken.None);

        var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() =>
            service.ClaimSeatAsync("c3", "game-1", "white", CancellationToken.None));

        Assert.AreEqual("SeatAlreadyClaimed", error.Message);
    }

    [TestMethod]
    public async Task TickAsync_WhenTurnExpires_ProducesSystemEndTurnUpdate()
    {
        var engine = new GameEngine(new TestMapProvider());
        var mapProvider = Substitute.For<IMapProvider>();
        mapProvider.Get("test-map").Returns(new TestMapProvider().Get("test-map"));

        var identityStore = Substitute.For<IIdentityStore>();
        identityStore.ResolveAsync(null, "Allan", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlayerIdentity("p1", "token-1", "Allan", null)));
        identityStore.ResolveAsync(null, "Bob", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlayerIdentity("p2", "token-2", "Bob", null)));

        PersistedMatchSnapshot? lastSnapshot = null;
        var matchStore = Substitute.For<IMatchStore>();
        matchStore
            .When(store => store.SaveSnapshotAsync(Arg.Any<PersistedMatchSnapshot>(), Arg.Any<CancellationToken>()))
            .Do(callInfo => lastSnapshot = callInfo.Arg<PersistedMatchSnapshot>());
        var persistence = Substitute.For<IGamePersistence>();

        var seedService = new GameService(engine, mapProvider, identityStore, matchStore, persistence);

        await seedService.ConnectAsync("c1", null, "Allan", CancellationToken.None);
        await seedService.ConnectAsync("c2", null, "Bob", CancellationToken.None);
        await seedService.CreateAndJoinMatchAsync(
            "c1",
            new CreateMatchRequestDto("game-1", "test-map", MinPlayers: 2, MaxPlayers: 2, AutoStart: false, TurnTimeLimitSeconds: 1, DisconnectGraceSeconds: 120),
            CancellationToken.None);
        await seedService.JoinGameAsync("c2", "game-1", CancellationToken.None);
        await seedService.SetReadyAsync("c1", "game-1", true, CancellationToken.None);
        await seedService.SetReadyAsync("c2", "game-1", true, CancellationToken.None);
        await seedService.StartMatchAsync("c1", "game-1", CancellationToken.None);

        Assert.IsNotNull(lastSnapshot);
        var snapshotDto = MatchSnapshotMapper.Deserialize(lastSnapshot!.SnapshotJson);
        var expiredSnapshot = lastSnapshot with
        {
            SnapshotJson = MatchSnapshotMapper.Serialize(snapshotDto with { TurnEndsAtUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 1 })
        };
        var reloadIdentityStore = Substitute.For<IIdentityStore>();
        reloadIdentityStore.ResolveAsync("token-1", "Allan", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlayerIdentity("p1", "token-1", "Allan", "game-1")));
        reloadIdentityStore.ResolveAsync("token-2", "Bob", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PlayerIdentity("p2", "token-2", "Bob", "game-1")));

        var reloadMatchStore = Substitute.For<IMatchStore>();
        reloadMatchStore.LoadSnapshotAsync("game-1", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<PersistedMatchSnapshot?>(expiredSnapshot));

        var reloadPersistence = Substitute.For<IGamePersistence>();
        var service = new GameService(engine, mapProvider, reloadIdentityStore, reloadMatchStore, reloadPersistence);

        await service.ConnectAsync("c1r", "token-1", "Allan", CancellationToken.None);
        await service.ConnectAsync("c2r", "token-2", "Bob", CancellationToken.None);
        await service.JoinGameAsync("c1r", "game-1", CancellationToken.None);
        await service.JoinGameAsync("c2r", "game-1", CancellationToken.None);

        var updates = await service.TickAsync(CancellationToken.None);
        var update = updates.Single(item => item.GameId == "game-1");

        Assert.IsTrue(update.IsSystemEndTurn);
        Assert.AreEqual("p2", update.State.CurrentTurnPlayerId);
        Assert.IsInstanceOfType<EndTurnActionDto>(update.State.LastAction);
        await reloadPersistence.Received().PersistAcceptedActionAsync(
            Arg.Any<PersistedMatchSnapshot>(),
            Arg.Is<MatchActionRecord>(record => record.PlayerId == "system"),
            Arg.Any<CancellationToken>());
    }
}


