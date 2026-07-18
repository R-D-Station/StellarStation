using System;
using Shared.World.Blocks;

namespace ServerTests.Shared.World.Blocks
{
    public class BlockAttachTests
    {
        private const ushort SolidT = 1;
        private const ushort ShelfT = 2;
        private const ushort StackA = 3;

        private static readonly Func<ushort, bool> IsSolid = t => t == SolidT;

        private static AttachSurface[] P(params AttachSurface[] s) => s;

        [Fact]
        public void Floor_SolidBelow_NoFacing()
        {
            var g = new BlockGrid();
            g.SetBlock(5, 4, 5, SolidT);
            bool ok = BlockAttach.Resolve(g, IsSolid, 5, 5, 5, P(AttachSurface.Floor), out var surf, out int facing);
            Assert.True(ok);
            Assert.Equal(AttachSurface.Floor, surf);
            Assert.Equal(0, facing);
        }

        [Fact]
        public void Ceiling_SolidAbove_NoFacing()
        {
            var g = new BlockGrid();
            g.SetBlock(5, 6, 5, SolidT);
            bool ok = BlockAttach.Resolve(g, IsSolid, 5, 5, 5, P(AttachSurface.Ceiling), out var surf, out int facing);
            Assert.True(ok);
            Assert.Equal(AttachSurface.Ceiling, surf);
            Assert.Equal(0, facing);
        }

        [Theory]
        [InlineData(1, 0, 3)]
        [InlineData(-1, 0, 1)]
        [InlineData(0, 1, 2)]
        [InlineData(0, -1, 0)]
        public void Wall_FacingAwayFromWall(int dx, int dz, int expectedFacing)
        {
            var g = new BlockGrid();
            g.SetBlock(5 + dx, 5, 5 + dz, SolidT);
            bool ok = BlockAttach.Resolve(g, IsSolid, 5, 5, 5, P(AttachSurface.Wall), out var surf, out int facing);
            Assert.True(ok);
            Assert.Equal(AttachSurface.Wall, surf);
            Assert.Equal(expectedFacing, facing);
        }

        [Fact]
        public void Priority_WallBeforeFloor_WhenBothSolid()
        {
            var g = new BlockGrid();
            g.SetBlock(5, 4, 5, SolidT);      // floor
            g.SetBlock(6, 5, 5, SolidT);      // wall +X
            bool ok = BlockAttach.Resolve(g, IsSolid, 5, 5, 5, P(AttachSurface.Wall, AttachSurface.Floor),
                                          out var surf, out int facing);
            Assert.True(ok);
            Assert.Equal(AttachSurface.Wall, surf);
            Assert.Equal(3, facing);
        }

        [Fact]
        public void AnySolid_HorizontalGivesFacing_VerticalDoesNot()
        {
            var wall = new BlockGrid();
            wall.SetBlock(4, 5, 5, SolidT);   // −X
            Assert.True(BlockAttach.Resolve(wall, IsSolid, 5, 5, 5, P(AttachSurface.AnySolid), out var s1, out int f1));
            Assert.Equal(AttachSurface.AnySolid, s1);
            Assert.Equal(1, f1);

            var floor = new BlockGrid();
            floor.SetBlock(5, 4, 5, SolidT);  // below
            Assert.True(BlockAttach.Resolve(floor, IsSolid, 5, 5, 5, P(AttachSurface.AnySolid), out var s2, out int f2));
            Assert.Equal(AttachSurface.AnySolid, s2);
            Assert.Equal(0, f2);
        }

        [Fact]
        public void NoSupport_ReturnsFalse()
        {
            var g = new BlockGrid();
            bool ok = BlockAttach.Resolve(g, IsSolid, 5, 5, 5,
                P(AttachSurface.Wall, AttachSurface.Floor, AttachSurface.Ceiling, AttachSurface.AnySolid),
                out _, out _);
            Assert.False(ok);
        }

        [Fact]
        public void Wall_ScanOrderDeterministic_XBeforeZ()
        {
            var g = new BlockGrid();
            g.SetBlock(6, 5, 5, SolidT);      // +X → facing 3
            g.SetBlock(5, 5, 6, SolidT);      // +Z → facing 2
            BlockAttach.Resolve(g, IsSolid, 5, 5, 5, P(AttachSurface.Wall), out _, out int facing);
            Assert.Equal(3, facing);          // +X scanned first
        }

        [Fact]
        public void ValidateAll_RemovesUnsupported_KeepsSupported()
        {
            var g = new BlockGrid();
            g.SetBlock(5, 4, 5, SolidT);      // floor for the shelf
            g.SetBlock(5, 5, 5, ShelfT);      // supported (floor below)
            g.SetBlock(9, 9, 9, ShelfT);      // unsupported (nothing around)

            int removed = BlockAttach.ValidateAll(g, ShelfIsSolid, ReqShelf, AttachFloor);

            Assert.Equal(1, removed);
            Assert.Equal(ShelfT, g.GetBlock(5, 5, 5));
            Assert.Equal((ushort)0, g.GetBlock(9, 9, 9));
        }

        [Fact]
        public void ValidateAll_CascadeFixpoint()
        {
            var g = new BlockGrid();
            g.SetBlock(5, 4, 5, SolidT);      // real floor
            g.SetBlock(5, 5, 5, StackA);      // A on floor — supported (Floor below)
            g.SetBlock(5, 6, 5, StackA);      // B on A — supported only via A below
            g.SetBlock(5, 7, 5, StackA);      // C on B

            g.SetBlock(5, 5, 5, 0);           // knock out A → B, C lose support in cascade

            int removed = BlockAttach.ValidateAll(g, StackIsSolid, ReqStack, AttachFloor);

            Assert.Equal(2, removed);         // B and C
            Assert.Equal((ushort)0, g.GetBlock(5, 6, 5));
            Assert.Equal((ushort)0, g.GetBlock(5, 7, 5));
            Assert.Equal(SolidT, g.GetBlock(5, 4, 5));
        }

        private static bool ShelfIsSolid(ushort t) => t == SolidT || t == ShelfT;
        private static bool StackIsSolid(ushort t) => t == SolidT || t == StackA;
        private static bool ReqShelf(ushort t) => t == ShelfT;
        private static bool ReqStack(ushort t) => t == StackA;
        private static AttachSurface[] AttachFloor(ushort t) => new[] { AttachSurface.Floor };
    }
}
