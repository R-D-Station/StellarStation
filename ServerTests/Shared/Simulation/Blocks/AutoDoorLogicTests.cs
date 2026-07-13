using Shared.World.Blocks;
using Shared.Simulation.Blocks;

namespace ServerTests.Shared.Simulation.Blocks
{
    /// <summary>Геометрия триггера авто-двери: поворот по facing + попадание игрока.</summary>
    public class AutoDoorLogicTests
    {
        // Триггер 1×1-двери в object-space: план x [0..1], z [1..2] (на 1 блок «на север»), y [0..2]. ×16.
        private static readonly TriggerBox North1 = new TriggerBox(0, 0, 16, 16, 32, 32);
        // Триггер 2-широкой двери: план x [0..2] (вся ширина), z [1..2] (1 блок вперёд), y [0..2].
        private static readonly TriggerBox WideNorth = new TriggerBox(0, 0, 16, 32, 32, 32);

        [Fact]
        public void Facing0_TriggerNorthOfAnchor()
        {
            AutoDoorLogic.TriggerWorldBounds(10, 5, 20, in North1, 1, 1, 0,
                out float minX, out float minY, out float minZ, out float maxX, out float maxY, out float maxZ);
            Assert.Equal(10f, minX);
            Assert.Equal(11f, maxX);
            Assert.Equal(21f, minZ); // az + 1
            Assert.Equal(22f, maxZ); // az + 2
            Assert.Equal(5f, minY);
            Assert.Equal(7f, maxY);
        }

        [Fact]
        public void Facing1_RotatesTriggerToEast()
        {
            // 1×1-дверь, facing 1: триггер уходит на восток от двери, оставаясь на её Z.
            AutoDoorLogic.TriggerWorldBounds(10, 5, 20, in North1, 1, 1, 1,
                out float minX, out float minY, out float minZ, out float maxX, out float maxY, out float maxZ);
            Assert.Equal(11f, minX);
            Assert.Equal(12f, maxX);
            Assert.Equal(20f, minZ);
            Assert.Equal(21f, maxZ);
        }

        [Fact]
        public void Facing1_WideDoor_TriggerMatchesRotatedFootprint()
        {
            // 2-широкая дверь (Door_2x2), facing 1: тело занимает X[ax..ax+1], Z[az−1..az+1]; триггер спереди
            // (восток) = X[ax+1..ax+2], Z[az−1..az+1]. Это и есть кейс «повёрнутая дверь не открывалась».
            AutoDoorLogic.TriggerWorldBounds(10, 5, 20, in WideNorth, 2, 1, 1,
                out float minX, out _, out float minZ, out float maxX, out _, out float maxZ);
            Assert.Equal(11f, minX); // ax + 1
            Assert.Equal(12f, maxX); // ax + 2
            Assert.Equal(19f, minZ); // az − 1
            Assert.Equal(21f, maxZ); // az + 1
        }

        [Fact]
        public void PlayerInside_Detected()
        {
            Assert.True(AutoDoorLogic.PlayerInTrigger(10.5f, 5f, 21.5f, 0.4f, 1.4f,
                10, 5, 20, new[] { North1 }, 1, 1, 0));
        }

        [Fact]
        public void PlayerInside_WideDoorFacing1_Detected()
        {
            // Игрок к востоку от повёрнутой 2-широкой двери (x≈11.5, z≈20) — должен попасть в триггер.
            Assert.True(AutoDoorLogic.PlayerInTrigger(11.5f, 5f, 20f, 0.4f, 1.4f,
                10, 5, 20, new[] { WideNorth }, 2, 1, 1));
        }

        [Fact]
        public void PlayerOutside_NotDetected()
        {
            Assert.False(AutoDoorLogic.PlayerInTrigger(10.5f, 5f, 18f, 0.4f, 1.4f,
                10, 5, 20, new[] { North1 }, 1, 1, 0));
        }

        [Fact]
        public void PlayerTooHigh_NotDetected()
        {
            Assert.False(AutoDoorLogic.PlayerInTrigger(10.5f, 8f, 21.5f, 0.4f, 1.4f,
                10, 5, 20, new[] { North1 }, 1, 1, 0));
        }

        [Fact]
        public void NoTriggers_False()
        {
            Assert.False(AutoDoorLogic.PlayerInTrigger(10.5f, 5f, 21.5f, 0.4f, 1.4f, 10, 5, 20, null, 1, 1, 0));
            Assert.False(AutoDoorLogic.PlayerInTrigger(10.5f, 5f, 21.5f, 0.4f, 1.4f, 10, 5, 20,
                System.Array.Empty<TriggerBox>(), 1, 1, 0));
        }

        [Fact]
        public void FaceTouch_NotInside()
        {
            // Игрок ровно на грани триггера (pMaxZ = 21.0 == minZ 21) — строгие неравенства → НЕ внутри.
            Assert.False(AutoDoorLogic.PlayerInTrigger(10.5f, 5f, 20.6f, 0.4f, 1.4f,
                10, 5, 20, new[] { North1 }, 1, 1, 0));
        }

        // ── таймер-автомат Tick ──────────────────────────────────────────────

        [Fact]
        public void Tick_Occupied_OpensAndHolds()
        {
            AutoDoorLogic.Tick(occupied: true, open: false, closeAtTick: 0, currentTick: 100, closeDelayTicks: 30,
                out bool newOpen, out uint newCloseAt);
            Assert.True(newOpen);
            Assert.Equal(uint.MaxValue, newCloseAt);
        }

        [Fact]
        public void Tick_Occupied_WhileOpen_CancelsCountdown()
        {
            AutoDoorLogic.Tick(true, true, closeAtTick: 130, currentTick: 100, 30, out bool newOpen, out uint newCloseAt);
            Assert.True(newOpen);
            Assert.Equal(uint.MaxValue, newCloseAt); // отсчёт отменён
        }

        [Fact]
        public void Tick_Vacated_StartsCountdown()
        {
            AutoDoorLogic.Tick(false, true, closeAtTick: uint.MaxValue, currentTick: 100, 30,
                out bool newOpen, out uint newCloseAt);
            Assert.True(newOpen);          // ещё открыта
            Assert.Equal(130u, newCloseAt); // 100 + 30
        }

        [Fact]
        public void Tick_CountingDown_StaysOpen()
        {
            AutoDoorLogic.Tick(false, true, closeAtTick: 130, currentTick: 120, 30, out bool newOpen, out uint newCloseAt);
            Assert.True(newOpen);
            Assert.Equal(130u, newCloseAt);
        }

        [Fact]
        public void Tick_DeadlineReached_Closes()
        {
            AutoDoorLogic.Tick(false, true, closeAtTick: 130, currentTick: 130, 30, out bool newOpen, out uint _);
            Assert.False(newOpen);
        }

        [Fact]
        public void Tick_ClosedAndEmpty_StaysClosed()
        {
            AutoDoorLogic.Tick(false, false, closeAtTick: 0, currentTick: 200, 30, out bool newOpen, out uint _);
            Assert.False(newOpen);
        }

        [Fact]
        public void Tick_ZeroDelay_ClosesNextTicksMinimumOne()
        {
            AutoDoorLogic.Tick(false, true, uint.MaxValue, currentTick: 50, closeDelayTicks: 0,
                out _, out uint newCloseAt);
            Assert.Equal(51u, newCloseAt); // 0 → минимум 1 тик
        }
    }
}
