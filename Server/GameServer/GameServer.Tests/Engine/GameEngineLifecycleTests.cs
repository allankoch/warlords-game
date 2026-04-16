using GameServer.Game.Engine;
using GameServer.Protocol;
using GameServer.Tests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GameServer.Tests.Engine;

[TestClass]
public sealed class GameEngineLifecycleTests
{
    private readonly IGameEngine _engine = new GameEngine(new TestMapProvider());

    [TestMethod]
    public void ReadyUp_UnknownPlayer_IsRejected()
    {
        var state = CreateLobbyMatch(autoStart: false);

        var result = _engine.SetReady(state, playerId: "missing", isReady: true);
        Assert.IsFalse(result.Success);
        Assert.AreEqual("UnknownPlayer", result.Error);
    }

    [TestMethod]
    public void ReadyUp_DisconnectedPlayer_IsRejected()
    {
        var state = CreateLobbyMatch(autoStart: false);
        state = ExpectOk(_engine.AddOrReconnectPlayer(state, "p1"));
        state = ExpectOk(_engine.SetConnected(state, "p1", isConnected: false));

        var result = _engine.SetReady(state, "p1", isReady: true);
        Assert.IsFalse(result.Success);
        Assert.AreEqual("PlayerDisconnected", result.Error);
    }

    [TestMethod]
    public void StartMatch_NonHost_IsRejected()
    {
        var state = CreateLobbyMatch(autoStart: false, minPlayers: 2, maxPlayers: 2, hostPlayerId: "host");
        state = ExpectOk(_engine.AddOrReconnectPlayer(state, "host"));
        state = ExpectOk(_engine.AddOrReconnectPlayer(state, "p2"));
        state = ExpectOk(_engine.SetReady(state, "host", true));
        state = ExpectOk(_engine.SetReady(state, "p2", true));

        var result = _engine.StartMatch(state, requestingPlayerId: "p2");
        Assert.IsFalse(result.Success);
        Assert.AreEqual("HostOnly", result.Error);
    }

    [TestMethod]
    public void StartMatch_NotReady_IsRejected()
    {
        var state = CreateLobbyMatch(autoStart: false, minPlayers: 2, maxPlayers: 2, hostPlayerId: "host");
        state = ExpectOk(_engine.AddOrReconnectPlayer(state, "host"));
        state = ExpectOk(_engine.AddOrReconnectPlayer(state, "p2"));
        state = ExpectOk(_engine.SetReady(state, "host", true));

        var result = _engine.StartMatch(state, requestingPlayerId: "host");
        Assert.IsFalse(result.Success);
        Assert.AreEqual("NotReady", result.Error);
    }

    [TestMethod]
    public void AutoStart_WhenAllReady_TransitionsToInProgress()
    {
        var settings = new MatchSettings(
            MapId: "test-map",
            MinPlayers: 2,
            MaxPlayers: 2,
            AutoStart: true,
            TurnTimeLimitSeconds: 60,
            DisconnectGraceSeconds: 120);
        var state = MatchState.CreateNew("game-1", settings, hostPlayerId: "p1");

        state = ExpectOk(_engine.AddOrReconnectPlayer(state, "p1"));
        state = ExpectOk(_engine.AddOrReconnectPlayer(state, "p2"));
        Assert.AreEqual(MatchPhases.Lobby, state.Phase);

        state = ExpectOk(_engine.SetReady(state, "p1", true));
        Assert.AreEqual(MatchPhases.Lobby, state.Phase);

        state = ExpectOk(_engine.SetReady(state, "p2", true));
        Assert.AreEqual(MatchPhases.InProgress, state.Phase);
        Assert.IsTrue(state.Turns.Started);
        Assert.IsNotNull(state.Turns.CurrentPlayerId);
    }

    [TestMethod]
    public void ReconnectAllowed_MidMatch_NewJoinRejected()
    {
        var state = CreateLobbyMatch(autoStart: false, minPlayers: 2, maxPlayers: 3, hostPlayerId: "p1");
        state = ExpectOk(_engine.AddOrReconnectPlayer(state, "p1"));
        state = ExpectOk(_engine.AddOrReconnectPlayer(state, "p2"));
        state = ExpectOk(_engine.SetReady(state, "p1", true));
        state = ExpectOk(_engine.SetReady(state, "p2", true));
        state = ExpectOk(_engine.StartMatch(state, "p1"));
        Assert.AreEqual(MatchPhases.InProgress, state.Phase);

        state = ExpectOk(_engine.SetConnected(state, "p2", isConnected: false));
        var reconnect = _engine.AddOrReconnectPlayer(state, "p2");
        state = ExpectOk(reconnect);
        Assert.IsTrue(state.Players["p2"].IsConnected);

        var newJoin = _engine.AddOrReconnectPlayer(state, "p3");
        Assert.IsFalse(newJoin.Success);
        Assert.AreEqual("MatchAlreadyStarted", newJoin.Error);
    }

    [TestMethod]
    public void Tick_WhenTurnExpired_AutoEndTurn()
    {
        var settings = new MatchSettings(
            MapId: "test-map",
            MinPlayers: 2,
            MaxPlayers: 2,
            AutoStart: false,
            TurnTimeLimitSeconds: 5,
            DisconnectGraceSeconds: 120);
        var state = MatchState.CreateNew("game-1", settings, hostPlayerId: "p1");
        state = ExpectOk(_engine.AddOrReconnectPlayer(state, "p1"));
        state = ExpectOk(_engine.AddOrReconnectPlayer(state, "p2"));
        state = ExpectOk(_engine.SetReady(state, "p1", true));
        state = ExpectOk(_engine.SetReady(state, "p2", true));
        state = ExpectOk(_engine.StartMatch(state, "p1"));

        var now = 100L;
        state = state with { TurnEndsAtUnixSeconds = now - 1 };

        var tick = ExpectOk(_engine.Tick(state, now));
        Assert.IsInstanceOfType<EndTurnActionDto>(tick.LastAction);
        var endTurn = (EndTurnActionDto)tick.LastAction!;
        StringAssert.StartsWith(endTurn.ActionId, "sys:endTurn:");
        Assert.IsNotNull(tick.TurnEndsAtUnixSeconds);
        Assert.IsTrue(tick.TurnEndsAtUnixSeconds > now);
    }

    [TestMethod]
    public void Tick_WhenDisconnectGraceExceeded_RemovesPlayer()
    {
        var settings = new MatchSettings(
            MapId: "test-map",
            MinPlayers: 2,
            MaxPlayers: 3,
            AutoStart: false,
            TurnTimeLimitSeconds: 60,
            DisconnectGraceSeconds: 10);
        var state = MatchState.CreateNew("game-1", settings, hostPlayerId: "p1");
        state = ExpectOk(_engine.AddOrReconnectPlayer(state, "p1"));
        state = ExpectOk(_engine.AddOrReconnectPlayer(state, "p2"));
        state = ExpectOk(_engine.SetReady(state, "p1", true));
        state = ExpectOk(_engine.SetReady(state, "p2", true));
        state = ExpectOk(_engine.StartMatch(state, "p1"));

        state = ExpectOk(_engine.SetConnected(state, "p2", isConnected: false));
        state = ExpectOk(_engine.Tick(state, nowUnixSeconds: 100));
        Assert.IsTrue(state.Players.ContainsKey("p2"));

        state = ExpectOk(_engine.Tick(state, nowUnixSeconds: 111));
        Assert.IsFalse(state.Players.ContainsKey("p2"));
    }

    private static MatchState CreateLobbyMatch(
        bool autoStart,
        int minPlayers = 1,
        int maxPlayers = 2,
        string hostPlayerId = "p1")
    {
        var settings = new MatchSettings(
            MapId: "test-map",
            MinPlayers: minPlayers,
            MaxPlayers: maxPlayers,
            AutoStart: autoStart,
            TurnTimeLimitSeconds: 60,
            DisconnectGraceSeconds: 120);
        return MatchState.CreateNew("game-1", settings, hostPlayerId);
    }

    private static MatchState ExpectOk(EngineResult<MatchState> result)
    {
        Assert.IsTrue(result.Success, result.Error);
        return result.State;
    }
}
