using Server.Doors;
using Shared.Messages.Interaction;
using Shared.World.Blocks;

namespace Server.Network.Interaction
{
    public sealed class DoorHandler : IInteractionHandler
    {
        private readonly GameServer _server;
        private readonly IDoorCommands _doors;

        public DoorHandler(GameServer server, IDoorCommands? doors = null)
        {
            _server = server;
            _doors = doors ?? server.Doors;
        }

        public bool TryHandle(in InteractContext ctx)
        {
            if (ctx.Verb != (byte)InteractVerb.Primary)
                return false;

            var grid = _server.BlockWorld;
            if (grid == null)
                return false;

            int bx = ctx.TileX, by = ctx.TileZ, bz = ctx.TileY;

            ushort type = grid.GetBlock(bx, by, bz);
            if (type == 0)
                return false;

            var info = BlockCatalog.Get(type);
            if (!info.Openable || info.Opening != DoorOpening.Interact)
                return false;

            if (!_doors.TryGetAnchorKey(bx, by, bz, out long key))
                return false;

            var result = _doors.TrySetOpen(key, !_doors.IsOpen(key));
            return result == DoorCommandResult.Applied || result == DoorCommandResult.BlockedByOccupant;
        }
    }
}
