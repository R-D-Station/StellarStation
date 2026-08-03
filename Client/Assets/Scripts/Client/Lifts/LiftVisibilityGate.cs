using System;
using Shared.World.Blocks;

namespace Client.Lifts
{
    public interface ICellAlphaSource
    {
        float AlphaAt(int x, int y, int z);
    }

    public struct LiftAlphaAccumulator
    {
        private float _max;
        private bool _any;

        public void Add(float alpha)
        {
            if (!_any || alpha > _max)
                _max = alpha;
            _any = true;
        }

        public bool Any => _any;

        public float Result => _any ? _max : 1f;
    }

    public static class LiftVisibilityGate
    {
        public const int MaxRows = 64;

        public static void ColumnRange(float anchorX, float anchorZ, int planW, int planD,
            out int x0, out int x1, out int z0, out int z1)
        {
            int w = planW < 1 ? 1 : planW;
            int d = planD < 1 ? 1 : planD;
            x0 = (int)MathF.Floor(anchorX);
            z0 = (int)MathF.Floor(anchorZ);
            x1 = x0 + w - 1;
            z1 = z0 + d - 1;
        }

        public static void RowRange(float y, float visualHeight, int maxRows, out int lo, out int hi)
        {
            lo = (int)MathF.Floor(y);
            if (!(visualHeight > 0f))
            {
                hi = lo;
                return;
            }

            hi = (int)MathF.Floor(y + visualHeight - 0.001f);
            if (hi < lo)
                hi = lo;

            int cap = maxRows < 1 ? 1 : (maxRows > MaxRows ? MaxRows : maxRows);
            if (hi - lo > cap)
                hi = lo + cap;
        }

        public static bool BlocksSight(ushort type)
            => type != 0 && !BlockCatalog.Get(type).IsMarker;

        public static float Combine(BlockGrid grid, ICellAlphaSource source, bool snapshotValid,
            int x0, int x1, int z0, int z1, int rowLo, int rowHi)
        {
            if (!snapshotValid || grid == null || source == null)
                return 1f;

            var acc = new LiftAlphaAccumulator();
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                    for (int y = rowLo; y <= rowHi; y++)
                    {
                        if (BlocksSight(grid.GetBlock(x, y, z)))
                            continue;
                        acc.Add(source.AlphaAt(x, y, z));
                    }
            return acc.Result;
        }
    }
}
