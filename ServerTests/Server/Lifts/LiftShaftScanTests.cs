using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using LiteNetLib;
using Server.Doors;
using Server.Lifts;
using Server.Network;
using Shared.Configs;
using Shared.Simulation.Blocks;
using Shared.World.Atmos;
using Shared.World.Blocks;

namespace ServerTests.Server.Lifts
{
    internal static class TestLiftCatalog
    {
        internal const ushort Rail = 960;
        internal const ushort RailAlt = 961;
        internal const ushort Cabin = 962;
        internal const ushort CabinNoBoxes = 963;
        internal const ushort CabinZeroSpeed = 964;
        internal const ushort ShaftDoor = 965;
        internal const ushort ShaftDoorTall = 966;
        internal const ushort RailWide = 967;
        internal const ushort CabinWide = 968;
        internal const ushort CabinBadModule = 969;

        // План 4×2 — НЕ квадрат, и бокс асимметричен по обеим осям: только так четыре facing дают
        // четыре РАЗНЫХ AABB. На квадратном модуле с симметричным боксом поворот ненаблюдаем.
        internal const int WideX = 4, WideY = 3, WideZ = 2, WideStep = 3;
        internal const float WideCenterX = 1.25f, WideHalfX = 0.75f;
        internal const float WideCenterZ = 0.625f, WideHalfZ = 0.375f;

        private static readonly TriggerBox[] WideCabinBoxes =
        {
            new TriggerBox(
                (short)((WideCenterX - WideHalfX) * 16f), 8, (short)((WideCenterZ - WideHalfZ) * 16f),
                (short)((WideCenterX + WideHalfX) * 16f), 24, (short)((WideCenterZ + WideHalfZ) * 16f))
        };

        internal const int Module = 3;
        internal const int Step = 3;

        internal const float CabinCenterX = 1.375f;
        internal const float CabinCenterZ = 1.625f;
        internal const float CabinHalfX = 1.25f;
        internal const float CabinHalfZ = 0.875f;
        internal const float CabinBottomY = 0.5f;
        internal const float CabinTopY = 2.5f;

        // Боксы кабины — в УГЛОВОМ object-space модуля [0..Module], как их рисует человек в редакторе.
        // Фикстура АСИММЕТРИЧНА по ОБЕИМ плановым осям (центр модуля 1.5, а тут 1.375 и 1.625) и
        // ПРЯМОУГОЛЬНА (HalfX != HalfZ): симметричный/квадратный бокс не различает ни подмену «угол ↔ центр»,
        // ни перестановку осей — ровно так баг +2.4 клетки и дожил до боевого ассета.
        private static readonly TriggerBox[] CabinBoxes =
        {
            new TriggerBox(
                (short)((CabinCenterX - CabinHalfX) * 16f), (short)(CabinBottomY * 16f),
                (short)((CabinCenterZ - CabinHalfZ) * 16f),
                (short)((CabinCenterX + CabinHalfX) * 16f), (short)(CabinTopY * 16f),
                (short)((CabinCenterZ + CabinHalfZ) * 16f))
        };

        [ModuleInitializer]
        internal static void Seed()
        {
            var field = typeof(BlockCatalog).GetField("_byId", BindingFlags.NonPublic | BindingFlags.Static)!;
            var byId = (Dictionary<ushort, BlockInfo>)field.GetValue(null)!;

            byId[Rail] = Marker(Rail, "TestLiftRail", RailPart());
            byId[RailAlt] = Marker(RailAlt, "TestLiftRailAlt", RailPart());
            byId[Cabin] = Marker(Cabin, "TestLiftCabin", CabinPart(2f, CabinBoxes));
            byId[CabinNoBoxes] = Marker(CabinNoBoxes, "TestLiftCabinNoBoxes", CabinPart(2f, Array.Empty<TriggerBox>()));
            byId[CabinZeroSpeed] = Marker(CabinZeroSpeed, "TestLiftCabinZeroSpeed", CabinPart(0f, CabinBoxes));

            byId[ShaftDoor] = new BlockInfo(ShaftDoor, "TestShaftDoor", BlockCategory.Door,
                BlockFaceFlags.All, BlockFaceFlags.All, 0,
                new[] { new[] { BlockBox.Full } }, 1, 1, 1,
                new[] { Array.Empty<BlockBox>() }, DoorOpening.External, Array.Empty<TriggerBox>(), 0.1f);

            byId[RailWide] = Marker(RailWide, "TestLiftRailWide",
                new LiftPartInfo(LiftPartKind.Rail, WideX, WideY, WideZ, WideStep));
            byId[CabinWide] = Marker(CabinWide, "TestLiftCabinWide",
                new LiftPartInfo(LiftPartKind.Cabin, WideX, WideY, WideZ, WideStep,
                    speedBlocksPerSec: 2f, dwellSec: 1f, doorLeadSec: 0.5f, boxes: WideCabinBoxes));

            // Модуль кабины (Module+1)×Module при рельсе Module×Module — расходится ТОЛЬКО по X,
            // симметричное расхождение не различило бы, какую ось читает проверка.
            byId[CabinBadModule] = Marker(CabinBadModule, "TestLiftCabinBadModule",
                new LiftPartInfo(LiftPartKind.Cabin, Module + 1, Module, Module, Step,
                    speedBlocksPerSec: 2f, dwellSec: 1f, doorLeadSec: 0.5f, boxes: CabinBoxes));

            byId[ShaftDoorTall] = new BlockInfo(ShaftDoorTall, "TestShaftDoorTall", BlockCategory.Door,
                BlockFaceFlags.All, BlockFaceFlags.All, 0,
                new[] { new[] { BlockBox.Full }, new[] { BlockBox.Full } }, 1, 2, 1,
                new[] { Array.Empty<BlockBox>(), Array.Empty<BlockBox>() },
                DoorOpening.External, Array.Empty<TriggerBox>(), 0.1f);
        }

        private static LiftPartInfo RailPart()
            => new LiftPartInfo(LiftPartKind.Rail, Module, Module, Module, Step);

        private static LiftPartInfo CabinPart(float speed, TriggerBox[] boxes)
            => new LiftPartInfo(LiftPartKind.Cabin, Module, Module, Module, Step,
                speedBlocksPerSec: speed, dwellSec: 1f, doorLeadSec: 0.5f, boxes: boxes);

        private static BlockInfo Marker(ushort id, string name, LiftPartInfo lift)
            => new BlockInfo(id, name, BlockCategory.Marker,
                BlockFaceFlags.None, BlockFaceFlags.None, 0,
                new[] { Array.Empty<BlockBox>() }, 1, 1, 1,
                null, DoorOpening.Auto, null, 0f, false, null, false, lift);
    }

    /// <summary>Скан шахт L3b: штабели рельса, привязка кабины, остановки по примыкающим External-дверям, диагностика.</summary>
    public class LiftShaftScanTests
    {
        private const int RX = 10, RZ = 10;

        private sealed class PeerRefComparer : IEqualityComparer<NetPeer>
        {
            public static readonly PeerRefComparer Instance = new();
            public bool Equals(NetPeer? a, NetPeer? b) => ReferenceEquals(a, b);
            public int GetHashCode(NetPeer p) => RuntimeHelpers.GetHashCode(p);
        }

        private readonly BlockGrid _grid = new();

        private void Put(int x, int y, int z, ushort type, int facing = 0)
        {
            _grid.SetBlock(x, y, z, type);
            _grid.SetState(x, y, z, BlockState.WithFacing(0, facing));
        }

        private void Column(int x, int z, int baseY, int floors, ushort type = TestLiftCatalog.Rail, int facing = 0)
        {
            for (int k = 0; k < floors; k++)
                Put(x, baseY + k * TestLiftCatalog.Step, z, type, facing);
        }

        private DoorRegistry Registry()
        {
            var registry = new DoorRegistry();
            registry.Build(_grid);
            return registry;
        }

        private LiftScanResult Scan() => LiftShaftScanner.Scan(_grid, Registry());

        private DoorSystem Doors()
        {
            var doors = new DoorSystem(_grid, new AtmosGrid(), new SVars { TickRate = 30 },
                new Dictionary<NetPeer, ClientConnection>(PeerRefComparer.Instance));
            doors.Build();
            return doors;
        }

        private static string Dump(LiftScanResult r)
            => string.Join(", ", r.Issues.ConvertAll(i => $"{i.Kind}{i.Cell}"));

        private static bool Has(LiftScanResult r, LiftScanIssueKind kind)
        {
            foreach (var issue in r.Issues)
                if (issue.Kind == kind)
                    return true;
            return false;
        }

        [Fact]
        public void ThreeModules_MakeOneShaftWithThreeFloors()
        {
            Column(RX, RZ, 0, 3);
            Put(RX + 1, 0, RZ + 1, TestLiftCatalog.Cabin);

            var r = Scan();

            Assert.True(r.Shafts.Count == 1,
                $"rails={r.RailModules} cabins={r.Cabins} shafts={r.Shafts.Count} issues=[{Dump(r)}]");
            var shaft = r.Shafts[0];
            Assert.Equal(1, shaft.LiftId);
            Assert.Equal(3, shaft.FloorCount);
            Assert.Equal(0, shaft.BaseY);
            Assert.Equal(TestLiftCatalog.Step, shaft.FloorStep);
            Assert.Equal(3, r.RailModules);
            Assert.Equal(1, r.Cabins);
            Assert.False(r.HasErrors);
        }

        [Fact]
        public void ShaftGeometry_ComesFromRailAnchor_NotCabinAnchor()
        {
            Column(RX, RZ, 0, 2);
            Put(RX + 2, 0, RZ + 2, TestLiftCatalog.Cabin);

            var shaft = Assert.Single(Scan().Shafts);

            Assert.Equal(RX, shaft.AnchorX);
            Assert.Equal(RZ, shaft.AnchorZ);
            Assert.Equal(RX, shaft.RailX);
            Assert.Equal(RZ, shaft.RailZ);
        }

        [Fact]
        public void CabinBox_LandsInsideShaftPlan_WithAsymmetricAuthoredCenter()
        {
            Column(RX, RZ, 0, 2);
            Put(RX + 1, 0, RZ + 1, TestLiftCatalog.Cabin);

            var lifts = new LiftSystem(_grid, Doors(), new SVars { TickRate = 30 });
            lifts.Build();

            var runtime = Assert.Single(lifts.Runtimes);
            var set = new DynamicObstacleSet();
            runtime.Tick(0u, set);
            set.Get(0, out float minX, out float minY, out float minZ,
                out float maxX, out float maxY, out float maxZ);

            Assert.Equal(RX + TestLiftCatalog.CabinCenterX, (minX + maxX) * 0.5f, 3);
            Assert.Equal(RZ + TestLiftCatalog.CabinCenterZ, (minZ + maxZ) * 0.5f, 3);
            Assert.Equal(TestLiftCatalog.CabinBottomY, minY, 3);
            Assert.Equal(TestLiftCatalog.CabinTopY, maxY, 3);

            Assert.True(minX >= RX && maxX <= RX + TestLiftCatalog.Module,
                $"кабина вне плана шахты по X: [{minX}..{maxX}] против [{RX}..{RX + TestLiftCatalog.Module}]");
            Assert.True(minZ >= RZ && maxZ <= RZ + TestLiftCatalog.Module,
                $"кабина вне плана шахты по Z: [{minZ}..{maxZ}] против [{RZ}..{RZ + TestLiftCatalog.Module}]");
        }

        [Fact]
        public void DebugStand_CabinStaysCenteredOnConfiguredPosition()
        {
            var config = new SVars
            {
                TickRate = 30,
                DebugLiftEnabled = true,
                DebugLiftX = 5.5f, DebugLiftZ = 7.5f,
                DebugLiftFromY = 1f, DebugLiftToY = 4f,
                DebugLiftSpeed = 0.05f, DebugLiftHalfW = 1f, DebugLiftHeight = 0.25f,
                DebugLiftPauseTicks = 60
            };
            var lifts = new LiftSystem(_grid, Doors(), config);
            lifts.Build();

            var runtime = Assert.Single(lifts.Runtimes);
            var set = new DynamicObstacleSet();
            runtime.Tick(0u, set);
            set.Get(0, out float minX, out float minY, out float minZ, out float maxX, out _, out float maxZ);

            Assert.Equal(5.5f, (minX + maxX) * 0.5f, 3);
            Assert.Equal(7.5f, (minZ + maxZ) * 0.5f, 3);
            Assert.Equal(2f, maxX - minX, 3);
            Assert.Equal(1f, minY, 3);
        }

        // Клетка (1,0) модуля после поворота на facing — свободна при любом facing (рельс стоит в (0,0)).
        private static readonly (int dx, int dz)[] CabinCellByFacing = { (1, 0), (0, -1), (-1, 0), (0, 1) };

        [Fact]
        public void RotatedShaft_IsBuilt_ForEveryFacing()
        {
            for (int facing = 0; facing < 4; facing++)
            {
                var probe = new LiftShaftScanTests();
                probe.Column(RX, RZ, 0, 2, facing: facing);
                var (dx, dz) = CabinCellByFacing[facing];
                probe.Put(RX + dx, 0, RZ + dz, TestLiftCatalog.Cabin, facing);

                var r = probe.Scan();

                Assert.True(r.Shafts.Count == 1,
                    $"facing={facing}: шахта обязана строиться; issues=[{Dump(r)}]");
                Assert.Equal(facing, r.Shafts[0].Facing);
                Assert.False(r.HasErrors, $"facing={facing}: issues=[{Dump(r)}]");
            }
        }

        [Fact]
        public void CabinBoxes_RotateWithShaft_ExactCoordinatesForEveryFacing()
        {
            const float MX = TestLiftCatalog.WideX, MZ = TestLiftCatalog.WideZ;
            const float CX = TestLiftCatalog.WideCenterX, HX = TestLiftCatalog.WideHalfX;
            const float CZ = TestLiftCatalog.WideCenterZ, HZ = TestLiftCatalog.WideHalfZ;

            var expected = new (float cx, float cz, float hx, float hz)[]
            {
                (CX, CZ, HX, HZ),
                (CZ, MX - CX, HZ, HX),
                (MX - CX, MZ - CZ, HX, HZ),
                (MZ - CZ, CX, HZ, HX)
            };

            var seen = new List<(float, float, float, float)>();
            for (int facing = 0; facing < 4; facing++)
            {
                var probe = new LiftShaftScanTests();
                probe.Column(RX, RZ, 0, 2, TestLiftCatalog.RailWide, facing);
                var (dx, dz) = CabinCellByFacing[facing];
                probe.Put(RX + dx, 0, RZ + dz, TestLiftCatalog.CabinWide, facing);

                var shaft = Assert.Single(probe.Scan().Shafts);
                var box = Assert.Single(LiftSystem.ToBoxes(shaft));
                var want = expected[facing];

                Assert.Equal(want.cx, box.CenterX, 3);
                Assert.Equal(want.cz, box.CenterZ, 3);
                Assert.Equal(want.hx, box.HalfX, 3);
                Assert.Equal(want.hz, box.HalfZ, 3);

                float minX = shaft.AnchorX + box.CenterX - box.HalfX;
                float maxX = shaft.AnchorX + box.CenterX + box.HalfX;
                float minZ = shaft.AnchorZ + box.CenterZ - box.HalfZ;
                float maxZ = shaft.AnchorZ + box.CenterZ + box.HalfZ;
                Assert.True(minX >= shaft.PlanX0 && maxX <= shaft.PlanX1 + 1,
                    $"facing={facing}: кабина вне плана по X [{minX}..{maxX}] против [{shaft.PlanX0}..{shaft.PlanX1 + 1}]");
                Assert.True(minZ >= shaft.PlanZ0 && maxZ <= shaft.PlanZ1 + 1,
                    $"facing={facing}: кабина вне плана по Z [{minZ}..{maxZ}] против [{shaft.PlanZ0}..{shaft.PlanZ1 + 1}]");

                seen.Add((box.CenterX, box.CenterZ, box.HalfX, box.HalfZ));
            }

            Assert.Equal(4, new HashSet<(float, float, float, float)>(seen).Count);
        }

        [Fact]
        public void RotatedCabin_ClientSetMatchesServer_EveryTick()
        {
            for (int facing = 1; facing < 4; facing++)
            {
                var probe = new LiftShaftScanTests();
                probe.Column(RX, RZ, 0, 2, TestLiftCatalog.RailWide, facing);
                var (dx, dz) = CabinCellByFacing[facing];
                probe.Put(RX + dx, 0, RZ + dz, TestLiftCatalog.CabinWide, facing);

                var shaft = Assert.Single(probe.Scan().Shafts);
                var runtime = new LiftRuntime(shaft.AnchorX, shaft.AnchorZ, LiftSystem.ToBoxes(shaft),
                    new LiftSegment(shaft.CabinY, shaft.CabinY + 6f, 5u, 0.05f), shaft.LiftId);
                var lifts = new List<LiftRuntime> { runtime };

                var wire = new global::Shared.Messages.Lifts.LiftRegistry();
                wire.Deserialize(LiftRegistryBuilder.Build(lifts).Serialize());
                var segments = new Dictionary<int, LiftSegment> { [runtime.LiftId] = runtime.Segment };

                for (uint tick = 0; tick < 160; tick++)
                {
                    var server = new DynamicObstacleSet();
                    runtime.Tick(tick, server);
                    ServerTests.Shared.Messages.Lifts.LiftRegistryTests.AssertSameBoxes(
                        server,
                        ServerTests.Shared.Messages.Lifts.LiftRegistryTests.ClientSet(wire, segments, tick));
                }
            }
        }

        [Fact]
        public void SharedPlacement_MatchesProductionChain_BitForBit_EveryFacing()
        {
            var authored = BlockCatalog.Get(TestLiftCatalog.CabinWide).Lift.Boxes;

            for (int facing = 0; facing < 4; facing++)
            {
                var probe = new LiftShaftScanTests();
                probe.Column(RX, RZ, 0, 2, TestLiftCatalog.RailWide, facing);
                var (dx, dz) = CabinCellByFacing[facing];
                probe.Put(RX + dx, 0, RZ + dz, TestLiftCatalog.CabinWide, facing);
                var shaft = Assert.Single(probe.Scan().Shafts);

                LiftCabinPlacement.PlanRect(RX, RZ, TestLiftCatalog.WideX, TestLiftCatalog.WideZ, facing,
                    out int x0, out int x1, out int z0, out int z1);
                Assert.Equal(shaft.PlanX0, x0);
                Assert.Equal(shaft.PlanX1, x1);
                Assert.Equal(shaft.PlanZ0, z0);
                Assert.Equal(shaft.PlanZ1, z1);

                var fromEditor = LiftCabinPlacement.LocalBoxes(
                    TestLiftCatalog.WideX, TestLiftCatalog.WideZ, facing, authored);
                var fromServer = LiftSystem.ToBoxes(shaft);

                Assert.Equal(fromServer.Length, fromEditor.Length);
                for (int i = 0; i < fromServer.Length; i++)
                {
                    Assert.Equal(fromServer[i].CenterX, fromEditor[i].CenterX);
                    Assert.Equal(fromServer[i].CenterZ, fromEditor[i].CenterZ);
                    Assert.Equal(fromServer[i].Y, fromEditor[i].Y);
                    Assert.Equal(fromServer[i].HalfX, fromEditor[i].HalfX);
                    Assert.Equal(fromServer[i].HalfZ, fromEditor[i].HalfZ);
                    Assert.Equal(fromServer[i].Height, fromEditor[i].Height);
                }
            }
        }

        [Fact]
        public void EditorWorldBounds_LandOnRuntimeCabin_EveryFacing()
        {
            var authored = BlockCatalog.Get(TestLiftCatalog.CabinWide).Lift.Boxes;

            for (int facing = 0; facing < 4; facing++)
            {
                var probe = new LiftShaftScanTests();
                probe.Column(RX, RZ, 0, 2, TestLiftCatalog.RailWide, facing);
                var (dx, dz) = CabinCellByFacing[facing];
                probe.Put(RX + dx, 0, RZ + dz, TestLiftCatalog.CabinWide, facing);
                var shaft = Assert.Single(probe.Scan().Shafts);

                var runtime = new LiftRuntime(shaft.AnchorX, shaft.AnchorZ, LiftSystem.ToBoxes(shaft),
                    new LiftSegment(shaft.CabinY, shaft.CabinY, 0u, 1f), shaft.LiftId);
                var set = new DynamicObstacleSet();
                runtime.Tick(0u, set);
                set.Get(0, out float rMinX, out float rMinY, out float rMinZ,
                    out float rMaxX, out float rMaxY, out float rMaxZ);

                LiftCabinPlacement.WorldBounds(RX, shaft.CabinY, RZ,
                    TestLiftCatalog.WideX, TestLiftCatalog.WideZ, facing, in authored[0],
                    out float eMinX, out float eMinY, out float eMinZ,
                    out float eMaxX, out float eMaxY, out float eMaxZ);

                Assert.Equal((rMinX, rMinY, rMinZ, rMaxX, rMaxY, rMaxZ),
                    (eMinX, eMinY, eMinZ, eMaxX, eMaxY, eMaxZ));
            }
        }

        [Fact]
        public void CabinBoxes_Rotation_IsCircularOverFourQuarterTurns()
        {
            var boxes = new LiftBox[5];
            for (int step = 0; step < 5; step++)
            {
                int facing = step & 3;
                var probe = new LiftShaftScanTests();
                probe.Column(RX, RZ, 0, 2, TestLiftCatalog.RailWide, facing);
                var (dx, dz) = CabinCellByFacing[facing];
                probe.Put(RX + dx, 0, RZ + dz, TestLiftCatalog.CabinWide, facing);
                boxes[step] = Assert.Single(LiftSystem.ToBoxes(Assert.Single(probe.Scan().Shafts)));
            }

            Assert.Equal(boxes[0].CenterX, boxes[4].CenterX, 3);
            Assert.Equal(boxes[0].CenterZ, boxes[4].CenterZ, 3);
            Assert.Equal(boxes[0].HalfX, boxes[4].HalfX, 3);
            Assert.Equal(boxes[0].HalfZ, boxes[4].HalfZ, 3);
        }

        [Fact]
        public void RotatedShaft_SwapsPlanFootprint_WhenModuleIsNotSquare()
        {
            var probe0 = new LiftShaftScanTests();
            probe0.Column(RX, RZ, 0, 2, TestLiftCatalog.RailWide, 0);
            probe0.Put(RX + 1, 0, RZ, TestLiftCatalog.CabinWide, 0);
            var s0 = Assert.Single(probe0.Scan().Shafts);

            var probe1 = new LiftShaftScanTests();
            probe1.Column(RX, RZ, 0, 2, TestLiftCatalog.RailWide, 1);
            probe1.Put(RX, 0, RZ - 1, TestLiftCatalog.CabinWide, 1);
            var s1 = Assert.Single(probe1.Scan().Shafts);

            Assert.Equal(TestLiftCatalog.WideX, s0.PlanX1 - s0.PlanX0 + 1);
            Assert.Equal(TestLiftCatalog.WideZ, s0.PlanZ1 - s0.PlanZ0 + 1);
            Assert.Equal(TestLiftCatalog.WideZ, s1.PlanX1 - s1.PlanX0 + 1);
            Assert.Equal(TestLiftCatalog.WideX, s1.PlanZ1 - s1.PlanZ0 + 1);
        }

        [Fact]
        public void RotatedCabinUpstairs_DoesNotKillTheShaftBelow()
        {
            const int UpperBase = 9;
            Column(RX, RZ, 0, 2);
            Column(RX, RZ, UpperBase, 2);
            Put(RX + 1, 0, RZ + 1, TestLiftCatalog.Cabin);
            Put(RX + 1, UpperBase, RZ + 1, TestLiftCatalog.Cabin, facing: 2);

            var r = Scan();

            Assert.True(Has(r, LiftScanIssueKind.CabinFacingMismatch));
            var alive = Assert.Single(r.Shafts);
            Assert.Equal(0, alive.BaseY);
        }

        [Fact]
        public void CabinFacingDifferentFromRail_IsRefused()
        {
            Column(RX, RZ, 0, 2);
            Put(RX + 1, 0, RZ + 1, TestLiftCatalog.Cabin, facing: 2);

            var r = Scan();

            Assert.True(Has(r, LiftScanIssueKind.CabinFacingMismatch),
                $"кабина и рельс с разным поворотом — ошибка автора; issues=[{Dump(r)}]");
            Assert.True(r.HasErrors);
            Assert.Empty(r.Shafts);
        }

        [Fact]
        public void StackGap_SplitsIntoTwoShafts()
        {
            Column(RX, RZ, 0, 2);
            Column(RX, RZ, 7, 2);
            Put(RX + 1, 0, RZ + 1, TestLiftCatalog.Cabin);
            Put(RX + 1, 7, RZ + 1, TestLiftCatalog.Cabin);

            var r = Scan();

            Assert.True(Has(r, LiftScanIssueKind.RailStackGap));
            Assert.Equal(2, r.Shafts.Count);
            Assert.Equal(0, r.Shafts[0].BaseY);
            Assert.Equal(7, r.Shafts[1].BaseY);
            Assert.Equal(1, r.Shafts[0].LiftId);
            Assert.Equal(2, r.Shafts[1].LiftId);
        }

        [Fact]
        public void MismatchedRailType_DropsWholeStack()
        {
            Put(RX, 0, RZ, TestLiftCatalog.Rail);
            Put(RX, TestLiftCatalog.Step, RZ, TestLiftCatalog.RailAlt);
            Put(RX + 1, 0, RZ + 1, TestLiftCatalog.Cabin);

            var r = Scan();

            Assert.True(Has(r, LiftScanIssueKind.RailModuleMismatch));
            Assert.True(r.HasErrors);
            Assert.Empty(r.Shafts);
        }

        [Fact]
        public void MismatchedRailFacing_DropsWholeStack()
        {
            Put(RX, 0, RZ, TestLiftCatalog.Rail);
            Put(RX, TestLiftCatalog.Step, RZ, TestLiftCatalog.Rail, facing: 1);
            Put(RX + 1, 0, RZ + 1, TestLiftCatalog.Cabin);

            var r = Scan();

            Assert.True(Has(r, LiftScanIssueKind.RailModuleMismatch));
            Assert.Empty(r.Shafts);
        }

        [Fact]
        public void CabinNotOnFloor_IsError()
        {
            Column(RX, RZ, 0, 3);
            Put(RX + 1, 1, RZ + 1, TestLiftCatalog.Cabin);

            var r = Scan();

            Assert.True(Has(r, LiftScanIssueKind.CabinOffFloor));
            Assert.Empty(r.Shafts);
        }

        [Fact]
        public void CabinWithoutRail_IsError()
        {
            Put(RX + 40, 0, RZ + 40, TestLiftCatalog.Cabin);

            var r = Scan();

            Assert.True(Has(r, LiftScanIssueKind.CabinWithoutRail));
            Assert.Empty(r.Shafts);
        }

        [Fact]
        public void CabinWithEmptyBoxes_IsError()
        {
            Column(RX, RZ, 0, 2);
            Put(RX + 1, 0, RZ + 1, TestLiftCatalog.CabinNoBoxes);

            var r = Scan();

            Assert.True(Has(r, LiftScanIssueKind.CabinBoxesEmpty));
            Assert.Empty(r.Shafts);
        }

        [Fact]
        public void CabinWithZeroSpeed_IsError()
        {
            Column(RX, RZ, 0, 2);
            Put(RX + 1, 0, RZ + 1, TestLiftCatalog.CabinZeroSpeed);

            var r = Scan();

            Assert.True(Has(r, LiftScanIssueKind.CabinBadSpeed));
            Assert.Empty(r.Shafts);
        }

        [Fact]
        public void ShaftWithoutCabin_IsWarningAndNoLift()
        {
            Column(RX, RZ, 0, 3);

            var r = Scan();

            Assert.True(Has(r, LiftScanIssueKind.ShaftWithoutCabin));
            Assert.False(r.HasErrors);
            Assert.Empty(r.Shafts);
        }

        [Fact]
        public void AdjacentDoor_BecomesStop()
        {
            Column(RX, RZ, 0, 2);
            Put(RX + 1, 0, RZ + 1, TestLiftCatalog.Cabin);
            Put(RX + 1, 0, RZ - 1, TestLiftCatalog.ShaftDoor);

            var r = Scan();

            var shaft = Assert.Single(r.Shafts);
            var stop = Assert.Single(shaft.Stops);
            Assert.Equal(0, stop.FloorIndex);
            Assert.True(stop.HasDoor, "дверь есть в реестре — ключ обязан быть");
            Assert.Equal(1, r.Stops);
        }

        [Fact]
        public void DoorTwoCellsAway_IsOrphan()
        {
            Column(RX, RZ, 0, 2);
            Put(RX + 1, 0, RZ + 1, TestLiftCatalog.Cabin);
            Put(RX + 1, 0, RZ - 2, TestLiftCatalog.ShaftDoor);

            var r = Scan();

            Assert.True(Has(r, LiftScanIssueKind.OrphanShaftDoor));
            Assert.Empty(r.Shafts[0].Stops);
            Assert.True(Has(r, LiftScanIssueKind.ShaftWithoutStops));
        }

        [Fact]
        public void DoorAboveTopFloor_IsOffFloor()
        {
            Column(RX, RZ, 0, 2);
            Put(RX + 1, 0, RZ + 1, TestLiftCatalog.Cabin);
            Put(RX + 1, 9, RZ - 1, TestLiftCatalog.ShaftDoor);

            var r = Scan();

            Assert.True(Has(r, LiftScanIssueKind.DoorOffFloor));
            Assert.Empty(r.Shafts[0].Stops);
        }

        [Fact]
        public void DoorMissingFromRegistry_IsWarningButStopKept()
        {
            Column(RX, RZ, 0, 2);
            Put(RX + 1, 0, RZ + 1, TestLiftCatalog.Cabin);
            Put(RX + 1, 0, RZ - 1, TestLiftCatalog.ShaftDoor);

            var r = LiftShaftScanner.Scan(_grid, null);

            Assert.True(Has(r, LiftScanIssueKind.DoorNotRegistered));
            var stop = Assert.Single(r.Shafts[0].Stops);
            Assert.False(stop.HasDoor);
        }

        [Fact]
        public void TouchingShafts_Overlap()
        {
            Column(RX, RZ, 0, 2);
            Column(RX + TestLiftCatalog.Module - 1, RZ, 0, 2);
            Put(RX + 1, 0, RZ + 1, TestLiftCatalog.Cabin);

            var r = Scan();

            Assert.True(Has(r, LiftScanIssueKind.ShaftFootprintOverlap));
            Assert.True(r.HasErrors);
            Assert.Empty(r.Shafts);
        }

        [Fact]
        public void SharedDoor_IsClaimedByTwoShafts()
        {
            const int GapZ = RZ + TestLiftCatalog.Module + 1;
            Column(RX, RZ, 0, 2);
            Column(RX, GapZ, 0, 2);
            Put(RX + 1, 0, RZ + 1, TestLiftCatalog.Cabin);
            Put(RX + 1, 0, GapZ + 1, TestLiftCatalog.Cabin);
            Put(RX + 1, 0, RZ + TestLiftCatalog.Module, TestLiftCatalog.ShaftDoor);
            Put(RX - 1, 0, RZ + 1, TestLiftCatalog.ShaftDoor);
            Put(RX - 1, 0, GapZ + 1, TestLiftCatalog.ShaftDoor);

            var r = Scan();

            Assert.Equal(2, r.Shafts.Count);
            Assert.True(Has(r, LiftScanIssueKind.DoorClaimedByTwoShafts));

            var ownA = Assert.Single(r.Shafts[0].Stops);
            var ownB = Assert.Single(r.Shafts[1].Stops);
            Assert.Equal(RZ + 1, ownA.DoorAnchor.Z);
            Assert.Equal(GapZ + 1, ownB.DoorAnchor.Z);
            Assert.Equal(2, r.Stops);
        }

        [Fact]
        public void TooManyFloors_TruncatesTop()
        {
            Column(RX, RZ, 0, LiftShaftScanner.MaxFloors + 2);
            Put(RX + 1, 0, RZ + 1, TestLiftCatalog.Cabin);

            var r = Scan();

            Assert.True(Has(r, LiftScanIssueKind.TooManyFloors));
            Assert.Equal(LiftShaftScanner.MaxFloors, Assert.Single(r.Shafts).FloorCount);
        }

        [Fact]
        public void StacksClosterThanModuleHeight_Overlap()
        {
            Column(RX, RZ, 0, 2);
            Put(RX, TestLiftCatalog.Module + 2, RZ, TestLiftCatalog.Rail);
            Put(RX, TestLiftCatalog.Module + 2 + TestLiftCatalog.Step, RZ, TestLiftCatalog.Rail);
            Put(RX + 1, 0, RZ + 1, TestLiftCatalog.Cabin);
            Put(RX + 1, TestLiftCatalog.Module + 2, RZ + 1, TestLiftCatalog.Cabin);

            var r = Scan();

            Assert.True(Has(r, LiftScanIssueKind.RailStackGap), "предусловие: зазор ломает штабель на два");
            Assert.True(Has(r, LiftScanIssueKind.ShaftFootprintOverlap),
                $"объём модуля нижней шахты (Y0..Y{TestLiftCatalog.Step + TestLiftCatalog.Module - 1}) " +
                $"накрывает якорь верхней (Y{TestLiftCatalog.Module + 2}) — обязано быть перекрытие; issues=[{Dump(r)}]");
            Assert.True(r.HasErrors);
            Assert.Empty(r.Shafts);
        }

        [Fact]
        public void StackedShafts_DoNotBothClaimTheSameDoor()
        {
            const int UpperBase = 9;
            Column(RX, RZ, 0, 2);
            Column(RX, RZ, UpperBase, 2);
            Put(RX + 1, 0, RZ + 1, TestLiftCatalog.Cabin);
            Put(RX + 1, UpperBase, RZ + 1, TestLiftCatalog.Cabin);
            Put(RX + 1, 0, RZ - 1, TestLiftCatalog.ShaftDoor);
            Put(RX + 1, UpperBase, RZ - 1, TestLiftCatalog.ShaftDoor);

            var r = Scan();

            Assert.Equal(2, r.Shafts.Count);
            Assert.False(Has(r, LiftScanIssueKind.DoorClaimedByTwoShafts),
                $"шахты стоят одна над другой — план общий, но этажи разные; issues=[{Dump(r)}]");
            var lower = Assert.Single(r.Shafts[0].Stops);
            var upper = Assert.Single(r.Shafts[1].Stops);
            Assert.Equal(0, lower.DoorAnchor.Y);
            Assert.Equal(UpperBase, upper.DoorAnchor.Y);
        }

        [Fact]
        public void OrphanExternalDoorSavedOpen_IsAlsoClosedByBuild()
        {
            int dx = RX + 40, dy = 0, dz = RZ + 40;
            Put(dx, dy, dz, TestLiftCatalog.ShaftDoor);
            _grid.SetState(dx, dy, dz, BlockState.WithOpen(_grid.GetState(dx, dy, dz), true));

            var doors = Doors();
            var lifts = new LiftSystem(_grid, doors);
            lifts.Build();

            Assert.Empty(lifts.Result.Shafts);
            Assert.False(BlockState.GetOpen(_grid.GetState(dx, dy, dz)),
                "B4 закрывает ВСЕ External-двери мира, а не только двери живых шахт — иначе атмос разметит " +
                "помещение за брошенной дверью как Space");
            Assert.Equal(1, lifts.NormalizedDoors);
        }

        [Fact]
        public void MultiCellExternalDoor_IsClosedInEveryCell()
        {
            int dx = RX + 40, dy = 0, dz = RZ + 40;
            var info = BlockCatalog.Get(TestLiftCatalog.ShaftDoorTall);
            Assert.True(_grid.PlaceMultiBlock(dx, dy, dz, TestLiftCatalog.ShaftDoorTall, 0));
            int parts = MultiBlock.PartCount(info.SizeX, info.SizeY, info.SizeZ);
            Assert.True(parts > 1, "предусловие: дверь обязана быть мульти-блочной");
            for (int p = 0; p < parts; p++)
            {
                MultiBlock.PartWorldOffset(p, info.SizeX, info.SizeZ, 0, out int ox, out int oy, out int oz);
                _grid.SetState(dx + ox, dy + oy, dz + oz,
                    BlockState.WithOpen(_grid.GetState(dx + ox, dy + oy, dz + oz), true));
            }

            var lifts = new LiftSystem(_grid, Doors());
            lifts.Build();

            for (int p = 0; p < parts; p++)
            {
                MultiBlock.PartWorldOffset(p, info.SizeX, info.SizeZ, 0, out int ox, out int oy, out int oz);
                Assert.False(BlockState.GetOpen(_grid.GetState(dx + ox, dy + oy, dz + oz)),
                    $"часть {p} осталась открытой: атмос читает бит Open ПОКЛЕТОЧНО, закрытия одного якоря мало");
            }
        }

        [Fact]
        public void CabinInsideRejectedShaft_IsNotReportedAsCabinWithoutRail()
        {
            Put(RX, 0, RZ, TestLiftCatalog.Rail);
            Put(RX, TestLiftCatalog.Step, RZ, TestLiftCatalog.RailAlt);
            Put(RX + 1, 0, RZ + 1, TestLiftCatalog.Cabin);

            var r = Scan();

            Assert.True(Has(r, LiftScanIssueKind.RailModuleMismatch));
            Assert.True(Has(r, LiftScanIssueKind.CabinInBrokenShaft),
                $"рельс под кабиной ЕСТЬ, шахта просто отбракована; issues=[{Dump(r)}]");
            Assert.False(Has(r, LiftScanIssueKind.CabinWithoutRail));
        }

        [Fact]
        public void TooManyFloors_KeepsOneStack_AndReportsRealCount()
        {
            const int Extra = 3;
            Column(RX, RZ, 0, LiftShaftScanner.MaxFloors + Extra);
            Put(RX + 1, 0, RZ + 1, TestLiftCatalog.Cabin);

            var r = Scan();

            Assert.True(Has(r, LiftScanIssueKind.TooManyFloors));
            var shaft = Assert.Single(r.Shafts);
            Assert.Equal(LiftShaftScanner.MaxFloors, shaft.FloorCount);
            Assert.False(Has(r, LiftScanIssueKind.RailStackGap),
                "лишние модули отбрасываются, но НЕ начинают новый штабель");
            foreach (var issue in r.Issues)
                if (issue.Kind == LiftScanIssueKind.TooManyFloors)
                    Assert.Equal(LiftShaftScanner.MaxFloors + Extra, issue.A);
        }

        [Fact]
        public void CabinModuleMismatch_IsCaughtAtScan_NotAtRuntime()
        {
            // План шахты строится по модулю РЕЛЬСА, боксы приходят от КАБИНЫ. Разные модули = визуал и
            // коллизия разъедутся молча, поэтому шахта обязана отбраковаться при загрузке карты.
            Column(RX, RZ, 0, 3);
            Put(RX + 1, 0, RZ + 1, TestLiftCatalog.CabinBadModule);

            var r = Scan();

            Assert.True(Has(r, LiftScanIssueKind.CabinModuleMismatch), Dump(r));
            Assert.Empty(r.Shafts);
            Assert.True(r.HasErrors, "расхождение модулей — ошибка, а не предупреждение");
        }

        [Fact]
        public void MatchingModules_ProduceNoMismatchIssue()
        {
            Column(RX, RZ, 0, 3);
            Put(RX + 1, 0, RZ + 1, TestLiftCatalog.Cabin);

            var r = Scan();

            Assert.False(Has(r, LiftScanIssueKind.CabinModuleMismatch), Dump(r));
            Assert.Single(r.Shafts);
        }

        [Fact]
        public void CabinModuleMismatch_ReportNamesBothModules()
        {
            var issue = new LiftScanIssue(LiftScanIssueKind.CabinModuleMismatch, new LiftCell(1, 2, 3),
                4 * 256 + 2, 3 * 256 + 3);
            string text = LiftScanReport.Describe(issue);

            Assert.Contains("4×2", text);
            Assert.Contains("3×3", text);
        }

        // Конфиг БЕЗ единого отладочного ключа: движение шахты обязано зависеть только от заказа с панели.
        private LiftSystem ScannedShaft(int floors, params int[] doorFloors)
        {
            Column(RX, RZ, 0, floors);
            Put(RX + 1, 0, RZ + 1, TestLiftCatalog.Cabin);
            foreach (int k in doorFloors)
                Put(RX + 1, k * TestLiftCatalog.Step, RZ - 1, TestLiftCatalog.ShaftDoor);

            var lifts = new LiftSystem(_grid, Doors(), new SVars { TickRate = 30 });
            lifts.Build();
            return lifts;
        }

        private static bool TickUntil(LiftSystem lifts, ref uint tick, int budget, Func<bool> done)
        {
            for (int i = 0; i < budget; i++, tick++)
            {
                lifts.Tick(tick);
                if (done())
                    return true;
            }
            return false;
        }

        [Fact]
        public void PanelRequest_MovesScannedShaft_BetweenServedFloors()
        {
            var lifts = ScannedShaft(floors: 4, doorFloors: new[] { 0, 3 });
            var controller = Assert.Single(lifts.Controllers);
            Assert.Equal(0, controller.CurrentFloor);

            uint tick = 0u;
            Assert.True(controller.Request(3));
            Assert.True(TickUntil(lifts, ref tick, 4000, () => controller.CurrentFloor == 3),
                "заказ с панели не довёз кабину на верхний обслуживаемый этаж СКАНИРОВАННОЙ шахты");

            Assert.True(controller.Request(0));
            Assert.True(TickUntil(lifts, ref tick, 4000, () => controller.CurrentFloor == 0),
                "обратный заказ не довёз кабину вниз");
        }

        [Fact]
        public void ScannedShaft_FloorWithoutDoor_IsNotRequestable()
        {
            // Скан обязан пометить этаж 3 НЕобслуживаемым: дверей там нет. Заказ на него — отказ,
            // и кабина остаётся на месте (решение B1: массивы длиной FloorCount, а не числом дверей).
            var lifts = ScannedShaft(floors: 4, doorFloors: new[] { 0, 2 });
            var controller = Assert.Single(lifts.Controllers);

            Assert.False(controller.IsServed(3));
            Assert.False(controller.Request(3));

            uint tick = 0u;
            Assert.False(TickUntil(lifts, ref tick, 600, () => controller.CurrentFloor != 0),
                "отказанный заказ не должен двигать кабину");

            Assert.True(controller.Request(2));
            Assert.True(TickUntil(lifts, ref tick, 4000, () => controller.CurrentFloor == 2));
        }

        [Fact]
        public void ScannedShaft_SingleServedFloor_RejectsEverythingElse()
        {
            // Кабина стоит на этаже 0, а дверь только на 1: парковка на НЕобслуживаемом этаже — штатная.
            var lifts = ScannedShaft(floors: 4, doorFloors: new[] { 1 });
            var controller = Assert.Single(lifts.Controllers);

            Assert.Equal(0, controller.CurrentFloor);
            Assert.False(controller.IsServed(0));
            Assert.True(controller.IsServed(1));

            Assert.False(controller.Request(0));
            Assert.False(controller.Request(2));
            Assert.False(controller.Request(3));

            uint tick = 0u;
            Assert.False(TickUntil(lifts, ref tick, 600, () => controller.CurrentFloor != 0),
                "без единого принятого заказа кабина обязана стоять");

            Assert.True(controller.Request(1));
            Assert.True(TickUntil(lifts, ref tick, 4000, () => controller.CurrentFloor == 1));
        }

        [Fact]
        public void TestCatalogIds_AreNotStolenByAnotherTestCatalog()
        {
            var rail = BlockCatalog.Get(TestLiftCatalog.Rail);
            var cabin = BlockCatalog.Get(TestLiftCatalog.Cabin);
            var door = BlockCatalog.Get(TestLiftCatalog.ShaftDoor);

            Assert.True(rail.Lift != null,
                $"id {TestLiftCatalog.Rail} занят чужим тестовым каталогом ('{rail.Name}'): порядок [ModuleInitializer] " +
                "не определён, победитель случаен — возьми свободный диапазон id");
            Assert.Equal(LiftPartKind.Rail, rail.Lift.Kind);
            Assert.Equal(LiftPartKind.Cabin, cabin.Lift.Kind);
            Assert.Equal(DoorOpening.External, door.Opening);
        }

        [Fact]
        public void EmptyWorld_YieldsNothing()
        {
            var r = Scan();

            Assert.Empty(r.Shafts);
            Assert.Empty(r.Issues);
            Assert.Equal(0, r.RailModules);
        }

        [Fact]
        public void ScanIsDeterministic_LiftIdsStableAcrossRuns()
        {
            Column(RX + 20, RZ + 20, 0, 2);
            Column(RX, RZ, 0, 2);
            Put(RX + 21, 0, RZ + 21, TestLiftCatalog.Cabin);
            Put(RX + 1, 0, RZ + 1, TestLiftCatalog.Cabin);

            var a = Scan();
            var b = Scan();

            Assert.Equal(2, a.Shafts.Count);
            for (int i = 0; i < a.Shafts.Count; i++)
            {
                Assert.Equal(a.Shafts[i].LiftId, b.Shafts[i].LiftId);
                Assert.Equal(a.Shafts[i].RailX, b.Shafts[i].RailX);
                Assert.Equal(a.Shafts[i].RailZ, b.Shafts[i].RailZ);
            }
            Assert.Equal(RX, a.Shafts[0].RailX);
        }

        [Fact]
        public void ExternalDoorSavedOpen_IsClosedByBuild()
        {
            Column(RX, RZ, 0, 2);
            Put(RX + 1, 0, RZ + 1, TestLiftCatalog.Cabin);
            int dx = RX + 1, dy = 0, dz = RZ - 1;
            Put(dx, dy, dz, TestLiftCatalog.ShaftDoor);
            _grid.SetState(dx, dy, dz, BlockState.WithOpen(_grid.GetState(dx, dy, dz), true));
            Assert.True(BlockState.GetOpen(_grid.GetState(dx, dy, dz)), "предусловие: дверь сохранена открытой");

            var doors = new DoorSystem(_grid, new AtmosGrid(), new SVars { TickRate = 30 },
                new Dictionary<NetPeer, ClientConnection>(PeerRefComparer.Instance));
            doors.Build();
            var lifts = new LiftSystem(_grid, doors);
            lifts.Build();

            Assert.False(BlockState.GetOpen(_grid.GetState(dx, dy, dz)),
                "B4: External-дверь обязана быть нормализована в закрытую до AtmosInit.Classify");
            Assert.Equal(1, lifts.NormalizedDoors);
            Assert.True(Has(lifts.Result, LiftScanIssueKind.DoorSavedOpen));
        }

        [Fact]
        public void ExternalDoor_IsIgnoredByDoorAutomation()
        {
            int dx = RX + 1, dy = 0, dz = RZ - 1;
            Put(dx, dy, dz, TestLiftCatalog.ShaftDoor);

            var doors = new DoorSystem(_grid, new AtmosGrid(), new SVars { TickRate = 30 },
                new Dictionary<NetPeer, ClientConnection>(PeerRefComparer.Instance));
            var stats = doors.Build();
            Assert.Equal(1, stats.External);
            Assert.True(doors.TryGetAnchorKey(dx, dy, dz, out long key));
            Assert.Equal(DoorCommandResult.Applied, doors.TrySetOpen(key, true));

            for (uint t = 0; t < 120; t++)
                doors.Tick(t);

            Assert.True(BlockState.GetOpen(_grid.GetState(dx, dy, dz)),
                "автомат дверей не имеет права закрывать External-дверь — ей командует только машина");
        }
    }
}
