using System.Collections.Generic;

namespace Shared.World.Blocks
{
    public delegate void CellVisitor(int x, int y, int z, ushort type, byte state);

    public static class BlockScan
    {
        public static int ForEach(BlockGrid grid, HashSet<ushort> types, CellVisitor visit)
        {
            if (grid == null || types == null || types.Count == 0 || visit == null)
                return 0;

            var keys = new List<long>(grid.Sections.Keys);
            keys.Sort(CompareKeys);

            int visited = 0;
            foreach (long key in keys)
            {
                if (!grid.Sections.TryGetValue(key, out var section))
                    continue;
                BlockGrid.UnpackKey(key, out int cx, out int cy, out int cz);
                for (int li = 0; li < ChunkSection.BlockCount; li++)
                {
                    ushort type = section.GetBlock(li);
                    if (type == 0 || !types.Contains(type))
                        continue;
                    int x = cx * ChunkSection.Size + (li & 15);
                    int y = cy * ChunkSection.Size + ((li >> 4) & 15);
                    int z = cz * ChunkSection.Size + ((li >> 8) & 15);
                    visit(x, y, z, type, section.GetState(li));
                    visited++;
                }
            }
            return visited;
        }

        private static int CompareKeys(long a, long b)
        {
            BlockGrid.UnpackKey(a, out int ax, out int ay, out int az);
            BlockGrid.UnpackKey(b, out int bx, out int by, out int bz);
            int c = ay.CompareTo(by);
            if (c != 0)
                return c;
            c = az.CompareTo(bz);
            return c != 0 ? c : ax.CompareTo(bx);
        }
    }
}
