using Shared.World.Blocks;

namespace ServerTests.Shared.World.Blocks
{
    public class BlockCatalogTests
    {
        [Fact]
        public void UnknownId_FallsBackToAir()
        {
            var info = BlockCatalog.Get(12345);
            Assert.Equal(BlockCatalog.Air, info);
            Assert.False(info.HasCollision);
        }

        [Fact]
        public void BlockBox_PresetsQuantized()
        {
            var slab = BlockBox.SlabTop;
            Assert.Equal(0.75f, slab.MinYf);   // слаб 0.25, зафиксирован по верху блока (Y — высота)
            Assert.Equal(1f, slab.MaxYf);

            var full = BlockBox.Full;
            Assert.Equal(0f, full.MinXf);
            Assert.Equal(1f, full.MaxYf);
        }

        [Fact]
        public void BlockIdAllocator_SmallestFreeUshort()
        {
            Assert.Equal(1, BlockIdAllocator.SmallestFreeId(null));
            Assert.Equal(3, BlockIdAllocator.SmallestFreeId(new ushort[] { 1, 2, 4 }));
            Assert.True(BlockIdAllocator.IsTaken(4, new ushort[] { 1, 2, 4 }));
            Assert.False(BlockIdAllocator.IsTaken(0, new ushort[] { 1, 2, 4 }));
        }

        [Fact]
        public void BlockBox_RotatedCW_FourStepsIdentity()
        {
            var b = new BlockBox(2, 0, 5, 10, 16, 7); // асимметричный в плане
            var r1 = b.RotatedCW();
            Assert.Equal((b.MinZ, (byte)(16 - b.MaxX), b.MaxZ, (byte)(16 - b.MinX)), (r1.MinX, r1.MinZ, r1.MaxX, r1.MaxZ));
            Assert.Equal((b.MinY, b.MaxY), (r1.MinY, r1.MaxY)); // высота не вращается

            var r4 = r1.RotatedCW().RotatedCW().RotatedCW();
            Assert.Equal((b.MinX, b.MinY, b.MinZ, b.MaxX, b.MaxY, b.MaxZ),
                         (r4.MinX, r4.MinY, r4.MinZ, r4.MaxX, r4.MaxY, r4.MaxZ));
        }

        [Fact]
        public void BlockInfo_PartBoxes_RotateByFacing()
        {
            // Часть 0: слаб-топ (симметричен — не меняется); часть 1: полустолб у +X-грани.
            var info = new BlockInfo(5, "Gate", BlockCategory.Wall, BlockFaceFlags.All, BlockFaceFlags.All, 0,
                new[] { new[] { BlockBox.SlabTop }, new[] { new BlockBox(8, 0, 0, 16, 16, 16) } }, 2, 1, 1);

            Assert.Equal(2, info.PartCount);
            Assert.True(info.HasCollision);

            var p1f0 = info.GetBoxes(1, 0)[0];
            Assert.Equal((8, 16), ((int)p1f0.MinX, (int)p1f0.MaxX));

            var p1f1 = info.GetBoxes(1, 1)[0]; // 90° CW: +X-половина → −Z-половина
            Assert.Equal((0, 16, 0, 8), ((int)p1f1.MinX, (int)p1f1.MaxX, (int)p1f1.MinZ, (int)p1f1.MaxZ));

            Assert.Same(info.GetBoxes(0, 2), info.GetBoxes(0, 2)); // без аллокаций: хранимые массивы
        }

        [Fact]
        public void Openable_DerivedFromCategory()
        {
            var door = new BlockInfo(9, "Door", BlockCategory.Door, BlockFaceFlags.All, BlockFaceFlags.All, 0, (BlockBox[])null);
            var wall = new BlockInfo(8, "Wall", BlockCategory.Wall, BlockFaceFlags.All, BlockFaceFlags.All, 2, (BlockBox[])null);
            var marker = new BlockInfo(7, "Spawn", BlockCategory.Marker, BlockFaceFlags.None, BlockFaceFlags.None, 0, (BlockBox[])null);

            Assert.True(door.Openable);
            Assert.False(wall.Openable);
            Assert.True(marker.IsMarker);
            Assert.False(marker.HasCollision);
        }
    }
}
