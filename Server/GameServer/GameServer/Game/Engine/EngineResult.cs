namespace GameServer.Game.Engine;

public readonly record struct EngineResult<T>(bool Success, T State, string? Error)
{
    public static EngineResult<T> Ok(T state) => new(true, state, null);
    public static EngineResult<T> Fail(T state, string error) => new(false, state, error);
}

