using Server.Doors;
using Server.Lifts;
using Shared.Messages.Interaction;
using Shared.World.Blocks;

namespace Server.Network.Interaction
{
    public sealed class LiftCallHandler : IInteractionHandler
    {
        private readonly GameServer _server;
        private readonly LiftSystem _lifts;
        private readonly IDoorCommands _doors;

        public LiftCallHandler(GameServer server, LiftSystem lifts, IDoorCommands doors = null)
        {
            _server = server;
            _lifts = lifts;
            _doors = doors ?? server.Doors;
        }

        public bool TryHandle(in InteractContext ctx)
        {
            if (ctx.Verb != (byte)InteractVerb.Primary || _lifts == null)
                return false;

            var grid = _server.BlockWorld;
            if (grid == null)
                return false;

            int bx = ctx.TileX, by = ctx.TileZ, bz = ctx.TileY;
            ushort type = grid.GetBlock(bx, by, bz);
            if (type == 0)
                return false;

            var info = BlockCatalog.Get(type);
            if (!info.Openable || info.Opening != DoorOpening.External)
                return false;

            if (!_doors.TryGetAnchorKey(bx, by, bz, out long key))
                return false;

            var controller = _lifts.FindByDoorKey(key, out int floor);
            if (controller == null)
                return false;

            controller.Request(floor);
            return true;
        }
    }
}
