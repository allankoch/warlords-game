using GameServer.Game.Engine;
using GameServer.Protocol;
using GameServer.Tests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GameServer.Tests.Engine;

[TestClass]
public sealed class GameEngineRulesTests
{
    private readonly IGameEngine _engine = new GameEngine(new TestMapProvider());

    [TestMethod]
    public void NotYourTurn_IsRejected()
    {
        var state = CreateStartedTwoPlayerMatch(out var p1, out var p2);

        Assert.AreEqual(p1, state.Turns.CurrentPlayerId);

        var result = _engine.ApplyAction(state, p2, new EndTurnActionDto { ActionId = "a1", ClientSequence = 1 });
        Assert.IsFalse(result.Success);
        Assert.AreEqual("NotYourTurn", result.Error);
    }

    [TestMethod]
    public void EndTurn_RotatesTurnOrder()
    {
        var state = CreateStartedTwoPlayerMatch(out var p1, out var p2);

        var r1 = _engine.ApplyAction(state, p1, new EndTurnActionDto { ActionId = "a1", ClientSequence = 1 });
        state = ExpectOk(r1);
        Assert.AreEqual(p2, state.Turns.CurrentPlayerId);
        Assert.AreEqual(2, state.Turns.TurnNumber);

        var r2 = _engine.ApplyAction(state, p2, new EndTurnActionDto { ActionId = "a2", ClientSequence = 1 });
        state = ExpectOk(r2);
        Assert.AreEqual(p1, state.Turns.CurrentPlayerId);
        Assert.AreEqual(3, state.Turns.TurnNumber);
    }

    [TestMethod]
    public void Disconnect_CurrentTurnAutoAdvances()
    {
        var state = CreateStartedTwoPlayerMatch(out var p1, out var p2);
        Assert.AreEqual(p1, state.Turns.CurrentPlayerId);

        var r = _engine.SetConnected(state, p1, isConnected: false);
        state = ExpectOk(r);

        Assert.AreEqual(p2, state.Turns.CurrentPlayerId);
    }

    [TestMethod]
    public void MoveValidation_OutOfBounds_IsRejected()
    {
        var state = CreateStartedTwoPlayerMatch(out var p1, out _);

        var r = _engine.ApplyAction(state, p1, new MoveEntityActionDto
        {
            ActionId = "m1",
            ClientSequence = 1,
            EntityId = $"unit-{p1}",
            X = -1,
            Y = 0
        });

        Assert.IsFalse(r.Success);
        Assert.AreEqual("OutOfBounds", r.Error);
    }

    [TestMethod]
    public void MoveValidation_InvalidMoveRange_IsRejected()
    {
        var state = CreateStartedTwoPlayerMatch(out var p1, out _);

        var r = _engine.ApplyAction(state, p1, new MoveEntityActionDto
        {
            ActionId = "m1",
            ClientSequence = 1,
            EntityId = $"unit-{p1}",
            X = 0,
            Y = 2
        });

        Assert.IsFalse(r.Success);
        Assert.AreEqual("InvalidMoveRange", r.Error);
    }

    [TestMethod]
    public void MoveValidation_CellOccupied_IsRejected()
    {
        var state = CreateStartedTwoPlayerMatch(out var p1, out var p2);

        var r = _engine.ApplyAction(state, p1, new MoveEntityActionDto
        {
            ActionId = "m1",
            ClientSequence = 1,
            EntityId = $"unit-{p1}",
            X = 1,
            Y = 0
        });

        Assert.IsFalse(r.Success);
        Assert.AreEqual("CellOccupied", r.Error);
    }

    [TestMethod]
    public void MoveValidation_NotEntityOwner_IsRejected()
    {
        var state = CreateStartedTwoPlayerMatch(out var p1, out var p2);

        var end = _engine.ApplyAction(state, p1, new EndTurnActionDto { ActionId = "a1", ClientSequence = 1 });
        state = ExpectOk(end);
        Assert.AreEqual(p2, state.Turns.CurrentPlayerId);

        var r = _engine.ApplyAction(state, p2, new MoveEntityActionDto
        {
            ActionId = "m1",
            ClientSequence = 1,
            EntityId = $"unit-{p1}",
            X = 0,
            Y = 1
        });

        Assert.IsFalse(r.Success);
        Assert.AreEqual("NotEntityOwner", r.Error);
    }

    [TestMethod]
    public void MoveValidation_UnknownEntity_IsRejected()
    {
        var state = CreateStartedTwoPlayerMatch(out var p1, out _);

        var r = _engine.ApplyAction(state, p1, new MoveEntityActionDto
        {
            ActionId = "m1",
            ClientSequence = 1,
            EntityId = "does-not-exist",
            X = 0,
            Y = 1
        });

        Assert.IsFalse(r.Success);
        Assert.AreEqual("UnknownEntity", r.Error);
    }

    private MatchState CreateStartedTwoPlayerMatch(out string player1Id, out string player2Id)
    {
        player1Id = "p1";
        player2Id = "p2";

        var settings = new MatchSettings(
            MapId: "test-map",
            MinPlayers: 2,
            MaxPlayers: 2,
            AutoStart: false,
            TurnTimeLimitSeconds: 60,
            DisconnectGraceSeconds: 120);
        var state = MatchState.CreateNew("game-1", settings, hostPlayerId: player1Id);

        state = ExpectOk(_engine.AddOrReconnectPlayer(state, player1Id));
        state = ExpectOk(_engine.AddOrReconnectPlayer(state, player2Id));

        state = ExpectOk(_engine.SetReady(state, player1Id, true));
        state = ExpectOk(_engine.SetReady(state, player2Id, true));

        state = ExpectOk(_engine.StartMatch(state, requestingPlayerId: player1Id));
        Assert.AreEqual(MatchPhases.InProgress, state.Phase);

        return state;
    }

    private static MatchState ExpectOk(EngineResult<MatchState> result)
    {
        Assert.IsTrue(result.Success, result.Error);
        return result.State;
    }
}
