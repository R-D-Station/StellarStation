using System.IO;
using Shared.Configs;
using Shared.World.Blocks;

namespace ServerTests.Shared.World.Blocks
{
    // v13 item-спавн лист: round-trip, побайтовая стабильность, legacy v12, обрезанный хвост, ластик ячейки, серверный спавн на загрузке.
    public class ItemSpawnTests
    {
        private static BlockGrid GridWithSpawns()
        {
            var g = new BlockGrid();
            g.SetBlock(0, 0, 0, 1);
            g.AddItemSpawn(new ItemSpawn(2, 1, 3, 7, 1));
            g.AddItemSpawn(new ItemSpawn(2, 1, 3, 8, 5));
            g.AddItemSpawn(new ItemSpawn(-4, 2, 10, 7, 255));
            return g;
        }

        [Fact]
        public void RoundTrip_V13_SpawnListPreserved()
        {
            var g = GridWithSpawns();

            using var ms = new MemoryStream();
            BlockMapSerializer.Write(ms, g);
            ms.Position = 0;
            var loaded = BlockMapSerializer.Read(ms);

            Assert.Equal(3, loaded.ItemSpawns.Count);
            Assert.Equal((2, 1, 3, (ushort)7, (byte)1),
                (loaded.ItemSpawns[0].X, loaded.ItemSpawns[0].Y, loaded.ItemSpawns[0].Z,
                 loaded.ItemSpawns[0].DefId, loaded.ItemSpawns[0].Stack));
            Assert.Equal((2, 1, 3, (ushort)8, (byte)5),
                (loaded.ItemSpawns[1].X, loaded.ItemSpawns[1].Y, loaded.ItemSpawns[1].Z,
                 loaded.ItemSpawns[1].DefId, loaded.ItemSpawns[1].Stack));
            Assert.Equal((-4, 2, 10, (ushort)7, (byte)255),
                (loaded.ItemSpawns[2].X, loaded.ItemSpawns[2].Y, loaded.ItemSpawns[2].Z,
                 loaded.ItemSpawns[2].DefId, loaded.ItemSpawns[2].Stack));
        }

        [Fact]
        public void RoundTrip_V13_IsByteStable()
        {
            var g = GridWithSpawns();

            using var first = new MemoryStream();
            BlockMapSerializer.Write(first, g);
            first.Position = 0;
            var loaded = BlockMapSerializer.Read(first);
            using var second = new MemoryStream();
            BlockMapSerializer.Write(second, loaded);

            Assert.Equal(first.ToArray(), second.ToArray());
        }

        [Fact]
        public void Read_V12_EmptySpawnList()
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                w.Write(BlockMapSerializer.Magic);
                w.Write((ushort)12);
                w.Write((byte)1);
                w.Write((byte)0);
                w.Write(0);
            }
            ms.Position = 0;

            var loaded = BlockMapSerializer.Read(ms);
            Assert.Empty(loaded.ItemSpawns);
        }

        [Fact]
        public void Read_TruncatedSpawnList_Throws()
        {
            using var full = new MemoryStream();
            BlockMapSerializer.Write(full, GridWithSpawns());
            var bytes = full.ToArray();

            using var truncated = new MemoryStream(bytes, 0, bytes.Length - 6);
            Assert.ThrowsAny<System.Exception>(() => BlockMapSerializer.Read(truncated));
        }

        [Fact]
        public void RemoveItemSpawnsAt_RemovesAllInCell()
        {
            var g = GridWithSpawns();

            Assert.True(g.RemoveItemSpawnsAt(2, 1, 3));
            Assert.Single(g.ItemSpawns);
            Assert.Equal(-4, g.ItemSpawns[0].X);
            Assert.False(g.RemoveItemSpawnsAt(2, 1, 3));
        }

        [Fact]
        public void Server_SpawnsMapItemsOnLoad()
        {
            var g = GridWithSpawns();
            string path = Path.Combine(Path.GetTempPath(), $"itemspawn-test-{System.Guid.NewGuid():N}.smap");
            try
            {
                BlockMapSerializer.SaveToFile(path, g);
                var config = new SVars { BlocksWorld = true, MapPath = path };
                var server = new global::Server.Network.GameServer(config);

                Assert.Equal(3, server.EntityCount);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
