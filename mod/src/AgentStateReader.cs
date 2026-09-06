using Vintagestory.API.Client;
using Vintagestory.API.Common;
using VintageStoryAgent.Protocol;

namespace VintageStoryAgent;

public sealed class AgentStateReader : IAgentStateReader
{
    private readonly ICoreClientAPI api;

    public AgentStateReader(ICoreClientAPI api)
    {
        this.api = api;
    }

    public AgentStateResult ReadState()
    {
        IClientPlayer? player = api.World?.Player;
        EntityPlayer? entity = player?.Entity;
        if (player is null || entity is null)
        {
            return AgentStateResult.Failure(AgentError.PlayerUnavailable);
        }

        var pos = entity.Pos;
        BlockSelectionState? selectedBlock = ReadBlockSelection(player.CurrentBlockSelection);

        return AgentStateResult.Success(new AgentState(
            Ok: true,
            Player: new PlayerState(
                Position: new Position3d(pos.X, pos.Y, pos.Z),
                Yaw: player.CameraYaw,
                Pitch: player.CameraPitch),
            Selection: new SelectionState(selectedBlock)));
    }

    private BlockSelectionState? ReadBlockSelection(BlockSelection? selection)
    {
        if (selection?.Position is null) return null;

        var position = selection.Position;
        Block block = api.World.BlockAccessor.GetBlock(position);
        string code = block.Code?.ToString() ?? "game:air";

        return new BlockSelectionState(
            Position: new BlockPosition(position.X, position.Y, position.Z),
            Code: code);
    }
}
