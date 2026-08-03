using System;
using System.Collections.Generic;
using Server.Doors;
using Server.Lifts;
using Shared.Simulation.Blocks;

namespace ServerTests.Server.Lifts
{
    internal sealed class FakeDoors : IDoorCommands
    {
        public readonly HashSet<long> Known = new();
        public readonly HashSet<long> Open = new();
        public readonly List<long> OpenCommands = new();
        public DoorCommandResult OpenResult = DoorCommandResult.Applied;

        public bool TryGetAnchorKey(int x, int y, int z, out long key)
        {
            key = 0L;
            return false;
        }

        public bool Exists(long key) => Known.Contains(key);

        public bool IsOpen(long key) => Open.Contains(key);

        public bool RefuseClose;

        public DoorCommandResult TrySetOpen(long key, bool open, bool force = false)
        {
            if (open)
            {
                OpenCommands.Add(key);
                if (OpenResult != DoorCommandResult.Applied)
                    return OpenResult;
                return Open.Add(key) ? DoorCommandResult.Applied : DoorCommandResult.AlreadyInState;
            }
            if (RefuseClose && !force)
                return DoorCommandResult.BlockedByOccupant;
            return Open.Remove(key) ? DoorCommandResult.Applied : DoorCommandResult.AlreadyInState;
        }
    }

    /// <summary>LiftController: SCAN-очередь, фазы от тика, командование шахтными дверьми, этажи без дверей.</summary>
    public class LiftControllerTests
    {
        private const float Speed = 1f;

        private static LiftRuntime Runtime(float y, float speed = Speed)
            => new LiftRuntime(10f, 20f, new[] { new LiftBox(0f, 0f, 0f, 1f, 1f, 0.25f) },
                new LiftSegment(y, y, 0u, speed), liftId: 1);

        private static LiftController Make(FakeDoors doors, float[] stopY, long[] stopDoor, bool[] served,
            int parkedFloor, uint dwell = 4u, uint lead = 0u, uint retry = 1u, float speed = Speed)
        {
            var runtime = Runtime(stopY.Length == 0 ? 0f : stopY[parkedFloor], speed);
            foreach (long key in stopDoor)
                if (key != 0L)
                    doors.Known.Add(key);
            return new LiftController(1, runtime, stopY, stopDoor, served, dwell, lead, retry, speed, doors);
        }

        private static uint Run(LiftController c, uint from, int ticks, List<uint> syncs = null)
        {
            uint t = from;
            for (int i = 0; i < ticks; i++, t++)
                if (c.Decide(t))
                    syncs?.Add(t);
            return t;
        }

        [Fact]
        public void ParkedLift_DoesNotOpenDoors_WithoutCalls()
        {
            var doors = new FakeDoors();
            var c = Make(doors, new[] { 0f, 3f }, new[] { 100L, 200L }, new[] { true, true }, parkedFloor: 0);

            Run(c, 0u, 200);

            Assert.Empty(doors.OpenCommands);
            Assert.Equal(0, c.CurrentFloor);
        }

        [Fact]
        public void Request_TravelsAndOpensTargetDoor()
        {
            var doors = new FakeDoors();
            var c = Make(doors, new[] { 0f, 3f }, new[] { 100L, 200L }, new[] { true, true }, parkedFloor: 0);
            var syncs = new List<uint>();

            Assert.True(c.Request(1));
            Run(c, 0u, 200, syncs);

            // Пакетов теперь ТРИ, и это по построению: заказ (Calls), отправление (сегмент), открытие двери
            // (Calls снялся). Триггер — «состояние уехало», а не «сменился сегмент»: иначе панель не узнала бы
            // ни о нажатии, ни о его снятии.
            Assert.Equal(3, syncs.Count);
            Assert.Equal(1, c.CurrentFloor);
            Assert.Contains(200L, doors.OpenCommands);
            Assert.DoesNotContain(100L, doors.OpenCommands);
        }

        [Fact]
        public void FloorWithoutDoor_IsNotRequestable_AndNeverTouchesNeighbourDoor()
        {
            var doors = new FakeDoors();
            var c = Make(doors, new[] { 0f, 3f, 6f }, new[] { 100L, 0L, 300L },
                new[] { true, false, true }, parkedFloor: 1);

            Assert.False(c.Request(1), "этаж без двери (техуровень) нельзя заказать");
            Assert.False(c.HasCall(1));

            Run(c, 0u, 200);
            Assert.Empty(doors.OpenCommands);

            Assert.True(c.Request(2));
            Run(c, 200u, 200);
            Assert.Equal(2, c.CurrentFloor);
            Assert.Contains(300L, doors.OpenCommands);
            Assert.DoesNotContain(100L, doors.OpenCommands);
        }

        [Fact]
        public void LiftWithoutStops_SurvivesManyTicks()
        {
            var doors = new FakeDoors();
            var c = new LiftController(1, Runtime(0f), Array.Empty<float>(), Array.Empty<long>(),
                Array.Empty<bool>(), 4u, 0u, 1u, Speed, doors);

            var ex = Record.Exception(() => Run(c, 0u, 300));

            Assert.Null(ex);
            Assert.False(c.Request(0));
        }

        [Fact]
        public void RefusedDoor_RetriesThenSkipsFloor_WithDiagnostics()
        {
            var doors = new FakeDoors { OpenResult = DoorCommandResult.BlockedByPressure };
            var c = Make(doors, new[] { 0f, 3f }, new[] { 100L, 200L }, new[] { true, true },
                parkedFloor: 0, dwell: 40u, retry: 1u);

            Assert.True(c.Request(1));
            Run(c, 0u, 300);

            Assert.Equal(LiftController.MaxOpenAttempts, doors.OpenCommands.Count);
            Assert.Equal(1, c.SkippedFloors);
            Assert.Equal(1, c.LastSkippedFloor);
            Assert.False(c.HasCall(1), "после исчерпания попыток заказ снимается, иначе лифт залипнет навсегда");
        }

        [Fact]
        public void SkippedFloor_DoorStaysShut_AndLiftServesTheNextCall()
        {
            var doors = new FakeDoors { OpenResult = DoorCommandResult.BlockedByPressure };
            var c = Make(doors, new[] { 0f, 3f, 7f }, new[] { 100L, 200L, 300L },
                new[] { true, true, true }, parkedFloor: 0, dwell: 40u, retry: 1u);

            Assert.True(c.Request(1));
            uint t = Run(c, 0u, 300);

            Assert.Equal(1, c.SkippedFloors);
            Assert.DoesNotContain(200L, doors.Open);

            doors.OpenResult = DoorCommandResult.Applied;
            Assert.True(c.Request(2));
            Run(c, t, 400);

            Assert.Equal(2, c.CurrentFloor);
            Assert.Contains(300L, doors.OpenCommands);
            Assert.DoesNotContain(200L, doors.Open);
        }

        [Fact]
        public void CallBitSurvives_UntilDoorActuallyOpens()
        {
            var doors = new FakeDoors { OpenResult = DoorCommandResult.BlockedByPressure };
            var c = Make(doors, new[] { 0f, 3f }, new[] { 100L, 200L }, new[] { true, true },
                parkedFloor: 0, dwell: 40u, retry: 5u);

            Assert.True(c.Request(1));
            uint t = Run(c, 0u, 8);
            Assert.True(c.HasCall(1), "отказ открытия НЕ должен снимать заказ — иначе вызов исчезает бесследно");

            doors.OpenResult = DoorCommandResult.Applied;
            Run(c, t, 40);

            Assert.False(c.HasCall(1));
            Assert.Equal(0, c.SkippedFloors);
            Assert.Contains(200L, doors.Open);
        }

        [Fact]
        public void DoesNotDepart_WhileDoorStillOpen()
        {
            var doors = new FakeDoors();
            var c = Make(doors, new[] { 0f, 3f }, new[] { 100L, 200L }, new[] { true, true },
                parkedFloor: 0, dwell: 4u);

            Assert.True(c.Request(1));
            uint t = Run(c, 0u, 200);
            Assert.Equal(1, c.CurrentFloor);

            doors.Open.Add(200L);
            doors.RefuseClose = true;
            Assert.True(c.Request(0));
            var blockedSegment = c.Runtime.Segment;
            t = Run(c, t, 200);

            // «Не уехал» проверяем по СЕГМЕНТУ, а не по числу пакетов: пакет теперь уходит и на смену Calls,
            // так что счётчик пакетов больше не синоним отправления.
            Assert.Equal(blockedSegment.ToY, c.Runtime.Segment.ToY);
            Assert.Equal(blockedSegment.StartTick, c.Runtime.Segment.StartTick);
            Assert.Equal(1, c.CurrentFloor);
            Assert.True(c.HasCall(0), "заказ обязан пережить блокировку двери");

            doors.RefuseClose = false;
            Run(c, t, 200);

            Assert.Equal(0, c.CurrentFloor);
        }

        [Fact]
        public void EnRouteCall_ShortensTrip_WithoutRewritingThePast()
        {
            var doors = new FakeDoors();
            var c = Make(doors, new[] { 0f, 3f, 9f }, new[] { 100L, 200L, 300L },
                new[] { true, true, true }, parkedFloor: 0, dwell: 4u, speed: 0.5f);

            Assert.True(c.Request(2));
            uint t = Run(c, 0u, 6);
            Assert.Equal(LiftPhaseKind.Travel, c.PhaseAt(t));

            var before = new float[t + 1];
            for (uint k = 0; k <= t; k++)
                before[k] = c.Runtime.YAt(k);

            Assert.True(c.Request(1));
            c.Decide(t);

            for (uint k = 0; k <= t; k++)
                Assert.True(Math.Abs(before[k] - c.Runtime.YAt(k)) < 1e-6f,
                    $"попутная остановка переписала прошлое на тике {k}: было {before[k]}, стало {c.Runtime.YAt(k)} " +
                    "— реплей клиента отклеит пассажира (M6)");

            Assert.Equal(3f, c.Runtime.Segment.ToY);
        }

        [Fact]
        public void Shorten_KeepsPastIdentical_UnlikeRetarget()
        {
            const uint Now = 12u;
            var shortened = Runtime(0f, 0.5f);
            var retargeted = Runtime(0f, 0.5f);
            shortened.Retarget(10f, 0u);
            retargeted.Retarget(10f, 0u);

            var before = new float[Now + 1];
            for (uint k = 0; k <= Now; k++)
                before[k] = shortened.YAt(k);
            Assert.Equal(6f, before[Now]);

            shortened.Shorten(8f);
            retargeted.Retarget(8f, Now);

            var retargetedSegment = retargeted.Segment;
            for (uint k = 0; k <= Now; k++)
                Assert.True(Math.Abs(before[k] - shortened.YAt(k)) < 1e-6f,
                    $"Shorten переписал прошлое на тике {k}: было {before[k]}, стало {shortened.YAt(k)}");

            for (uint k = 0; k < Now; k++)
                Assert.Equal(0f, LiftTrajectory.DeltaAt(in retargetedSegment, k));

            Assert.True(Math.Abs(before[5] - retargeted.YAt(5)) > 1e-3f,
                "Retarget обязан ломать прошлое — иначе тест не доказывает, ЗАЧЕМ нужен Shorten");
            Assert.Equal(8f, shortened.Segment.ToY);
        }

        [Fact]
        public void CabinBoxes_AreRelativeToCabinFloor_OnEveryFloor()
        {
            var boxes = new[] { new LiftBox(0.5f, 0f, -0.5f, 1f, 2f, 2.5f) };
            var low = new LiftRuntime(10f, 20f, boxes, new LiftSegment(0f, 0f, 0u, 1f), 1);
            var high = new LiftRuntime(10f, 20f, boxes, new LiftSegment(6f, 6f, 0u, 1f), 2);

            var lowSet = new DynamicObstacleSet();
            var highSet = new DynamicObstacleSet();
            low.Tick(50u, lowSet);
            high.Tick(50u, highSet);

            lowSet.Get(0, out float lx0, out float ly0, out float lz0, out float lx1, out float ly1, out float lz1);
            highSet.Get(0, out float hx0, out float hy0, out float hz0, out float hx1, out float hy1, out float hz1);

            Assert.Equal(lx0, hx0);
            Assert.Equal(lx1, hx1);
            Assert.Equal(lz0, hz0);
            Assert.Equal(lz1, hz1);
            Assert.Equal(6f, hy0 - ly0);
            Assert.Equal(6f, hy1 - ly1);
            Assert.Equal(ly1 - ly0, hy1 - hy0);
        }

        [Fact]
        public void CabinBox_IsRectangularInPlan_NotSquare()
        {
            var set = new DynamicObstacleSet();
            new LiftRuntime(0f, 0f, new[] { new LiftBox(0f, 0f, 0f, 1f, 3f, 2f) },
                new LiftSegment(0f, 0f, 0u, 1f), 1).Tick(0u, set);

            set.Get(0, out float x0, out _, out float z0, out float x1, out _, out float z1);

            Assert.Equal(2f, x1 - x0);
            Assert.Equal(6f, z1 - z0);
        }

        [Fact]
        public void ScanOrder_ServesSameDirectionBeforeReversing()
        {
            var doors = new FakeDoors();
            var c = Make(doors, new[] { 0f, 3f, 6f, 9f }, new[] { 100L, 200L, 300L, 400L },
                new[] { true, true, true, true }, parkedFloor: 1, dwell: 4u, speed: 3f);

            Assert.True(c.Request(3));
            Assert.True(c.Request(0));

            var visited = new List<int>();
            int last = c.CurrentFloor;
            for (uint t = 0; t < 200; t++)
            {
                c.Decide(t);
                if (c.CurrentFloor != last)
                {
                    last = c.CurrentFloor;
                    visited.Add(last);
                }
            }

            Assert.Equal(new[] { 3, 0 }, visited);
        }
    }
}
