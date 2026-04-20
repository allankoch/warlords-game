using System.Text.Json.Serialization;

namespace GameServer.Protocol;

// Polymorphic action envelope for SignalR. The Hub forwards actions; game rules live behind IGameSessionService.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(EndTurnActionDto), "endTurn")]
[JsonDerivedType(typeof(MoveEntityActionDto), "move")]
[JsonDerivedType(typeof(AttackEntityActionDto), "attack")]
public abstract record PlayerActionDto
{
    public required string ActionId { get; init; }

    // Client-provided monotonic sequence per player/connection (helps reject out-of-order messages).
    public int ClientSequence { get; init; }

    // Optional optimistic concurrency check against the last known authoritative state version.
    public int? ExpectedStateVersion { get; init; }
}

public sealed record EndTurnActionDto : PlayerActionDto;

public sealed record MoveEntityActionDto : PlayerActionDto
{
    public required string EntityId { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
}

public sealed record AttackEntityActionDto : PlayerActionDto
{
    public required string EntityId { get; init; }
    public required string TargetEntityId { get; init; }
}
