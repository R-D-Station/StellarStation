using Shared.World.Blocks;

namespace ServerTests.Shared.World.Blocks
{
    public class BlockInfoFacingTests
    {
        private static BlockInfo PlateAtZPlus()
            => new BlockInfo(900, "Plate", BlockCategory.Generic,
                BlockFaceFlags.None, BlockFaceFlags.None, 0,
                new[] { new BlockBox(0, 0, 15, 16, 16, 16) });

        [Fact]
        public void Facing0_PlateStaysAtZPlus()
        {
            var b = PlateAtZPlus().GetBoxes(0, 0)[0];
            Assert.Equal((15, 16), (b.MinZ, b.MaxZ));
            Assert.Equal((0, 16), (b.MinX, b.MaxX));
        }

        [Fact]
        public void Facing1_PlateMovesToXPlus()
        {
            var b = PlateAtZPlus().GetBoxes(0, 1)[0];
            Assert.Equal((15, 16), (b.MinX, b.MaxX));
            Assert.Equal((0, 16), (b.MinZ, b.MaxZ));
        }

        [Fact]
        public void Facing2_PlateMovesToZMinus()
        {
            var b = PlateAtZPlus().GetBoxes(0, 2)[0];
            Assert.Equal((0, 1), (b.MinZ, b.MaxZ));
            Assert.Equal((0, 16), (b.MinX, b.MaxX));
        }

        [Fact]
        public void Facing3_PlateMovesToXMinus()
        {
            var b = PlateAtZPlus().GetBoxes(0, 3)[0];
            Assert.Equal((0, 1), (b.MinX, b.MaxX));
            Assert.Equal((0, 16), (b.MinZ, b.MaxZ));
        }

        [Fact]
        public void ShapesPath_UsesFacingFromState()
        {
            var info = PlateAtZPlus();
            byte state = BlockState.WithFacing(0, 2);
            var b = info.GetBoxes(BlockState.GetPart(state), BlockState.GetFacing(state), BlockState.GetOpen(state))[0];
            Assert.Equal((0, 1), (b.MinZ, b.MaxZ));
        }
    }
}
