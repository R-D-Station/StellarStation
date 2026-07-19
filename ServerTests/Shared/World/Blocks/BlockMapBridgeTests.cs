using System.IO;
using Server.Services;
using Shared.Messages.Core;
using Shared.World.Blocks;

namespace ServerTests.Shared.World.Blocks
{
    public class BlockMapBridgeTests
    {
        [Fact]
        public void BlockChunkData_RoundTripsSection()
        {
            var grid = new BlockGrid();
            grid.SetBlock(1, 2, 3, 7);
            grid.SetBlock(1, 2, 3, 7); // idempotent
            grid.SetState(1, 2, 3, 5);
            BlockGrid.UnpackKey(BlockGrid.KeyOfBlock(1, 2, 3), out int cx, out int cy, out int cz);

            var msg = new BlockChunkData { Cx = cx, Cy = cy, Cz = cz, Section = grid.GetSection(cx, cy, cz) };
            var back = new BlockChunkData();
            back.Deserialize(msg.Serialize());

            Assert.Equal((cx, cy, cz), (back.Cx, back.Cy, back.Cz));
            int li = global::Shared.World.Blocks.ChunkSection.LocalIndex(1, 2, 3);
            Assert.Equal((ushort)7, back.Section.GetBlock(li));
            Assert.Equal(5, back.Section.GetState(li));
        }

        [Fact]
        public void BlockSectionGone_And_UpdateBatch_RoundTrip()
        {
            var gone = new BlockSectionGone { Cx = -1, Cy = 2, Cz = -3, Reason = BlockSectionGone.Emptied };
            var goneBack = new BlockSectionGone();
            goneBack.Deserialize(gone.Serialize());
            Assert.Equal((-1, 2, -3, BlockSectionGone.Emptied), (goneBack.Cx, goneBack.Cy, goneBack.Cz, goneBack.Reason));

            var batch = new BlockUpdateBatch
            {
                Entries = new[]
                {
                    new BlockUpdateBatch.Entry { X = 1, Y = 2, Z = 3, BlockType = 9, State = 1 },
                    new BlockUpdateBatch.Entry { X = -4, Y = 0, Z = 5, BlockType = 0, State = 0 },
                }
            };
            var batchBack = new BlockUpdateBatch();
            batchBack.Deserialize(batch.Serialize());
            Assert.Equal(2, batchBack.Entries.Length);
            Assert.Equal((ushort)9, batchBack.Entries[0].BlockType);
            Assert.Equal(-4, batchBack.Entries[1].X);
        }

        [Fact]
        public void BlockUpdateBatch_RejectsGarbageCount()
        {
            var bad = new byte[] { 0, 0xFF, 0xFF }; // count 65535 без данных
            var msg = new BlockUpdateBatch();
            Assert.Throws<System.InvalidOperationException>(() => msg.Deserialize(bad));
        }

        [Fact]
        public void BlockWorldSpawn_PicksLowestMarkerOrFallback()
        {
            var g = new BlockGrid();
            g.SetBlock(5, 5, 3, 100);
            g.SetBlock(2, 1, 2, 100); // ниже по Y — приоритет
            g.SetBlock(0, 0, 0, 7);   // не маркер

            var (x, y, z) = BlockWorldSpawn.Find(g, t => t == 100, 9f, 9f, 9f);
            Assert.Equal((2.5f, 1f, 2.5f), (x, y, z)); // ноги в блоке маркера, Y — высота

            var none = BlockWorldSpawn.Find(g, t => t == 200, 9f, 8f, 7f);
            Assert.Equal((9f, 8f, 7f), none);
        }

        [Fact]
        public void BlockWorldSource_LoadsV10File()
        {
            string path = Path.Combine(Path.GetTempPath(), $"bridge-{System.Guid.NewGuid():N}.smap");
            try
            {
                var g = new BlockGrid();
                g.SetBlock(1, 2, 3, 42);
                BlockMapSerializer.SaveToFile(path, g);

                var (loaded, fromFile) = BlockWorldSource.Load(path);
                Assert.True(fromFile);
                Assert.Equal((ushort)42, loaded.GetBlock(1, 2, 3));
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void BlockWorldSource_FallsBackToDevWorld()
        {
            // Нет файла → дев-полигон.
            var (missing, fromFileA) = BlockWorldSource.Load($"no-such-{System.Guid.NewGuid():N}.smap");
            Assert.False(fromFileA);
            Assert.Equal((ushort)DevBlockWorld.Slab, missing.GetBlock(0, 0, 0));

            // Тайловый файл (v3-заголовок) → тоже дев-полигон, без падения.
            string path = Path.Combine(Path.GetTempPath(), $"tile-{System.Guid.NewGuid():N}.smap");
            try
            {
                using (var w = new BinaryWriter(File.Create(path)))
                {
                    w.Write(global::Shared.World.MapSerializer.Magic);
                    w.Write((ushort)3);
                    w.Write(0);
                }
                var (tile, fromFileB) = BlockWorldSource.Load(path);
                Assert.False(fromFileB);
                Assert.Equal((ushort)DevBlockWorld.Slab, tile.GetBlock(0, 0, 0));
            }
            finally { File.Delete(path); }
        }
    }
}
