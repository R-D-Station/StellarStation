using Shared.World.Blocks;

namespace ServerTests.Shared.World.Blocks
{
    public class ItemGroundSnapTests
    {
        private static BlockGrid Grid() => new BlockGrid();

        [Fact]
        public void AirAboveFullBlock_RestsInCellAboveIt()
        {
            var g = Grid();
            g.SetBlock(0, 1, 0, DevBlockWorld.Full);
            Assert.Equal(2, ItemGroundSnap.SnapDown(g, DevBlockWorld.Shapes, 0, 5, 0));
        }

        [Fact]
        public void AirAboveTopSlab_RestsInCellAboveIt()
        {
            var g = Grid();
            g.SetBlock(0, 0, 0, DevBlockWorld.Slab);
            Assert.Equal(1, ItemGroundSnap.SnapDown(g, DevBlockWorld.Shapes, 0, 5, 0));
        }

        [Fact]
        public void AirAboveHalfStep_RestsInsideItsCell()
        {
            var g = Grid();
            g.SetBlock(0, 1, 0, DevBlockWorld.HalfStep);
            Assert.Equal(1, ItemGroundSnap.SnapDown(g, DevBlockWorld.Shapes, 0, 5, 0));
        }

        [Fact]
        public void AlreadyOnSurface_Unchanged()
        {
            var g = Grid();
            g.SetBlock(0, 0, 0, DevBlockWorld.Slab);
            Assert.Equal(1, ItemGroundSnap.SnapDown(g, DevBlockWorld.Shapes, 0, 1, 0));
        }

        [Fact]
        public void Vacuum_StaysAtStartCell()
        {
            Assert.Equal(5, ItemGroundSnap.SnapDown(Grid(), DevBlockWorld.Shapes, 0, 5, 0));
        }

        [Fact]
        public void StartInsideFullBlock_PopsOnTop()
        {
            var g = Grid();
            g.SetBlock(0, 3, 0, DevBlockWorld.Full);
            Assert.Equal(4, ItemGroundSnap.SnapDown(g, DevBlockWorld.Shapes, 0, 3, 0));
        }

        [Fact]
        public void DeepSurfaceWithinScanDepth_Found()
        {
            var g = Grid();
            g.SetBlock(0, -40, 0, DevBlockWorld.HalfStep);
            Assert.Equal(-40, ItemGroundSnap.SnapDown(g, DevBlockWorld.Shapes, 0, 5, 0));
        }

        [Fact]
        public void SurfaceBeyondScanDepth_StaysAtStart()
        {
            var g = Grid();
            g.SetBlock(0, 5 - ItemGroundSnap.MaxScanDepth - 2, 0, DevBlockWorld.Full);
            Assert.Equal(5, ItemGroundSnap.SnapDown(g, DevBlockWorld.Shapes, 0, 5, 0));
        }
    }
}
