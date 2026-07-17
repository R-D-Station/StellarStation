using System.IO;
using Shared.World.Blocks;

namespace ServerTests.Shared.World.Blocks
{
    public class ZoneSubstrateTests
    {
        private const string UnicodeName = "Медблок №3 — «Стационар» 🚀";

        private static BlockGrid BuildZonedGrid()
        {
            var g = new BlockGrid();
            g.SetBlock(0, 0, 0, 1);
            g.SetBlock(1, 0, 0, 2);
            g.SetBlock(-5, -3, 7, 3);
            g.SetBake(0, 0, 0, (byte)(ChunkSection.BakeCeiling | ChunkSection.BakeDivider));
            g.SetBake(1, 0, 0, ChunkSection.BakeMerge);

            var s = g.GetSection(0, 0, 0);
            s.SetZone(ChunkSection.LocalIndex(0, 0, 0), 7);
            s.SetZone(ChunkSection.LocalIndex(1, 0, 0), ushort.MaxValue);
            s.SetZone(ChunkSection.LocalIndex(2, 0, 0), 9);
            s.SetSeed(ChunkSection.LocalIndex(0, 0, 0), new FloorSeed(UnicodeName, -1, 3));
            s.SetSeed(ChunkSection.LocalIndex(1, 0, 0), new FloorSeed("", 42, -7));

            var far = g.GetSection(-1, -1, 0);
            far.SetZone(ChunkSection.LocalIndex(11, 13, 7), 300);
            far.SetSeed(ChunkSection.LocalIndex(11, 13, 7), new FloorSeed(new string('Я', 500), 1, 0));
            return g;
        }

        [Fact]
        public void RoundTrip_V12_ZonesSeedsAndFlags()
        {
            var g = BuildZonedGrid();

            using var ms = new MemoryStream();
            BlockMapSerializer.Write(ms, g);
            ms.Position = 0;
            var loaded = BlockMapSerializer.Read(ms);

            var s = loaded.GetSection(0, 0, 0);
            Assert.Equal((ushort)7, s.GetZone(ChunkSection.LocalIndex(0, 0, 0)));
            Assert.Equal(ushort.MaxValue, s.GetZone(ChunkSection.LocalIndex(1, 0, 0)));
            Assert.Equal((ushort)9, s.GetZone(ChunkSection.LocalIndex(2, 0, 0)));
            Assert.Equal((ushort)0, s.GetZone(ChunkSection.LocalIndex(5, 5, 5)));

            Assert.True(s.TryGetSeed(ChunkSection.LocalIndex(0, 0, 0), out var seed));
            Assert.Equal(UnicodeName, seed.Name);
            Assert.Equal(-1, seed.Rank);
            Assert.Equal(3, seed.Floor);
            Assert.True(s.TryGetSeed(ChunkSection.LocalIndex(1, 0, 0), out var empty));
            Assert.Equal("", empty.Name);
            Assert.Equal(42, empty.Rank);
            Assert.Equal(-7, empty.Floor);

            Assert.Equal((byte)(ChunkSection.BakeCeiling | ChunkSection.BakeDivider), loaded.GetBake(0, 0, 0));
            Assert.Equal(ChunkSection.BakeMerge, loaded.GetBake(1, 0, 0));

            var far = loaded.GetSection(-1, -1, 0);
            Assert.Equal((ushort)300, far.GetZone(ChunkSection.LocalIndex(11, 13, 7)));
            Assert.True(far.TryGetSeed(ChunkSection.LocalIndex(11, 13, 7), out var longSeed));
            Assert.Equal(new string('Я', 500), longSeed.Name);
        }

        [Fact]
        public void RoundTrip_V12_IsByteStable()
        {
            var g = BuildZonedGrid();

            using var first = new MemoryStream();
            BlockMapSerializer.Write(first, g);

            first.Position = 0;
            var loaded = BlockMapSerializer.Read(first);
            using var second = new MemoryStream();
            BlockMapSerializer.Write(second, loaded);

            Assert.Equal(first.ToArray(), second.ToArray());
        }

        [Fact]
        public void Read_AcceptsV11WithoutZonesAndSeeds()
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                w.Write(BlockMapSerializer.Magic);
                w.Write((ushort)11);
                w.Write((byte)1);
                w.Write((byte)0);
                w.Write(1);
                w.Write(0);
                w.Write(0);
                w.Write(0);

                w.Write((byte)0);
                w.Write((ushort)2);
                w.Write((ushort)1);
                var indices = new byte[ChunkSection.BlockCount];
                indices[ChunkSection.LocalIndex(5, 0, 0)] = 1;
                w.Write(indices, 0, indices.Length);
                w.Write((ushort)0);
                w.Write((ushort)1);
                w.Write((ushort)ChunkSection.LocalIndex(5, 0, 0));
                w.Write(ChunkSection.BakeCeiling);
            }
            ms.Position = 0;

            var loaded = BlockMapSerializer.Read(ms);
            Assert.Equal((ushort)1, loaded.GetBlock(5, 0, 0));
            Assert.Equal(ChunkSection.BakeCeiling, loaded.GetBake(5, 0, 0));

            var s = loaded.GetSection(0, 0, 0);
            Assert.Equal((ushort)0, s.GetZone(ChunkSection.LocalIndex(5, 0, 0)));
            Assert.False(s.TryGetSeed(ChunkSection.LocalIndex(5, 0, 0), out _));
        }

        [Fact]
        public void Section_ZoneAllowedOnAir_SeedForbidden()
        {
            var s = new ChunkSection();
            int li = ChunkSection.LocalIndex(1, 2, 3);

            Assert.True(s.SetZone(li, 5));
            Assert.Equal((ushort)5, s.GetZone(li));
            Assert.False(s.SetSeed(li, new FloorSeed("x", 1, 1)));
            Assert.False(s.TryGetSeed(li, out _));
        }

        [Fact]
        public void Section_SetBlockAir_KeepsZone_ClearsSeed()
        {
            var s = new ChunkSection();
            int li = ChunkSection.LocalIndex(4, 4, 4);
            s.SetBlock(li, 9);
            Assert.True(s.SetZone(li, 12));
            Assert.True(s.SetSeed(li, new FloorSeed("Мостик", 0, 1)));

            s.SetBlock(li, 0);

            Assert.Equal((ushort)12, s.GetZone(li));
            Assert.False(s.TryGetSeed(li, out _));
        }

        [Fact]
        public void Section_ZoneAndSeed_ChangeDetection()
        {
            var s = new ChunkSection();
            int li = ChunkSection.LocalIndex(0, 0, 0);
            s.SetBlock(li, 1);

            Assert.True(s.SetZone(li, 7));
            Assert.False(s.SetZone(li, 7));
            Assert.True(s.SetZone(li, 0));
            Assert.Equal((ushort)0, s.GetZone(li));

            var seed = new FloorSeed("Ангар", 2, -1);
            Assert.True(s.SetSeed(li, seed));
            Assert.False(s.SetSeed(li, new FloorSeed("Ангар", 2, -1)));
            Assert.True(s.SetSeed(li, new FloorSeed("Ангар", 3, -1)));
            Assert.True(s.RemoveSeed(li));
            Assert.False(s.RemoveSeed(li));
        }

        [Fact]
        public void Catalog_NewCategories_Predicates()
        {
            var anchor = new BlockInfo(999, "FloorAnchor", BlockCategory.FloorAnchor,
                BlockFaceFlags.None, BlockFaceFlags.None, 0, System.Array.Empty<BlockBox>());
            var divider = new BlockInfo(998, "Divider", BlockCategory.Divider,
                BlockFaceFlags.None, BlockFaceFlags.None, 0, System.Array.Empty<BlockBox>());
            var merge = new BlockInfo(997, "Merge", BlockCategory.MergeMarker,
                BlockFaceFlags.None, BlockFaceFlags.None, 0, System.Array.Empty<BlockBox>());

            Assert.True(anchor.IsFloorAnchor);
            Assert.False(anchor.IsMarker);
            Assert.True(divider.IsMarker);
            Assert.True(merge.IsMarker);
            Assert.False(divider.IsFloorAnchor);
        }
    }
}
