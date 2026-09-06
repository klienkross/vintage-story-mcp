using Vintagestory.API.Client;

namespace VintageStoryAgent;

public sealed class VintageStoryMainThreadDispatcher : IMainThreadDispatcher
{
    private readonly ICoreClientAPI api;

    public VintageStoryMainThreadDispatcher(ICoreClientAPI api)
    {
        this.api = api;
    }

    public ValueTask<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));

        api.Event.EnqueueMainThreadTask(() =>
        {
            if (completion.Task.IsCompleted)
            {
                registration.Dispose();
                return;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                completion.TrySetResult(action());
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                completion.TrySetCanceled(cancellationToken);
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
            finally
            {
                registration.Dispose();
            }
        }, "vintage-story-agent-state-snapshot");

        return new ValueTask<T>(completion.Task);
    }
}
