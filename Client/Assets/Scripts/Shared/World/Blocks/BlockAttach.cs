using System;
using System.Collections.Generic;

namespace Shared.World.Blocks
{
    public static class BlockAttach
    {
        private static readonly int[] WDX = { 1, -1, 0, 0 };
        private static readonly int[] WDZ = { 0, 0, 1, -1 };
        private static readonly int[] WFacing = { 3, 1, 2, 0 };

        public static bool DefaultIsSolid(ushort type)
        {
            if (type == 0)
                return false;
            var info = BlockCatalog.Get(type);
            return info.HasCollision && !info.Openable;
        }

        public static bool Resolve(BlockGrid grid, Func<ushort, bool> isSolid, int x, int y, int z,
                                   AttachSurface[] priority, out AttachSurface surface, out int facing)
        {
            surface = AttachSurface.Floor;
            facing = 0;
            if (grid == null || isSolid == null || priority == null)
                return false;

            for (int i = 0; i < priority.Length; i++)
            {
                switch (priority[i])
                {
                    case AttachSurface.Floor:
                        if (isSolid(grid.GetBlock(x, y - 1, z))) { surface = AttachSurface.Floor; return true; }
                        break;
                    case AttachSurface.Ceiling:
                        if (isSolid(grid.GetBlock(x, y + 1, z))) { surface = AttachSurface.Ceiling; return true; }
                        break;
                    case AttachSurface.Wall:
                        for (int d = 0; d < 4; d++)
                            if (isSolid(grid.GetBlock(x + WDX[d], y, z + WDZ[d])))
                            {
                                surface = AttachSurface.Wall;
                                facing = WFacing[d];
                                return true;
                            }
                        break;
                    case AttachSurface.AnySolid:
                        for (int d = 0; d < 4; d++)
                            if (isSolid(grid.GetBlock(x + WDX[d], y, z + WDZ[d])))
                            {
                                surface = AttachSurface.AnySolid;
                                facing = WFacing[d];
                                return true;
                            }
                        if (isSolid(grid.GetBlock(x, y - 1, z))) { surface = AttachSurface.AnySolid; return true; }
                        if (isSolid(grid.GetBlock(x, y + 1, z))) { surface = AttachSurface.AnySolid; return true; }
                        break;
                }
            }
            return false;
        }

        private static readonly Func<ushort, bool> CatalogRequiresSupport = t => BlockCatalog.Get(t).RequiresSupport;
        private static readonly Func<ushort, AttachSurface[]> CatalogAttachTo = t => BlockCatalog.Get(t).AttachTo;

        public static int ValidateAll(BlockGrid grid, Func<ushort, bool> isSolid)
            => ValidateAll(grid, isSolid, CatalogRequiresSupport, CatalogAttachTo);

        public static int ValidateAll(BlockGrid grid, Func<ushort, bool> isSolid,
                                      Func<ushort, bool> requiresSupport, Func<ushort, AttachSurface[]> attachTo)
        {
            if (grid == null || isSolid == null || requiresSupport == null || attachTo == null)
                return 0;

            int removed = 0;
            var keys = new List<long>();
            var toRemove = new List<(int x, int y, int z)>();
            bool changed = true;
            while (changed)
            {
                changed = false;
                keys.Clear();
                keys.AddRange(grid.Sections.Keys);
                keys.Sort(CompareKeys);
                toRemove.Clear();

                foreach (long key in keys)
                {
                    var s = grid.Sections[key];
                    BlockGrid.UnpackKey(key, out int cx, out int cy, out int cz);
                    for (int li = 0; li < ChunkSection.BlockCount; li++)
                    {
                        ushort t = s.GetBlock(li);
                        if (t == 0 || !requiresSupport(t))
                            continue;
                        int x = cx * 16 + (li & 15);
                        int y = cy * 16 + ((li >> 4) & 15);
                        int z = cz * 16 + ((li >> 8) & 15);
                        if (!Resolve(grid, isSolid, x, y, z, attachTo(t), out _, out _))
                            toRemove.Add((x, y, z));
                    }
                }

                for (int i = 0; i < toRemove.Count; i++)
                {
                    var p = toRemove[i];
                    if (grid.SetBlock(p.x, p.y, p.z, 0))
                    {
                        removed++;
                        changed = true;
                    }
                }
            }
            return removed;
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
