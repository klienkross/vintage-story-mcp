namespace Vintagestory.API.Client;

public interface ICoreClientAPI
{
    IClientEventAPI Event { get; }
}

public interface IClientEventAPI
{
    void EnqueueMainThreadTask(Action action, string code);
}
