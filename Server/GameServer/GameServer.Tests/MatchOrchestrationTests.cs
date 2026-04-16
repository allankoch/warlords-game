using GameServer.Game;
using GameServer.Game.Engine;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NSubstitute;
using GameServer.Protocol;

namespace GameServer.Tests;

[TestClass]
public sealed class MatchOrchestrationTests
{
    [TestMethod]
    public void Match_DelegatesToEngine_AndIncrementsVersion()
    {
        var engine = Substitute.For<IGameEngine>();
        var settings = new MatchSettings(
            MapId: "test-map",
            MinPlayers: 1,
            MaxPlayers: 2,
            AutoStart: false,
            TurnTimeLimitSeconds: 60,
            DisconnectGraceSeconds: 120);

        engine
            .AddOrReconnectPlayer(Arg.Any<MatchState>(), "p1")
            .Returns(callInfo =>
            {
                var input = callInfo.Arg<MatchState>();
                var updated = input with
                {
                    Players = input.Players.SetItem("p1", new PlayerPresenceDto("p1", true)),
                    Turns = input.Turns.EnsurePlayerInOrder("p1")
                };
                return EngineResult<MatchState>.Ok(updated);
            });

        var match = new Match("game-1", settings, "host", engine);
        Assert.AreEqual(0, match.Version);

        match.AddOrReconnectPlayer("p1");

        engine.Received(1).AddOrReconnectPlayer(Arg.Is<MatchState>(s => s.GameId == "game-1"), "p1");
        Assert.AreEqual(1, match.Version);
    }
}
