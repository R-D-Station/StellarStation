using System.IO;
using Shared.World.Blocks;

namespace ServerTests.Shared.World.Blocks
{
    /// <summary>Сжатие зон «дефолт на секцию + исключения»: модальный выбор, инвариант словаря, round-trip v14 и чтение v13.</summary>
    public class ZoneCompactionTests
    {
        private const ushort WallT = 1;

        private static ChunkSection Section()
        {
            var s = new ChunkSection();
            s.SetBlock(0, WallT);
            return s;
        }

        private static ushort[] Snapshot(ChunkSection s)
        {
            var v = new ushort[ChunkSection.BlockCount];
            for (int li = 0; li < ChunkSection.BlockCount; li++)
                v[li] = s.GetZone(li);
            return v;
        }

        private static void AssertSameZones(ushort[] before, ChunkSection s)
        {
            for (int li = 0; li < ChunkSection.BlockCount; li++)
                Assert.Equal(before[li], s.GetZone(li));
        }

        [Fact]
        public void Compact_DominantZone_BecomesDefault_CellsUnchanged()
        {
            var s = Section();
            for (int li = 0; li < ChunkSection.BlockCount; li++)
                s.SetZone(li, (ushort)(li < 400 ? 7 : 42));
            var before = Snapshot(s);
            int entriesBefore = s.ZoneExceptionCount;

            s.CompactZones();

            Assert.Equal(42, s.DefaultZone);
            Assert.Equal(400, s.ZoneExceptionCount);
            Assert.True(s.ZoneExceptionCount < entriesBefore);
            AssertSameZones(before, s);
        }

        [Fact]
        public void Compact_UniformSection_EmptiesDictionary()
        {
            var s = Section();
            for (int li = 0; li < ChunkSection.BlockCount; li++)
                s.SetZone(li, 5);

            s.CompactZones();

            Assert.Equal(5, s.DefaultZone);
            Assert.Equal(0, s.ZoneExceptionCount);
            for (int li = 0; li < ChunkSection.BlockCount; li++)
                Assert.Equal(5, s.GetZone(li));
        }

        [Fact]
        public void Compact_NoZones_KeepsZeroDefault()
        {
            var s = Section();

            s.CompactZones();

            Assert.Equal(0, s.DefaultZone);
            Assert.Equal(0, s.ZoneExceptionCount);
            Assert.Equal(0, s.GetZone(123));
        }

        [Fact]
        public void Compact_ZeroCells_BecomeExplicitExceptions()
        {
            var s = Section();
            for (int li = 100; li < ChunkSection.BlockCount; li++)
                s.SetZone(li, ZoneFlood.ExteriorZoneId);
            var before = Snapshot(s);

            s.CompactZones();

            Assert.Equal(ZoneFlood.ExteriorZoneId, s.DefaultZone);
            Assert.Equal(100, s.ZoneExceptionCount);
            for (int li = 0; li < 100; li++)
                Assert.Equal(0, s.GetZone(li));
            AssertSameZones(before, s);
        }

        [Fact]
        public void Compact_TieBreak_PicksSmallerId()
        {
            var a = Section();
            var b = Section();
            for (int li = 0; li < ChunkSection.BlockCount; li++)
            {
                ushort z = (ushort)(li < ChunkSection.BlockCount / 2 ? 9 : 3);
                a.SetZone(li, z);
                b.SetZone(li, z);
            }

            a.CompactZones();
            b.CompactZones();

            Assert.Equal(3, a.DefaultZone);
            Assert.Equal(a.DefaultZone, b.DefaultZone);
            Assert.Equal(a.ZoneExceptionCount, b.ZoneExceptionCount);
        }

        [Fact]
        public void SetZone_EqualToDefault_StoresNoException()
        {
            var s = Section();
            for (int li = 0; li < ChunkSection.BlockCount; li++)
                s.SetZone(li, 8);
            s.CompactZones();
            Assert.Equal(0, s.ZoneExceptionCount);

            Assert.False(s.SetZone(10, 8));
            Assert.Equal(0, s.ZoneExceptionCount);

            Assert.True(s.SetZone(10, 4));
            Assert.Equal(1, s.ZoneExceptionCount);
            Assert.Equal(4, s.GetZone(10));

            Assert.True(s.SetZone(10, 8));
            Assert.Equal(0, s.ZoneExceptionCount);
            Assert.Equal(8, s.GetZone(10));
        }

        [Fact]
        public void ResetZones_ClearsDefaultAndExceptions()
        {
            var s = Section();
            for (int li = 0; li < ChunkSection.BlockCount; li++)
                s.SetZone(li, 6);
            s.SetZone(1, 2);
            s.CompactZones();

            s.ResetZones();

            Assert.Equal(0, s.DefaultZone);
            Assert.Equal(0, s.ZoneExceptionCount);
            Assert.Equal(0, s.GetZone(1));
        }

        [Fact]
        public void RoundTripV14_PreservesEveryCellZone()
        {
            var s = Section();
            for (int li = 0; li < ChunkSection.BlockCount; li++)
                s.SetZone(li, ZoneFlood.ExteriorZoneId);
            s.SetZone(5, 1);
            s.SetZone(6, 0);
            s.SetZone(7, 2);
            s.CompactZones();
            var before = Snapshot(s);
            Assert.Equal(ZoneFlood.ExteriorZoneId, s.DefaultZone);

            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                BlockMapSerializer.WriteSection(w, s);
            ms.Position = 0;
            using var r = new BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: true);
            var back = BlockMapSerializer.ReadSection(r);

            Assert.Equal(s.DefaultZone, back.DefaultZone);
            AssertSameZones(before, back);
            Assert.Equal(s.ZoneExceptionCount, back.ZoneExceptionCount);
        }

        [Fact]
        public void ReadsLegacyV13_WithoutDefaultZone()
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                w.Write((byte)0);
                w.Write((ushort)2);
                w.Write(WallT);
                var indices = new byte[ChunkSection.BlockCount];
                indices[0] = 1;
                w.Write(indices, 0, ChunkSection.BlockCount);

                w.Write((ushort)0);
                w.Write((ushort)0);

                w.Write((ushort)2);
                w.Write((ushort)3); w.Write((ushort)11);
                w.Write((ushort)9); w.Write((ushort)12);

                w.Write((ushort)0);
            }

            ms.Position = 0;
            using var r = new BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: true);
            var back = BlockMapSerializer.ReadSection(r, 13);

            Assert.Equal(ms.Length, ms.Position);
            Assert.Equal(0, back.DefaultZone);
            Assert.Equal(WallT, back.GetBlock(0));
            Assert.Equal(11, back.GetZone(3));
            Assert.Equal(12, back.GetZone(9));
            Assert.Equal(0, back.GetZone(4));
        }
    }
}
