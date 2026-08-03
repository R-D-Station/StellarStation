using System.Collections.Generic;
using Shared.World.Blocks;

namespace ServerTests.Shared.World.Blocks
{
    /// <summary>BlockScan: детерминированный порядок обхода (фундамент стабильности LiftId) и фильтр по типам.</summary>
    public class BlockScanTests
    {
        private const ushort Wanted = 3;
        private const ushort Other = 7;

        private static List<(int x, int y, int z)> Run(BlockGrid grid, params ushort[] types)
        {
            var seen = new List<(int, int, int)>();
            BlockScan.ForEach(grid, new HashSet<ushort>(types), (x, y, z, t, s) => seen.Add((x, y, z)));
            return seen;
        }

        private static BlockGrid Spread()
        {
            var g = new BlockGrid();
            g.SetBlock(40, 40, 40, Wanted);
            g.SetBlock(2, 2, 2, Wanted);
            g.SetBlock(20, 4, 33, Wanted);
            g.SetBlock(33, 20, 4, Wanted);
            g.SetBlock(4, 33, 20, Wanted);
            return g;
        }

        [Fact]
        public void TraversalOrder_IsStableAcrossRuns()
        {
            var first = Run(Spread(), Wanted);
            var second = Run(Spread(), Wanted);

            Assert.Equal(5, first.Count);
            Assert.Equal(first, second);
        }

        [Fact]
        public void TraversalOrder_IsSectionYThenZThenX()
        {
            var seen = Run(Spread(), Wanted);

            for (int i = 1; i < seen.Count; i++)
            {
                var a = seen[i - 1];
                var b = seen[i];
                (int sy, int sz, int sx) ka = (a.y >> 4, a.z >> 4, a.x >> 4);
                (int sy, int sz, int sx) kb = (b.y >> 4, b.z >> 4, b.x >> 4);
                Assert.True(Compare(ka, kb) <= 0, $"секции не по (Y,Z,X): {ka} перед {kb}");
            }
        }

        private static int Compare((int sy, int sz, int sx) a, (int sy, int sz, int sx) b)
        {
            if (a.sy != b.sy) return a.sy.CompareTo(b.sy);
            if (a.sz != b.sz) return a.sz.CompareTo(b.sz);
            return a.sx.CompareTo(b.sx);
        }

        [Fact]
        public void Filter_SkipsUnwantedTypes()
        {
            var g = new BlockGrid();
            g.SetBlock(1, 1, 1, Wanted);
            g.SetBlock(2, 1, 1, Other);
            g.SetBlock(3, 1, 1, Wanted);

            var seen = Run(g, Wanted);

            Assert.Equal(2, seen.Count);
            Assert.DoesNotContain((2, 1, 1), seen);
        }

        [Fact]
        public void CarriesTypeAndState()
        {
            var g = new BlockGrid();
            g.SetBlock(5, 6, 7, Wanted);
            g.SetState(5, 6, 7, BlockState.WithFacing(0, 2));

            ushort gotType = 0;
            byte gotState = 0;
            BlockScan.ForEach(g, new HashSet<ushort> { Wanted }, (x, y, z, t, s) => { gotType = t; gotState = s; });

            Assert.Equal(Wanted, gotType);
            Assert.Equal(2, BlockState.GetFacing(gotState));
        }

        [Fact]
        public void EmptyGridAndNullArgs_DoNotThrow()
        {
            Assert.Equal(0, BlockScan.ForEach(new BlockGrid(), new HashSet<ushort> { Wanted }, (x, y, z, t, s) => { }));
            Assert.Equal(0, BlockScan.ForEach(null, new HashSet<ushort> { Wanted }, (x, y, z, t, s) => { }));
            Assert.Equal(0, BlockScan.ForEach(new BlockGrid(), null, (x, y, z, t, s) => { }));
            Assert.Equal(0, BlockScan.ForEach(new BlockGrid(), new HashSet<ushort>(), (x, y, z, t, s) => { }));
        }
    }
}
