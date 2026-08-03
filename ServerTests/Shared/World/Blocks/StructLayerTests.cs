using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using Shared.Simulation.Blocks;
using Shared.World.Blocks;

namespace ServerTests.Shared.World.Blocks
{
    internal static class TestStructCatalog
    {
        internal const ushort Door2x2 = 940;
        internal const ushort LiftDoor5x5 = 941;
        internal const ushort Big4x4x2 = 942;
        internal const ushort Single = 943;

        /// <summary>2×1×1: якорь — ПОЛНЫЙ куб, вторая часть — низкий слэб. Различает «боксы части» и «боксы якоря».</summary>
        internal const ushort SplitHeights = 944;

        /// <summary>3×1×1 Wall: часть 1 (середина) БЕЗ боксов (проём), концы — полные. Пер-частная коллизия/герметичность.</summary>
        internal const ushort GappyWall = 946;

        /// <summary>3×1×1 Wall: якорь и часть 1 БЕЗ боксов, часть 2 — полная. Мувер должен упереться в НЕ-якорную клетку.</summary>
        internal const ushort WalkWall = 947;

        /// <summary>17×1×1: ось &gt; MaxAxis — часть 16 даёт смещение вне ±15, PlaceMultiBlock обязан отказать.</summary>
        internal const ushort Wide17 = 948;

        [ModuleInitializer]
        internal static void Seed()
        {
            var field = typeof(BlockCatalog).GetField("_byId", BindingFlags.NonPublic | BindingFlags.Static)!;
            var byId = (Dictionary<ushort, BlockInfo>)field.GetValue(null)!;

            byId[Door2x2] = Multi(Door2x2, "TestDoor2x2", 2, 2, 1);
            byId[LiftDoor5x5] = Multi(LiftDoor5x5, "TestLiftDoor5x5", 5, 5, 1);
            byId[Big4x4x2] = Multi(Big4x4x2, "TestBig4x4x2", 4, 4, 2);
            byId[Single] = Multi(Single, "TestSingle", 1, 1, 1);

            byId[SplitHeights] = new BlockInfo(SplitHeights, "TestSplitHeights", BlockCategory.Wall,
                BlockFaceFlags.All, BlockFaceFlags.All, 0,
                new[]
                {
                    new[] { new BlockBox(0, 0, 0, 16, 16, 16) }, // часть 0 (якорь) — полный
                    new[] { new BlockBox(0, 0, 0, 16, 8, 16) },  // часть 1 — слэб 0.5
                },
                2, 1, 1);

            byId[GappyWall] = new BlockInfo(GappyWall, "TestGappyWall", BlockCategory.Wall,
                BlockFaceFlags.All, BlockFaceFlags.All, 0,
                new[] { new[] { BlockBox.Full }, System.Array.Empty<BlockBox>(), new[] { BlockBox.Full } },
                3, 1, 1);

            byId[WalkWall] = new BlockInfo(WalkWall, "TestWalkWall", BlockCategory.Wall,
                BlockFaceFlags.All, BlockFaceFlags.All, 0,
                new[] { System.Array.Empty<BlockBox>(), System.Array.Empty<BlockBox>(), new[] { BlockBox.Full } },
                3, 1, 1);

            byId[Wide17] = FullMulti(Wide17, "TestWide17", 17, 1, 1);
        }

        private static BlockInfo FullMulti(ushort id, string name, byte sx, byte sy, byte sz)
        {
            int parts = sx * sy * sz;
            var boxes = new BlockBox[parts][];
            for (int p = 0; p < parts; p++)
                boxes[p] = new[] { BlockBox.Full };
            return new BlockInfo(id, name, BlockCategory.Wall, BlockFaceFlags.All, BlockFaceFlags.All, 0,
                boxes, sx, sy, sz);
        }

        private static BlockInfo Multi(ushort id, string name, byte sx, byte sy, byte sz)
        {
            int parts = sx * sy * sz;
            var boxes = new BlockBox[parts][];
            for (int p = 0; p < parts; p++)
                boxes[p] = new[] { Slice(p, sx, sz) };
            return new BlockInfo(id, name, BlockCategory.Wall, BlockFaceFlags.All, BlockFaceFlags.All, 0,
                boxes, sx, sy, sz);
        }

        // Уникальный «ломтик» на часть: 1³-бокс в локальных (w,y,d) части — контент боксов различает КАЖДУЮ часть.
        private static BlockBox Slice(int part, int sx, int sz)
        {
            MultiBlock.PartToLocal(part, sx, sz, out int w, out int y, out int d);
            return new BlockBox((byte)w, (byte)y, (byte)d, (byte)(w + 1), (byte)(y + 1), (byte)(d + 1));
        }
    }

    /// <summary>S1: слой принадлежности структур — паковка смещений, Place/Erase, резолв якоря, шейпы, миграция v14→v15.</summary>
    public class StructLayerTests
    {
        [Theory]
        [InlineData(0, 0, 0)]
        [InlineData(15, 15, 15)]
        [InlineData(-15, -15, -15)]
        [InlineData(-15, 15, -1)]
        [InlineData(3, -7, 11)]
        public void PackOffset_RoundTrips(int dx, int dy, int dz)
        {
            ushort packed = (ushort)MultiBlock.PackOffset(dx, dy, dz);
            MultiBlock.UnpackOffset(packed, out int ux, out int uy, out int uz);
            Assert.Equal(dx, ux);
            Assert.Equal(dy, uy);
            Assert.Equal(dz, uz);
        }

        [Fact]
        public void PackOffset_AllInRangeCombinations_AreUnique()
        {
            var seen = new HashSet<int>();
            for (int dx = -15; dx <= 15; dx++)
                for (int dy = -15; dy <= 15; dy++)
                    for (int dz = -15; dz <= 15; dz++)
                        Assert.True(seen.Add(MultiBlock.PackOffset(dx, dy, dz)), $"коллизия на ({dx},{dy},{dz})");
        }

        [Fact]
        public void RealOffsets_NeverPackToZero_SoZeroIsFreeSentinel()
        {
            // dy части = локальный y ≥ 0 ⇒ (dy+15) ≥ 15 ⇒ packed ≠ 0. Это и делает 0 корректным «нет записи».
            for (int sy = 1; sy <= 16; sy++)
                for (int part = 0; part < sy; part++)
                    for (int facing = 0; facing < 4; facing++)
                    {
                        MultiBlock.PartWorldOffset(part, 1, 1, facing, out int dx, out int dy, out int dz);
                        Assert.NotEqual(0, MultiBlock.PackOffset(dx, dy, dz));
                    }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void PartFromWorldOffset_InvertsPartWorldOffset(int facing)
        {
            const int sx = 5, sy = 5, sz = 1;
            int parts = MultiBlock.PartCount(sx, sy, sz);
            for (int p = 0; p < parts; p++)
            {
                MultiBlock.PartWorldOffset(p, sx, sz, facing, out int dx, out int dy, out int dz);
                Assert.Equal(p, MultiBlock.PartFromWorldOffset(dx, dy, dz, sx, sy, sz, facing));
            }
        }

        private static void AssertPlacedCorrectly(BlockGrid g, int ax, int ay, int az, ushort type, int facing)
        {
            var info = BlockCatalog.Get(type);
            int parts = MultiBlock.PartCount(info.SizeX, info.SizeY, info.SizeZ);
            for (int p = 0; p < parts; p++)
            {
                MultiBlock.PartWorldOffset(p, info.SizeX, info.SizeZ, facing, out int dx, out int dy, out int dz);
                int x = ax + dx, y = ay + dy, z = az + dz;

                Assert.Equal(type, g.GetBlock(x, y, z));
                Assert.Equal(facing, BlockState.GetFacing(g.GetState(x, y, z)));

                bool isAnchor = dx == 0 && dy == 0 && dz == 0;
                Assert.Equal(!isAnchor, g.TryGetStructOffset(x, y, z, out int gx, out int gy, out int gz));
                if (!isAnchor)
                {
                    Assert.Equal(dx, gx);
                    Assert.Equal(dy, gy);
                    Assert.Equal(dz, gz);
                }
                Assert.Equal(isAnchor, g.IsStructAnchor(x, y, z));

                Assert.True(g.AnchorOf(x, y, z, out int rax, out int ray, out int raz));
                Assert.Equal((ax, ay, az), (rax, ray, raz));
            }
        }

        [Theory]
        [InlineData(TestStructCatalog.Door2x2, 0)]
        [InlineData(TestStructCatalog.Door2x2, 1)]
        [InlineData(TestStructCatalog.Door2x2, 2)]
        [InlineData(TestStructCatalog.Door2x2, 3)]
        [InlineData(TestStructCatalog.LiftDoor5x5, 0)]
        [InlineData(TestStructCatalog.LiftDoor5x5, 1)]
        [InlineData(TestStructCatalog.LiftDoor5x5, 2)]
        [InlineData(TestStructCatalog.LiftDoor5x5, 3)]
        [InlineData(TestStructCatalog.Big4x4x2, 0)]
        [InlineData(TestStructCatalog.Big4x4x2, 3)]
        public void PlaceMultiBlock_AllPartsLinkedToAnchor(ushort type, int facing)
        {
            var g = new BlockGrid();
            Assert.True(g.PlaceMultiBlock(20, 5, 20, type, facing));
            AssertPlacedCorrectly(g, 20, 5, 20, type, facing);
        }

        [Fact]
        public void PlaceMultiBlock_25Parts_FitsWithoutBitLimit()
        {
            var g = new BlockGrid();
            Assert.True(g.PlaceMultiBlock(0, 0, 0, TestStructCatalog.LiftDoor5x5, 0));

            int cells = 0;
            for (int x = 0; x < 5; x++)
                for (int y = 0; y < 5; y++)
                    if (g.GetBlock(x, y, 0) == TestStructCatalog.LiftDoor5x5)
                        cells++;
            Assert.Equal(25, cells);
        }

        [Fact]
        public void PlaceMultiBlock_Occupied_FailsAtomically()
        {
            var g = new BlockGrid();
            g.SetBlock(21, 5, 20, 1);

            Assert.False(g.PlaceMultiBlock(20, 5, 20, TestStructCatalog.Door2x2, 0));
            Assert.Equal(0, g.GetBlock(20, 5, 20));
            Assert.False(g.TryGetStructOffset(20, 5, 21, out _, out _, out _));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void EraseMultiBlock_FromAnyCell_ClearsEverything(int eraseFromPart)
        {
            var g = new BlockGrid();
            const int ax = 30, ay = 4, az = 30;
            Assert.True(g.PlaceMultiBlock(ax, ay, az, TestStructCatalog.Door2x2, 1));

            MultiBlock.PartWorldOffset(eraseFromPart, 2, 1, 1, out int fx, out int fy, out int fz);
            Assert.True(g.EraseMultiBlock(ax + fx, ay + fy, az + fz));

            for (int p = 0; p < 4; p++)
            {
                MultiBlock.PartWorldOffset(p, 2, 1, 1, out int dx, out int dy, out int dz);
                Assert.Equal(0, g.GetBlock(ax + dx, ay + dy, az + dz));
                Assert.False(g.TryGetStructOffset(ax + dx, ay + dy, az + dz, out _, out _, out _));
            }
        }

        [Fact]
        public void AnchorOf_Orphan_ReturnsFalse_NoCrash()
        {
            var g = new BlockGrid();
            Assert.True(g.PlaceMultiBlock(10, 3, 10, TestStructCatalog.Door2x2, 0));

            g.SetBlock(10, 3, 10, 0); // якорь снесён мимо EraseMultiBlock

            Assert.False(g.AnchorOf(11, 3, 10, out int ax, out int ay, out int az));
            Assert.Equal((11, 3, 10), (ax, ay, az));
        }

        [Fact]
        public void AnchorOf_SingleBlock_ReturnsFalse()
        {
            var g = new BlockGrid();
            g.SetBlock(5, 5, 5, TestStructCatalog.Single);
            Assert.False(g.AnchorOf(5, 5, 5, out _, out _, out _));
            Assert.False(g.IsStructAnchor(5, 5, 5));
        }

        [Fact]
        public void Shapes_PartBoxes_ComeFromLayer_NotState()
        {
            var g = new BlockGrid();
            Assert.True(g.PlaceMultiBlock(0, 0, 0, TestStructCatalog.LiftDoor5x5, 0));
            var info = BlockCatalog.Get(TestStructCatalog.LiftDoor5x5);
            var shapes = BlockCatalogShapes.Instance;

            for (int p = 0; p < 25; p++)
            {
                MultiBlock.PartWorldOffset(p, 5, 1, 0, out int dx, out int dy, out int dz);
                var actual = shapes.GetBoxes(TestStructCatalog.LiftDoor5x5, g.GetState(dx, dy, dz), g, dx, dy, dz);
                Assert.Same(info.GetBoxes(p, 0, false), actual);
            }
        }

        [Fact]
        public void Shapes_SingleBlock_IgnoresCoordinates()
        {
            var g = new BlockGrid();
            g.SetBlock(2, 2, 2, TestStructCatalog.Single);
            var shapes = BlockCatalogShapes.Instance;

            Assert.Same(shapes.GetBoxes(TestStructCatalog.Single, 0),
                        shapes.GetBoxes(TestStructCatalog.Single, 0, g, 2, 2, 2));
        }

        [Fact]
        public void RoundTripV15_PreservesLayerCellByCell()
        {
            var g = new BlockGrid();
            Assert.True(g.PlaceMultiBlock(3, 2, 3, TestStructCatalog.LiftDoor5x5, 2));

            using var ms = new MemoryStream();
            BlockMapSerializer.Write(ms, g);
            ms.Position = 0;
            var loaded = BlockMapSerializer.Read(ms);

            AssertPlacedCorrectly(loaded, 3, 2, 3, TestStructCatalog.LiftDoor5x5, 2);
        }

        [Fact]
        public void MigratesV14PartBits_ToLayer()
        {
            const ushort type = TestStructCatalog.Door2x2;
            const int facing = 2;
            const int part = 3;
            const int li = 0;

            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                w.Write((byte)0);
                w.Write((ushort)2);
                w.Write(type);
                var indices = new byte[ChunkSection.BlockCount];
                indices[li] = 1;
                w.Write(indices, 0, ChunkSection.BlockCount);

                byte legacyState = (byte)(BlockState.Open | (facing << 3) | (part << 5));
                w.Write((ushort)1);
                w.Write((ushort)li);
                w.Write(legacyState);

                w.Write((ushort)0);          // bake
                w.Write((ushort)0);          // DefaultZone (v14)
                w.Write((ushort)0);          // zone
                w.Write((ushort)0);          // seeds
            }

            ms.Position = 0;
            using var r = new BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: true);
            var section = BlockMapSerializer.ReadSection(r, 14);

            Assert.Equal(ms.Length, ms.Position);

            byte st = section.GetState(li);
            Assert.Equal(0, (st >> 5) & 0b11);                 // part-биты обнулены
            Assert.Equal(facing, BlockState.GetFacing(st));    // facing цел
            Assert.True(BlockState.GetOpen(st));               // Open цел

            MultiBlock.PartWorldOffset(part, 2, 1, facing, out int dx, out int dy, out int dz);
            Assert.Equal((ushort)MultiBlock.PackOffset(dx, dy, dz), section.GetStruct(li));
        }

        [Fact]
        public void ItemGroundSnap_UsesBoxesOfTheActualPart_NotAnchor()
        {
            var g = new BlockGrid();
            Assert.True(g.PlaceMultiBlock(0, 0, 0, TestStructCatalog.SplitHeights, 0));
            var shapes = BlockCatalogShapes.Instance;

            // Якорь (x=0) — полный куб: предмет ложится НАД ним. Часть 1 (x=1) — слэб 0.5: предмет остаётся В клетке.
            // До фикса обе колонны отдавали боксы ЯКОРЯ и снап возвращал 1 в обеих.
            Assert.Equal(1, ItemGroundSnap.SnapDown(g, shapes, 0, 3, 0));
            Assert.Equal(0, ItemGroundSnap.SnapDown(g, shapes, 1, 3, 0));
        }

        [Fact]
        public void SetBlock_OverwriteWithOtherType_DropsLayerRecord()
        {
            var g = new BlockGrid();
            Assert.True(g.PlaceMultiBlock(0, 0, 0, TestStructCatalog.Door2x2, 0));
            Assert.True(g.TryGetStructOffset(1, 0, 0, out _, out _, out _));

            g.SetBlock(1, 0, 0, TestStructCatalog.Single); // перезапись не-Air другим не-Air

            Assert.False(g.TryGetStructOffset(1, 0, 0, out _, out _, out _));
        }

        [Fact]
        public void EraseMultiBlock_LeavesNoLayerRecords_EvenOnTypeMismatch()
        {
            var g = new BlockGrid();
            Assert.True(g.PlaceMultiBlock(0, 0, 0, TestStructCatalog.Door2x2, 0));

            g.SetBlock(1, 0, 0, TestStructCatalog.Single); // ← чужой тип в футпринте (запись слоя уже снята)
            g.SetStructOffset(1, 0, 0, 1, 0, 0);           // ← и снова осиротевшая запись поверх чужого типа

            Assert.True(g.EraseMultiBlock(0, 0, 0));

            for (int p = 0; p < 4; p++)
            {
                MultiBlock.PartWorldOffset(p, 2, 1, 0, out int dx, out int dy, out int dz);
                Assert.False(g.TryGetStructOffset(dx, dy, dz, out _, out _, out _), $"хвост записи в части {p}");
            }
        }

        [Fact]
        public void EraseMultiBlock_KeepsCellBakeBits()
        {
            var g = new BlockGrid();
            g.SetBlock(8, 8, 8, TestStructCatalog.Single); // держит секцию живой: пустая секция удаляется вместе с бейком
            Assert.True(g.PlaceMultiBlock(0, 0, 0, TestStructCatalog.Door2x2, 0));
            Assert.True(g.SetBake(1, 0, 0, (byte)(ChunkSection.BakeDivider | ChunkSection.BakeCeiling)));

            Assert.True(g.EraseMultiBlock(0, 0, 0));

            Assert.Equal(ChunkSection.BakeDivider, g.GetBake(1, 0, 0));
        }

        [Fact]
        public void FromData_DropsLayerRecord_OnSingleCellType()
        {
            var section = new ChunkSection();
            section.SetBlock(0, TestStructCatalog.Single);

            var stale = new Dictionary<int, ushort> { [0] = (ushort)MultiBlock.PackOffset(1, 0, 0) };
            var rebuilt = InvokeFromData(section, stale);

            Assert.Equal(0, rebuilt.GetStruct(0));
        }

        [Fact]
        public void FromData_DropsLayerRecord_OnAirCell()
        {
            var section = new ChunkSection();
            var stale = new Dictionary<int, ushort> { [5] = (ushort)MultiBlock.PackOffset(1, 0, 0) };
            var rebuilt = InvokeFromData(section, stale);

            Assert.Equal(0, rebuilt.GetStruct(5));
        }

        private static ChunkSection InvokeFromData(ChunkSection source, Dictionary<int, ushort> structOffsets)
        {
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                BlockMapSerializer.WriteSection(w, source);

            var m = typeof(ChunkSection).GetMethod("FromData", BindingFlags.NonPublic | BindingFlags.Static)!;
            var palette = typeof(ChunkSection).GetProperty("Palette", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(source);
            var indices = typeof(ChunkSection).GetProperty("Indices", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(source);
            var raw = typeof(ChunkSection).GetProperty("Raw", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(source);

            return (ChunkSection)m.Invoke(null, new object?[]
            {
                palette, indices, raw, new Dictionary<int, byte>(), null, null, null, (ushort)0, structOffsets
            })!;
        }

        [Fact]
        public void CrossSection_MissingAnchorSection_IsNotOrphan()
        {
            var g = new BlockGrid();
            // Якорь в секции 0, часть — за границей 16³ в секции 1; затем секцию якоря «не стримили».
            Assert.True(g.PlaceMultiBlock(15, 0, 0, TestStructCatalog.Door2x2, 0));
            Assert.True(g.TryGetStructOffset(16, 0, 0, out int dx, out _, out _));
            Assert.Equal(1, dx);

            g.RemoveSection(0, 0, 0);

            Assert.False(g.AnchorOf(16, 0, 0, out int ax, out int ay, out int az));
            Assert.Equal((16, 0, 0), (ax, ay, az));
        }

        [Fact]
        public void Shapes_CorruptOffset_FallsBackToAnchorPart_NoThrow()
        {
            var g = new BlockGrid();
            Assert.True(g.PlaceMultiBlock(0, 0, 0, TestStructCatalog.Door2x2, 0));
            g.SetStructOffset(1, 0, 0, 14, 0, 14); // смещение вне футпринта 2×2 → часть вне диапазона

            var info = BlockCatalog.Get(TestStructCatalog.Door2x2);
            var boxes = BlockCatalogShapes.Instance.GetBoxes(
                TestStructCatalog.Door2x2, g.GetState(1, 0, 0), g, 1, 0, 0);

            Assert.Same(info.GetBoxes(0, 0, false), boxes);
        }

        [Fact]
        public void MigrationV14_UnknownType_KeepsStateBits_AndCounts()
        {
            const ushort unknownType = 60000;
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                w.Write((byte)0);
                w.Write((ushort)2);
                w.Write(unknownType);
                var indices = new byte[ChunkSection.BlockCount];
                indices[0] = 1;
                w.Write(indices, 0, ChunkSection.BlockCount);

                byte legacyState = (byte)(BlockState.Open | (2 << 3) | (3 << 5));
                w.Write((ushort)1);
                w.Write((ushort)0);
                w.Write(legacyState);

                w.Write((ushort)0);
                w.Write((ushort)0);
                w.Write((ushort)0);
                w.Write((ushort)0);
            }

            ms.Position = 0;
            using var r = new BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: true);
            var section = BlockMapSerializer.ReadSection(r, 14);

            Assert.Equal(ms.Length, ms.Position);
            byte st = section.GetState(0);
            Assert.True(BlockState.GetOpen(st));            // Open не потерян
            Assert.Equal(2, BlockState.GetFacing(st));      // facing не потерян
            Assert.Equal(3, (st >> 5) & 0b11);              // part-биты НЕ обнулены (тип неизвестен — не мутируем)
            Assert.Equal(0, section.GetStruct(0));
        }

        [Fact]
        public void MigrationV14_AnchorCell_GetsNoLayerRecord()
        {
            const ushort type = TestStructCatalog.Door2x2;
            const int li = 0;

            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                w.Write((byte)0);
                w.Write((ushort)2);
                w.Write(type);
                var indices = new byte[ChunkSection.BlockCount];
                indices[li] = 1;
                w.Write(indices, 0, ChunkSection.BlockCount);

                w.Write((ushort)1);
                w.Write((ushort)li);
                w.Write((byte)(1 << 3)); // facing 1, part 0 — якорь

                w.Write((ushort)0);
                w.Write((ushort)0);
                w.Write((ushort)0);
                w.Write((ushort)0);
            }

            ms.Position = 0;
            using var r = new BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: true);
            var section = BlockMapSerializer.ReadSection(r, 14);

            Assert.Equal(ms.Length, ms.Position);
            Assert.Equal(0, section.GetStruct(li));
            Assert.Equal(1, BlockState.GetFacing(section.GetState(li)));
        }

        [Fact]
        public void MultiBlockLimits_AreDeclared()
        {
            Assert.Equal(16, MultiBlock.MaxAxis);
            Assert.Equal(256, MultiBlock.MaxPartsSanity);
            Assert.Equal(15, MultiBlock.MaxOffset);
        }

        [Theory]
        [InlineData(-1, 0, 0)]   // w < 0
        [InlineData(5, 0, 0)]    // w >= sx
        [InlineData(0, 5, 0)]    // y >= sy
        [InlineData(0, 0, 1)]    // d >= sz (sz=1)
        public void LocalToPart_OutsideFootprint_ReturnsInvalid(int w, int y, int d)
            => Assert.Equal(-1, MultiBlock.LocalToPart(w, y, d, 5, 5, 1));

        [Fact]
        public void LocalToPart_InsideFootprint_IsValid()
            => Assert.True(MultiBlock.LocalToPart(4, 4, 0, 5, 5, 1) >= 0);

        [Fact]
        public void PlaceMultiBlock_AxisBeyondOffsetRange_RejectedAtomically()
        {
            var g = new BlockGrid();
            Assert.False(g.PlaceMultiBlock(0, 0, 0, TestStructCatalog.Wide17, 0)); // часть 16 → смещение 16 > MaxOffset
            Assert.Equal(0, g.GetBlock(0, 0, 0));
        }

        [Fact]
        public void SetStructOffset_OutOfRange_Rejected_NoWrap()
        {
            var g = new BlockGrid();
            g.SetBlock(0, 0, 0, TestStructCatalog.Single); // держит секцию
            Assert.False(g.SetStructOffset(0, 0, 0, 16, 0, 0)); // 16 > MaxOffset → отказ, а не перелив в соседнее поле
            Assert.False(g.TryGetStructOffset(0, 0, 0, out _, out _, out _));
        }

        [Fact]
        public void Shapes_StaleOffsetOutsideFootprint_IncrementsBadPartReads()
        {
            var g = new BlockGrid();
            Assert.True(g.PlaceMultiBlock(0, 0, 0, TestStructCatalog.Door2x2, 0));
            g.SetStructOffset(1, 0, 0, 14, 0, 14); // в диапазоне слоя, но вне футпринта 2×2

            int before = BlockCatalogShapes.BadPartReads;
            BlockCatalogShapes.Instance.GetBoxes(TestStructCatalog.Door2x2, g.GetState(1, 0, 0), g, 1, 0, 0);
            Assert.True(BlockCatalogShapes.BadPartReads > before); // теперь гейт ЛОВИТ чужую часть, а не молчит
        }

        [Fact]
        public void GetBoxes_PartBeyondRange_ClampsToAnchor_NoThrow()
        {
            var info = BlockCatalog.Get(TestStructCatalog.Door2x2);
            Assert.Same(info.GetBoxes(0, 0, false), info.GetBoxes(999, 0, false));
            Assert.Same(info.GetBoxes(0, 0, false), info.GetBoxes(-1, 0, false));
        }

        [Fact]
        public void GetBoxes_OpenSet_EveryPartIndexable_NoThrow()
        {
            var info = BlockCatalog.Get(5); // Door_2x2 — openable multi (открытый набор индексируется, длины совпадают)
            for (int p = 0; p < info.PartCount; p++)
                Assert.NotNull(info.GetBoxes(p, 0, true));
            Assert.Same(info.GetBoxes(0, 0, true), info.GetBoxes(999, 0, true));
        }
    }
}
