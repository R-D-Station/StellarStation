using System;

namespace Shared.World.Blocks
{
    /// <summary>Поиск точки спавна: первый маркер-блок карты (детерминированно: низший по Y, затем Z, X).</summary>
    public static class BlockWorldSpawn
    {
        /// <summary>Спавн у нижнего-первого маркера (ноги в его блоке, Y — высота); нет маркеров — fallback.</summary>
        public static (float x, float y, float z) Find(BlockGrid grid, Func<ushort, bool> isMarker,
                                                       float fallbackX, float fallbackY, float fallbackZ)
        {
            bool found = false;
            int bestX = 0, bestY = 0, bestZ = 0;

            foreach (var kv in grid.Sections)
            {
                BlockGrid.UnpackKey(kv.Key, out int cx, out int cy, out int cz);
                var s = kv.Value;
                for (int lz = 0; lz < ChunkSection.Size; lz++)
                    for (int ly = 0; ly < ChunkSection.Size; ly++)
                        for (int lx = 0; lx < ChunkSection.Size; lx++)
                        {
                            ushort type = s.GetBlock(ChunkSection.LocalIndex(lx, ly, lz));
                            if (type == 0 || !isMarker(type))
                                continue;

                            int bx = cx * ChunkSection.Size + lx;
                            int by = cy * ChunkSection.Size + ly;
                            int bz = cz * ChunkSection.Size + lz;
                            if (!found || by < bestY || (by == bestY && (bz < bestZ || (bz == bestZ && bx < bestX))))
                            {
                                bestX = bx;
                                bestY = by;
                                bestZ = bz;
                                found = true;
                            }
                        }
            }

            return found ? (bestX + 0.5f, bestY, bestZ + 0.5f) : (fallbackX, fallbackY, fallbackZ);
        }
    }
}
