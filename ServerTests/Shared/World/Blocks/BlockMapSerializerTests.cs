using System.IO;
using Shared.World.Blocks;
using Shared.World;

namespace ServerTests.Shared.World.Blocks
{
    public class BlockMapSerializerTests
    {
        private static BlockGrid BuildSampleGrid()
        {
            var g = new BlockGrid();
            g.SetBlock(0, 0, 0, 1);
            g.SetBlock(-1, -1, -1, 2);      // отрицательная секция
            g.SetBlock(40, 3, -20, 3);
            g.SetState(0, 0, 0, BlockState.WithFacing(BlockState.Open, 1));
            g.SetBake(0, 0, 0, (byte)(ChunkSection.BakeCeiling | ChunkSection.BakeInteriorFloor)); // v11-канал

            for (int i = 0; i < 300; i++)   // raw-fallback секция (в стороне от остальных)
                g.SetBlock(1000 + i, 0, 0, (ushort)(1 + i));
            g.SetState(1000, 0, 0, BlockState.Pipe);
            return g;
        }

        [Fact]
        public void RoundTrip_PreservesBlocksAndStates()
        {
            var g = BuildSampleGrid();

            using var ms = new MemoryStream();
            BlockMapSerializer.Write(ms, g);
            ms.Position = 0;
            var loaded = BlockMapSerializer.Read(ms);

            Assert.Equal((ushort)1, loaded.GetBlock(0, 0, 0));
            Assert.Equal((ushort)2, loaded.GetBlock(-1, -1, -1));
            Assert.Equal((ushort)3, loaded.GetBlock(40, 3, -20));
            Assert.Equal((ushort)0, loaded.GetBlock(5, 5, 5));
            Assert.Equal(BlockState.WithFacing(BlockState.Open, 1), loaded.GetState(0, 0, 0));
            Assert.Equal((byte)(ChunkSection.BakeCeiling | ChunkSection.BakeInteriorFloor), loaded.GetBake(0, 0, 0));

            for (int i = 0; i < 300; i++)
                Assert.Equal((ushort)(1 + i), loaded.GetBlock(1000 + i, 0, 0));
            Assert.Equal(BlockState.Pipe, loaded.GetState(1000, 0, 0));
            Assert.Equal(g.Sections.Count, loaded.Sections.Count);
        }

        [Fact]
        public void RoundTrip_IsByteStable()
        {
            var g = BuildSampleGrid();

            using var first = new MemoryStream();
            BlockMapSerializer.Write(first, g);

            first.Position = 0;
            var loaded = BlockMapSerializer.Read(first);
            using var second = new MemoryStream();
            BlockMapSerializer.Write(second, loaded);

            Assert.Equal(first.ToArray(), second.ToArray());
        }

        [Fact]
        public void Read_AcceptsV10WithoutBake()
        {
            // v10-заголовок (пустая карта) читается: бейк-канала нет — просто пусто.
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                w.Write(BlockMapSerializer.Magic);
                w.Write((ushort)10);
                w.Write((byte)1); // gridCount
                w.Write((byte)0); // gridId
                w.Write(0);       // sectionCount
            }
            ms.Position = 0;
            var loaded = BlockMapSerializer.Read(ms);
            Assert.Empty(loaded.Sections);
        }

        [Fact]
        public void Read_RejectsTileVersions()
        {
            // Заголовок тайлового v3: конвертации нет — читатель обязан внятно отказать.
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                w.Write(MapSerializer.Magic);
                w.Write((ushort)3);
                w.Write(0);
            }
            ms.Position = 0;
            Assert.Throws<InvalidDataException>(() => BlockMapSerializer.Read(ms));
        }

        [Fact]
        public void Read_RejectsBadMagicAndFutureVersion()
        {
            using var bad = new MemoryStream(new byte[] { 1, 2, 3, 4, 0, 0 });
            Assert.Throws<InvalidDataException>(() => BlockMapSerializer.Read(bad));

            using var future = new MemoryStream();
            using (var w = new BinaryWriter(future, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                w.Write(BlockMapSerializer.Magic);
                w.Write((ushort)(BlockMapSerializer.Version + 1));
            }
            future.Position = 0;
            Assert.Throws<InvalidDataException>(() => BlockMapSerializer.Read(future));
        }

        [Fact]
        public void RoundTrip_MultiBlockDoor2x1x2()
        {
            // Дверь 2×1×2: 4 позиции одного типа, part-биты различают четверти, Open — общий (сервер зовёт для всех).
            var g = new BlockGrid();
            const ushort doorType = 50;
            var parts = new (int x, int z, int part)[] { (10, 0, 0), (11, 0, 1), (10, 1, 2), (11, 1, 3) };

            foreach (var p in parts)
            {
                g.SetBlock(p.x, 5, p.z, doorType);
                g.SetState(p.x, 5, p.z, BlockState.WithPart(BlockState.WithFacing(BlockState.Open, 1), p.part));
            }

            using var ms = new MemoryStream();
            BlockMapSerializer.Write(ms, g);
            ms.Position = 0;
            var loaded = BlockMapSerializer.Read(ms);

            foreach (var p in parts)
            {
                Assert.Equal(doorType, loaded.GetBlock(p.x, 5, p.z));
                byte st = loaded.GetState(p.x, 5, p.z);
                Assert.NotEqual(0, st & BlockState.Open);
                Assert.Equal(1, BlockState.GetFacing(st));
                Assert.Equal(p.part, BlockState.GetPart(st));
            }
        }

        [Fact]
        public void SaveLoad_File()
        {
            var g = BuildSampleGrid();
            string path = Path.Combine(Path.GetTempPath(), $"blockmap-test-{System.Guid.NewGuid():N}.smap");
            try
            {
                BlockMapSerializer.SaveToFile(path, g);
                var loaded = BlockMapSerializer.LoadFromFile(path);
                Assert.Equal(g.Sections.Count, loaded.Sections.Count);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
