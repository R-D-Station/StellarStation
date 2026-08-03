using System;
using System.Collections.Generic;
using Server.Lifts;
using Shared.Messages.Lifts;
using Shared.Simulation.Blocks;

namespace ServerTests.Shared.Messages.Lifts
{
    /// <summary>Геометрия лифтов по проводу: round-trip реестра и совпадение клиентского набора боксов с серверным.</summary>
    public class LiftRegistryTests
    {
        [Fact]
        public void RoundTrip_MultipleLifts_MultipleBoxes()
        {
            var src = new LiftRegistry
            {
                Lifts = new[]
                {
                    new LiftRegistryEntry
                    {
                        LiftId = 1, AnchorX = 5.5f, AnchorZ = -6.5f,
                        Boxes = new[]
                        {
                            new LiftCabinBox(0f, 0f, 0f, 2.5f, 1.75f, 0.25f),
                            new LiftCabinBox(0f, 2f, 0f, 2.5f, 1.75f, 0.25f)
                        },
                        DoorLeadTicks = 5u,
                        CabinDefId = 4271, PlanW = 6, PlanD = 5, Facing = 3,
                        Stops = new[] { new LiftStopEntry(1, 4.75f, 13, -5, 62) }
                    },
                    new LiftRegistryEntry
                    {
                        LiftId = 7, AnchorX = 100f, AnchorZ = 0.5f,
                        Boxes = new[] { new LiftCabinBox(1f, -0.5f, -1f, 1f, 4f, 3f) },
                        DoorLeadTicks = 17u,
                        CabinDefId = 0, PlanW = 3, PlanD = 9, Facing = 1,
                        Stops = new[] { new LiftStopEntry(0, -3.5f, 7, 19, -24), LiftStopEntry.WithoutDoor(2, 11.25f) }
                    }
                }
            };

            var dst = new LiftRegistry();
            dst.Deserialize(src.Serialize());

            Assert.NotNull(dst.Lifts);
            Assert.Equal(2, dst.Lifts!.Length);
            for (int i = 0; i < src.Lifts.Length; i++)
            {
                Assert.Equal(src.Lifts[i].LiftId, dst.Lifts[i].LiftId);
                Assert.Equal(src.Lifts[i].AnchorX, dst.Lifts[i].AnchorX);
                Assert.Equal(src.Lifts[i].AnchorZ, dst.Lifts[i].AnchorZ);
                Assert.Equal(src.Lifts[i].DoorLeadTicks, dst.Lifts[i].DoorLeadTicks);
                Assert.Equal(src.Lifts[i].CabinDefId, dst.Lifts[i].CabinDefId);
                Assert.Equal(src.Lifts[i].PlanW, dst.Lifts[i].PlanW);
                Assert.Equal(src.Lifts[i].PlanD, dst.Lifts[i].PlanD);
                // Facing 1 и 3 дают ОДИН план 5×6 — их различает только это поле.
                Assert.Equal(src.Lifts[i].Facing, dst.Lifts[i].Facing);
                Assert.Equal(src.Lifts[i].Stops.Length, dst.Lifts[i].Stops.Length);
                for (int t = 0; t < src.Lifts[i].Stops.Length; t++)
                {
                    Assert.Equal(src.Lifts[i].Stops[t].Floor, dst.Lifts[i].Stops[t].Floor);
                    Assert.Equal(src.Lifts[i].Stops[t].Y, dst.Lifts[i].Stops[t].Y);
                    Assert.Equal(src.Lifts[i].Stops[t].DoorX, dst.Lifts[i].Stops[t].DoorX);
                    Assert.Equal(src.Lifts[i].Stops[t].DoorY, dst.Lifts[i].Stops[t].DoorY);
                    Assert.Equal(src.Lifts[i].Stops[t].DoorZ, dst.Lifts[i].Stops[t].DoorZ);
                    Assert.Equal(src.Lifts[i].Stops[t].HasDoor, dst.Lifts[i].Stops[t].HasDoor);
                }
                Assert.Equal(src.Lifts[i].Boxes.Length, dst.Lifts[i].Boxes.Length);
                for (int b = 0; b < src.Lifts[i].Boxes.Length; b++)
                {
                    Assert.Equal(src.Lifts[i].Boxes[b].CenterX, dst.Lifts[i].Boxes[b].CenterX);
                    Assert.Equal(src.Lifts[i].Boxes[b].Y, dst.Lifts[i].Boxes[b].Y);
                    Assert.Equal(src.Lifts[i].Boxes[b].CenterZ, dst.Lifts[i].Boxes[b].CenterZ);
                    Assert.Equal(src.Lifts[i].Boxes[b].HalfX, dst.Lifts[i].Boxes[b].HalfX);
                    Assert.Equal(src.Lifts[i].Boxes[b].HalfZ, dst.Lifts[i].Boxes[b].HalfZ);
                    Assert.Equal(src.Lifts[i].Boxes[b].Height, dst.Lifts[i].Boxes[b].Height);
                }
            }
        }

        [Fact]
        public void RoundTrip_Empty()
        {
            var dst = new LiftRegistry();
            dst.Deserialize(new LiftRegistry { Lifts = Array.Empty<LiftRegistryEntry>() }.Serialize());
            Assert.NotNull(dst.Lifts);
            Assert.Empty(dst.Lifts!);
        }

        [Fact]
        public void Deserialize_Null_Throws()
            => Assert.Throws<ArgumentNullException>(() => new LiftRegistry().Deserialize(null!));

        [Fact]
        public void Deserialize_TrailingBytes_Throws()
        {
            var bytes = new LiftRegistry { Lifts = Array.Empty<LiftRegistryEntry>() }.Serialize();
            var padded = new byte[bytes.Length + 1];
            bytes.CopyTo(padded, 0);
            Assert.Throws<InvalidOperationException>(() => new LiftRegistry().Deserialize(padded));
        }

        [Fact]
        public void Deserialize_NaNAnchor_Throws()
        {
            var bytes = new LiftRegistry
            {
                Lifts = new[] { new LiftRegistryEntry { LiftId = 1, AnchorX = 1f, AnchorZ = 2f, Boxes = Array.Empty<LiftCabinBox>() } }
            }.Serialize();
            BitConverter.GetBytes(float.NaN).CopyTo(bytes, 2 + 4); // после count(2) и LiftId(4) — AnchorX
            Assert.Throws<InvalidOperationException>(() => new LiftRegistry().Deserialize(bytes));
        }

        [Fact]
        public void Deserialize_LiftCountOverCap_Throws_BeforeAllocating()
        {
            var bytes = new byte[2];
            BitConverter.GetBytes((ushort)(LiftRegistry.MaxLifts + 1)).CopyTo(bytes, 0);
            Assert.Throws<InvalidOperationException>(() => new LiftRegistry().Deserialize(bytes));
        }

        // Клиентская сборка боксов (зеркало Networkrunner.PrepareSimObstacles): геометрия из реестра + сегмент по LiftId.
        internal static DynamicObstacleSet ClientSet(LiftRegistry registry, Dictionary<int, LiftSegment> segments, uint tick)
        {
            var set = new DynamicObstacleSet();
            foreach (var kv in segments)
            {
                LiftRegistryEntry geom = default;
                bool known = false;
                for (int i = 0; i < registry.Lifts.Length; i++)
                    if (registry.Lifts[i].LiftId == kv.Key) { geom = registry.Lifts[i]; known = true; break; }
                if (!known || geom.Boxes == null) continue;

                var seg = kv.Value;
                float y = LiftTrajectory.YAt(in seg, tick);
                float delta = LiftTrajectory.DeltaAt(in seg, tick);
                for (int b = 0; b < geom.Boxes.Length; b++)
                {
                    var box = geom.Boxes[b];
                    set.Add(geom.AnchorX + box.CenterX, y + box.Y, geom.AnchorZ + box.CenterZ, box.HalfX, box.HalfZ, box.Height, delta);
                }
            }
            return set;
        }

        internal static void AssertSameBoxes(DynamicObstacleSet expected, DynamicObstacleSet actual)
        {
            Assert.Equal(expected.Count, actual.Count);
            for (int i = 0; i < expected.Count; i++)
            {
                expected.Get(i, out float eMinX, out float eMinY, out float eMinZ, out float eMaxX, out float eMaxY, out float eMaxZ);
                actual.Get(i, out float aMinX, out float aMinY, out float aMinZ, out float aMaxX, out float aMaxY, out float aMaxZ);
                Assert.Equal((eMinX, eMinY, eMinZ, eMaxX, eMaxY, eMaxZ), (aMinX, aMinY, aMinZ, aMaxX, aMaxY, aMaxZ));
                Assert.Equal(expected.GetDeltaY(i), actual.GetDeltaY(i));
            }
        }

        [Fact]
        public void TwoLiftsDifferentAnchors_ClientSetMatchesServer_EveryTick()
        {
            var liftA = new LiftRuntime(5.5f, 5.5f,
                new[] { new LiftBox(0f, 0f, 0f, 1f, 2.75f, 0.25f), new LiftBox(0f, 2.5f, 0f, 1f, 2.75f, 0.25f) },
                new LiftSegment(1f, 6f, 10, 0.05f), liftId: 1);
            var liftB = new LiftRuntime(-20.5f, 33.5f,
                new[] { new LiftBox(0.5f, 0f, -0.5f, 2.5f, 0.75f, 0.5f) },
                new LiftSegment(9f, 2f, 40, 0.125f), liftId: 2);
            var lifts = new List<LiftRuntime> { liftA, liftB };

            var registry = LiftRegistryBuilder.Build(lifts);
            var wire = new LiftRegistry();
            wire.Deserialize(registry.Serialize()); // ЧЕРЕЗ провод: ловим и потери сериализации

            var segments = new Dictionary<int, LiftSegment>();
            foreach (var l in lifts) segments[l.LiftId] = l.Segment;

            for (uint tick = 0; tick < 200; tick++)
            {
                var server = new DynamicObstacleSet();
                foreach (var l in lifts) l.Tick(tick, server);

                AssertSameBoxes(server, ClientSet(wire, segments, tick));
            }
        }

        [Fact]
        public void LiftWithoutRegistryEntry_ProducesNoBoxes_AndDoesNotThrow()
        {
            var wire = new LiftRegistry { Lifts = Array.Empty<LiftRegistryEntry>() };
            var segments = new Dictionary<int, LiftSegment> { [42] = new LiftSegment(0f, 5f, 0, 0.05f) };

            var set = ClientSet(wire, segments, 10);

            Assert.Equal(0, set.Count);
        }

        [Fact]
        public void Builder_CopiesAnchorAndBoxes_FromRuntime()
        {
            var lift = new LiftRuntime(3.25f, -7.75f,
                new[] { new LiftBox(1f, 2f, 3f, 4f, 6f, 5f) },
                new LiftSegment(0f, 1f, 0, 0.1f), liftId: 9);

            var reg = LiftRegistryBuilder.Build(new List<LiftRuntime> { lift });

            Assert.Single(reg.Lifts);
            Assert.Equal(9, reg.Lifts[0].LiftId);
            Assert.Equal(3.25f, reg.Lifts[0].AnchorX);
            Assert.Equal(-7.75f, reg.Lifts[0].AnchorZ);
            Assert.Single(reg.Lifts[0].Boxes);
            Assert.Equal(1f, reg.Lifts[0].Boxes[0].CenterX);
            Assert.Equal(2f, reg.Lifts[0].Boxes[0].Y);
            Assert.Equal(3f, reg.Lifts[0].Boxes[0].CenterZ);
            Assert.Equal(4f, reg.Lifts[0].Boxes[0].HalfX);
            Assert.Equal(6f, reg.Lifts[0].Boxes[0].HalfZ);
            Assert.Equal(5f, reg.Lifts[0].Boxes[0].Height);
        }

        [Fact]
        public void Builder_CarriesCabinPrefabPlanAndFacing_FromTheShaft()
        {
            var lift = new LiftRuntime(10f, 20f, new[] { new LiftBox(1f, 2f, 3f, 4f, 6f, 5f) },
                new LiftSegment(0f, 1f, 0, 0.1f), liftId: 9);
            var shaft = new LiftShaftSpec
            {
                LiftId = 9, Facing = 3, CabinType = 4242,
                PlanX0 = 10, PlanX1 = 15, PlanZ0 = 20, PlanZ1 = 24
            };

            var reg = LiftRegistryBuilder.Build(new List<LiftRuntime> { lift }, null,
                new List<LiftShaftSpec> { shaft });

            Assert.Equal(4242, reg.Lifts[0].CabinDefId);
            Assert.Equal(6, reg.Lifts[0].PlanW);
            Assert.Equal(5, reg.Lifts[0].PlanD);
            Assert.Equal(3, reg.Lifts[0].Facing);
        }

        [Fact]
        public void Builder_WithoutShaft_EmitsZeroPlan_ClientClampsIt()
        {
            // Штатный путь: Build без шахт (живые тесты, стенд). Ноль здесь безопасен — кламп на клиенте
            // (PivotFromPlan / ColumnRange), сервер не выдумывает несуществующую геометрию.
            var lift = new LiftRuntime(1f, 2f, new[] { new LiftBox(0f, 0f, 0f, 1f, 1f, 1f) },
                new LiftSegment(0f, 1f, 0, 0.1f), liftId: 5);

            var reg = LiftRegistryBuilder.Build(new List<LiftRuntime> { lift });

            Assert.Equal(0, reg.Lifts[0].CabinDefId);
            Assert.Equal(0, reg.Lifts[0].PlanW);
            Assert.Equal(0, reg.Lifts[0].PlanD);
            Assert.Equal(0, reg.Lifts[0].Facing);
        }
    }
}
