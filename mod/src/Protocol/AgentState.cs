namespace VintageStoryAgent.Protocol;

public sealed record AgentState(
    bool Ok,
    PlayerState Player,
    SelectionState Selection
);

public sealed record PlayerState(
    Position3d Position,
    float Yaw,
    float Pitch
);

public sealed record Position3d(double X, double Y, double Z);

public sealed record SelectionState(BlockSelectionState? Block);

public sealed record BlockSelectionState(BlockPosition Position, string Code);

public sealed record BlockPosition(int X, int Y, int Z);

public sealed record AgentError(bool Ok, string Code)
{
    public static AgentError PlayerUnavailable { get; } = new(false, "player_unavailable");
    public static AgentError StateTimeout { get; } = new(false, "state_timeout");
    public static AgentError NotFound { get; } = new(false, "not_found");
    public static AgentError MethodNotAllowed { get; } = new(false, "method_not_allowed");
    public static AgentError ExecutionError { get; } = new(false, "execution_error");
}

public readonly record struct AgentStateResult(AgentState? State, AgentError? Error)
{
    public bool IsSuccess => State is not null;

    public static AgentStateResult Success(AgentState state) => new(state, null);
    public static AgentStateResult Failure(AgentError error) => new(null, error);
}
