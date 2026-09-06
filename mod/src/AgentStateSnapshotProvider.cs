using VintageStoryAgent.Protocol;

namespace VintageStoryAgent;

public sealed class AgentStateSnapshotProvider : IAgentStateProvider
{
    private readonly IMainThreadDispatcher dispatcher;
    private readonly IAgentStateReader reader;

    public AgentStateSnapshotProvider(IMainThreadDispatcher dispatcher, IAgentStateReader reader)
    {
        this.dispatcher = dispatcher;
        this.reader = reader;
    }

    public ValueTask<AgentStateResult> GetStateAsync(CancellationToken cancellationToken) =>
        dispatcher.InvokeAsync(reader.ReadState, cancellationToken);
}
