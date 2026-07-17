using System.IO;
using Shared.World.Blocks;

namespace ServerTests.Shared.World.Blocks
{
    public class ZoneLoadTests
    {
        private const ushort WallT = 1;
        private const ushort AnchorT = 4;

        private sealed class TestClassifier : IZoneClassifier
        {
            public bool IsSolid(ushort t) => t == WallT;
            public bool IsDoor(ushort t) => false;
            public bool IsFloorAnchor(ushort t) => t == AnchorT;
            public bool IsDivider(ushort t) => false;
            public bool IsMergeMarker(ushort t) => false;
        }

        private static BlockGrid SealedRoomWithSeed()
        {
            var g = new BlockGrid();
            for (int x = 0; x <= 4; x++)
                for (int z = 0; z <= 4; z++)
                {
                    g.SetBlock(x, 0, z, WallT);
                    g.SetBlock(x, 2, z, WallT);
                }
            for (int x = 0; x <= 4; x++)
            {
                g.SetBlock(x, 1, 0, WallT);
                g.SetBlock(x, 1, 4, WallT);
            }
            for (int z = 0; z <= 4; z++)
            {
                g.SetBlock(0, 1, z, WallT);
                g.SetBlock(4, 1, z, WallT);
            }
            g.SetBlock(1, 1, 1, AnchorT);
            g.GetSection(0, 0, 0).SetSeed(ChunkSection.LocalIndex(1, 1, 1), new FloorSeed("Мостик", 0, 4));
            return g;
        }

        [Fact]
        public void LoadFlood_PopulatesZones_ReadyForStream()
        {
            var g = SealedRoomWithSeed();

            var zones = ZoneFlood.Recompute(g, new TestClassifier());

            Assert.Single(zones.Zones);
            Assert.Empty(zones.Conflicts);
            var zone = zones.Zones[0];
            Assert.Equal("Мостик", zone.Name);
            Assert.Equal(4, zone.Floor);
            Assert.Equal(0, zone.Rank);
            Assert.Single(zone.Seeds);
            Assert.Equal(new BlockCoord(1, 1, 1), zone.Seeds[0].Pos);

            for (int x = 1; x <= 3; x++)
                for (int z = 1; z <= 3; z++)
                    Assert.Equal(zone.Id, g.GetZone(x, 1, z));
            Assert.Equal((ushort)0, g.GetZone(2, 5, 2));

            var section = g.GetSection(0, 0, 0);
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                BlockMapSerializer.WriteSection(w, section);
            ms.Position = 0;
            using var r = new BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: true);
            var streamed = BlockMapSerializer.ReadSection(r);

            Assert.Equal(zone.Id, streamed.GetZone(ChunkSection.LocalIndex(2, 1, 2)));
            Assert.True(streamed.TryGetSeed(ChunkSection.LocalIndex(1, 1, 1), out var seed));
            Assert.Equal("Мостик", seed.Name);
        }

        [Fact]
        public void LoadFlood_Rerun_IsIdempotent()
        {
            var g = SealedRoomWithSeed();

            var first = ZoneFlood.Recompute(g, new TestClassifier());
            ushort zid = first.Zones[0].Id;
            var second = ZoneFlood.Recompute(g, new TestClassifier());

            Assert.Equal(zid, second.Zones[0].Id);
            Assert.Equal(zid, g.GetZone(2, 1, 2));
        }
    }
}
