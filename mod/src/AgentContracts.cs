using VintageStoryAgent.Protocol;

namespace VintageStoryAgent;

public interface IAgentStateProvider
{
    ValueTask<AgentStateResult> GetStateAsync(CancellationToken cancellationToken);
}

public interface IAgentStateReader
{
    AgentStateResult ReadState();
}

public interface IMainThreadDispatcher
{
    ValueTask<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken);
}
