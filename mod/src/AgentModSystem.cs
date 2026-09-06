using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VintageStoryAgent;

public sealed class AgentModSystem : ModSystem
{
    private AgentHttpServer? server;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        var reader = new AgentStateReader(api);
        var dispatcher = new VintageStoryMainThreadDispatcher(api);
        var stateProvider = new AgentStateSnapshotProvider(dispatcher, reader);

        server = new AgentHttpServer(AgentHttpServerOptions.CreateDefault(), stateProvider);
        server.Start();
        Mod.Logger.Notification($"Vintage Story Agent observation bridge listening on {AgentHttpServerOptions.CreateDefault().Prefix}");
    }

    public override void Dispose()
    {
        server?.Dispose();
        server = null;
        base.Dispose();
    }
}
