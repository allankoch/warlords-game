using GameServer.Protocol;

namespace GameServer.Game.Engine;

public interface IGameEngine
{
    EngineResult<MatchState> AddOrReconnectPlayer(MatchState state, string playerId, string? displayName);
    EngineResult<MatchState> ClaimSeat(MatchState state, string playerId, string? displayName, string seatId);
    EngineResult<MatchState> SetConnected(MatchState state, string playerId, bool isConnected);
    EngineResult<MatchState> RemovePlayer(MatchState state, string playerId);
    EngineResult<MatchState> SetReady(MatchState state, string playerId, bool isReady);
    EngineResult<MatchState> StartMatch(MatchState state, string requestingPlayerId);
    EngineResult<MatchState> ApplyAction(MatchState state, string playerId, PlayerActionDto action);
    EngineResult<MatchState> Tick(MatchState state, long nowUnixSeconds);
    IReadOnlyList<AvailableActionDto> GetAvailableActions(MatchState state);
}
