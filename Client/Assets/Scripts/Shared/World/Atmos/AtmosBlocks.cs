using Shared.World.Blocks;

namespace Shared.World.Atmos
{
    public static class AtmosBlocks
    {
        public static bool IsAirtight(BlockGrid grid, int x, int y, int z)
        {
            ushort type = grid.GetBlock(x, y, z);
            if (type == 0) return false;

            var info = BlockCatalog.Get(type);
            int part = MultiBlock.PartAt(grid, info.SizeX, info.SizeY, info.SizeZ,
                                         BlockState.GetFacing(grid.GetState(x, y, z)), x, y, z);
            if (part < 0 || !info.PartHasCollision(part)) return false;

            if (info.Openable) return !BlockState.GetOpen(grid.GetState(x, y, z));

            return true;
        }

        public static int AirCellY(BlockGrid grid, int x, int y, int z)
            => IsAirtight(grid, x, y, z) ? y + 1 : y;
    }
}
