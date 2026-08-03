using System.Collections.Generic;
using Shared.World.Blocks;

namespace Shared.World.Atmos
{
    public static class AtmosInit
    {
        public static void Classify(BlockGrid grid, AtmosGrid atmos)
        {
            var keys = new List<long>(grid.Sections.Keys);
            keys.Sort();

            var queue = new Queue<long>();

            for (int k = 0; k < keys.Count; k++)
            {
                BlockGrid.UnpackKey(keys[k], out int cx, out int cy, out int cz);
                int ox = cx * GasSection.Size, oy = cy * GasSection.Size, oz = cz * GasSection.Size;

                for (int ly = 0; ly < GasSection.Size; ly++)
                    for (int lz = 0; lz < GasSection.Size; lz++)
                        for (int lx = 0; lx < GasSection.Size; lx++)
                        {
                            int x = ox + lx, y = oy + ly, z = oz + lz;
                            if (AtmosBlocks.IsAirtight(grid, x, y, z)) continue;
                            if (!TouchesUnloaded(grid, x, y, z)) continue;
                            if (atmos.IsSpace(x, y, z)) continue;

                            atmos.MarkSpace(x, y, z);
                            queue.Enqueue(AtmosCell.Pack(x, y, z));
                        }
            }

            while (queue.Count > 0)
            {
                AtmosCell.Unpack(queue.Dequeue(), out int x, out int y, out int z);

                for (int n = 0; n < AtmosCell.NeighborCount; n++)
                {
                    int nx = x + AtmosCell.NeighborDx[n], ny = y + AtmosCell.NeighborDy[n], nz = z + AtmosCell.NeighborDz[n];
                    if (!IsLoaded(grid, nx, ny, nz)) continue;
                    if (AtmosBlocks.IsAirtight(grid, nx, ny, nz)) continue;
                    if (atmos.IsSpace(nx, ny, nz)) continue;

                    atmos.MarkSpace(nx, ny, nz);
                    queue.Enqueue(AtmosCell.Pack(nx, ny, nz));
                }
            }

            var standard = GasMix.Standard;
            var vacuum = GasMix.Vacuum;

            for (int k = 0; k < keys.Count; k++)
            {
                BlockGrid.UnpackKey(keys[k], out int cx, out int cy, out int cz);
                int ox = cx * GasSection.Size, oy = cy * GasSection.Size, oz = cz * GasSection.Size;

                for (int ly = 0; ly < GasSection.Size; ly++)
                    for (int lz = 0; lz < GasSection.Size; lz++)
                        for (int lx = 0; lx < GasSection.Size; lx++)
                        {
                            int x = ox + lx, y = oy + ly, z = oz + lz;
                            bool airtight = AtmosBlocks.IsAirtight(grid, x, y, z);
                            bool space = atmos.IsSpace(x, y, z);
                            atmos.SetMix(x, y, z, airtight || space ? vacuum : standard);
                        }
            }
        }

        private static bool IsLoaded(BlockGrid grid, int x, int y, int z)
            => grid.GetSection(AtmosGrid.SectionCoord(x), AtmosGrid.SectionCoord(y), AtmosGrid.SectionCoord(z)) != null;

        private static bool TouchesUnloaded(BlockGrid grid, int x, int y, int z)
        {
            for (int n = 0; n < AtmosCell.NeighborCount; n++)
                if (!IsLoaded(grid, x + AtmosCell.NeighborDx[n], y + AtmosCell.NeighborDy[n], z + AtmosCell.NeighborDz[n]))
                    return true;
            return false;
        }
    }
}
