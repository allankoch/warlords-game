namespace GameServer.Game.Engine;

public sealed record SeatState(
    string SeatId,
    string? ClaimedByPlayerId,
    string? DisplayName,
    bool IsConnected,
    bool IsReady,
    long? DisconnectedSinceUnixSeconds = null,
    bool IsActive = false)
{
    public bool IsClaimed => !string.IsNullOrWhiteSpace(ClaimedByPlayerId);
}
