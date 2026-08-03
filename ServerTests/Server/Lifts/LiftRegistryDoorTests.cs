using System.Collections.Generic;
using System.Runtime.CompilerServices;
using LiteNetLib;
using Server.Doors;
using Server.Lifts;
using Server.Network;
using Shared.Configs;
using Shared.Messages.Lifts;
using Shared.World.Atmos;
using Shared.World.Blocks;

namespace ServerTests.Server.Lifts
{
    /// <summary>Клетка шахтной двери доезжает до клиента: без неё дисплей позиционировать нечем,
    /// а выводить дверь из плана шахты — догадка о геометрии (на этом треке стреляла четырежды).</summary>
    public class LiftRegistryDoorTests
    {
        private const int RX = 10, RZ = 10;

        private sealed class PeerRefComparer : IEqualityComparer<NetPeer>
        {
            public static readonly PeerRefComparer Instance = new();
            public bool Equals(NetPeer? a, NetPeer? b) => ReferenceEquals(a, b);
            public int GetHashCode(NetPeer p) => RuntimeHelpers.GetHashCode(p);
        }

        // Дверь ТОЛЬКО на нижнем этаже: верхний обслуживается кабиной, но двери там нет — штатный техуровень.
        private static LiftSystem BuildShaft(bool doorOnUpperFloor)
        {
            var grid = new BlockGrid();
            for (int k = 0; k < 2; k++)
            {
                grid.SetBlock(RX, k * TestLiftCatalog.WideStep, RZ, TestLiftCatalog.RailWide);
                grid.SetState(RX, k * TestLiftCatalog.WideStep, RZ, BlockState.WithFacing(0, 0));
            }
            grid.SetBlock(RX + 1, 0, RZ, TestLiftCatalog.CabinWide);
            grid.SetBlock(RX + 1, 0, RZ - 1, TestLiftCatalog.ShaftDoor);
            if (doorOnUpperFloor)
                grid.SetBlock(RX + 1, TestLiftCatalog.WideStep, RZ - 1, TestLiftCatalog.ShaftDoor);

            var clients = new Dictionary<NetPeer, ClientConnection>(PeerRefComparer.Instance);
            var doors = new DoorSystem(grid, new AtmosGrid(), new SVars { TickRate = 30 }, clients);
            doors.Build();
            var lifts = new LiftSystem(grid, doors, new SVars { TickRate = 30 });
            lifts.Build();
            return lifts;
        }

        private static LiftRegistry BuildRegistry(LiftSystem lifts)
        {
            var runtimes = new List<LiftRuntime>();
            for (int i = 0; i < lifts.Controllers.Count; i++)
                runtimes.Add(lifts.Controllers[i].Runtime);
            return LiftRegistryBuilder.Build(runtimes, lifts.Controllers, lifts.Result.Shafts);
        }

        [Fact]
        public void DoorCell_ReachesClient_ThroughTheWire()
        {
            var lifts = BuildShaft(doorOnUpperFloor: true);
            var wire = new LiftRegistry();
            wire.Deserialize(BuildRegistry(lifts).Serialize());

            var entry = Assert.Single(wire.Lifts);
            Assert.Equal(2, entry.Stops.Length);

            foreach (var stop in entry.Stops)
            {
                Assert.True(stop.HasDoor, $"этаж {stop.Floor}: дверь есть на карте, но не доехала до клиента");
                Assert.Equal(RX + 1, stop.DoorX);
                Assert.Equal(RZ - 1, stop.DoorZ);
                Assert.Equal(stop.Floor * TestLiftCatalog.WideStep, stop.DoorY);
            }
        }

        [Fact]
        public void CabinPrefabAndPlan_ReachTheClient()
        {
            var lifts = BuildShaft(doorOnUpperFloor: true);
            var wire = new LiftRegistry();
            wire.Deserialize(BuildRegistry(lifts).Serialize());
            var entry = Assert.Single(wire.Lifts);

            Assert.Equal(TestLiftCatalog.CabinWide, entry.CabinDefId);
            Assert.Equal(TestLiftCatalog.WideX, entry.PlanW);
            Assert.Equal(TestLiftCatalog.WideZ, entry.PlanD);
            Assert.NotEqual(entry.PlanW, entry.PlanD);

            // Якорь — угол ТОГО ЖЕ прямоугольника, из которого выводится план: пивот и якорь не разъедутся.
            Assert.Equal(RX, entry.AnchorX);
            Assert.Equal(RZ, entry.AnchorZ);
        }

        [Fact]
        public void ScannedShaft_RailFloorsAndParkedCabinAgree()
        {
            // На БОЕВЫХ данных скана: высота остановки, до которой довозит контроллер, обязана совпасть
            // с FloorYAt того же индекса, по которому ставится модуль рельса. Разойдутся — кабина
            // и шахта окажутся на разной высоте, а по одной лишь таблице остановок это незаметно.
            var lifts = BuildShaft(doorOnUpperFloor: true);
            var wire = new LiftRegistry();
            wire.Deserialize(BuildRegistry(lifts).Serialize());
            var entry = Assert.Single(wire.Lifts);

            Assert.Equal(TestLiftCatalog.RailWide, entry.RailDefId);
            Assert.True(entry.FloorCount >= entry.Stops.Length,
                "модулей рельса не может быть меньше, чем обслуживаемых этажей");

            foreach (var stop in entry.Stops)
                Assert.Equal(entry.FloorYAt(stop.Floor), stop.Y, 4);
        }

        [Fact]
        public void ScannedShaft_ServedFloorAlwaysHasDoor()
        {
            // В боевом скане остановка РОЖДАЕТСЯ дверью (BindDoors), поэтому «обслуживается ⇒ дверь есть».
            // Этаж без двери просто не попадает в таблицу — и дисплея на нём не будет по построению.
            var lifts = BuildShaft(doorOnUpperFloor: false);
            var wire = new LiftRegistry();
            wire.Deserialize(BuildRegistry(lifts).Serialize());

            var entry = Assert.Single(wire.Lifts);
            var stop = Assert.Single(entry.Stops);
            Assert.Equal(0, stop.Floor);
            Assert.True(stop.HasDoor);
        }

        [Fact]
        public void SyntheticDebugShaft_HasDoorlessStops_ThatSurviveTheWire()
        {
            // Единственный источник остановок БЕЗ двери — синтетическая шахта стенда (у неё дверей нет вовсе).
            // Она обязана доехать до клиента без падения чтения и не породить дисплеев.
            var grid = new BlockGrid();
            var clients = new Dictionary<NetPeer, ClientConnection>(PeerRefComparer.Instance);
            var config = new SVars
            {
                TickRate = 30, DebugLiftEnabled = true,
                DebugLiftX = 5.5f, DebugLiftZ = 7.5f, DebugLiftFromY = 1f, DebugLiftToY = 4f,
                DebugLiftSpeed = 0.05f, DebugLiftHalfW = 1f, DebugLiftHeight = 0.25f, DebugLiftPauseTicks = 60
            };
            var doors = new DoorSystem(grid, new AtmosGrid(), config, clients);
            doors.Build();
            var lifts = new LiftSystem(grid, doors, config);
            lifts.Build();

            var wire = new LiftRegistry();
            wire.Deserialize(BuildRegistry(lifts).Serialize());

            var entry = Assert.Single(wire.Lifts);
            // Стенд без контента: префаба нет, план вырожден в одну клетку — обе штатные ветки фолбэка.
            Assert.Equal(0, entry.CabinDefId);
            Assert.Equal(1, entry.PlanW);
            Assert.Equal(1, entry.PlanD);

            Assert.Equal(2, entry.Stops.Length);
            foreach (var stop in entry.Stops)
            {
                Assert.False(stop.HasDoor, "у синтетической шахты дверей нет — дисплею не на чем висеть");
                Assert.Equal(LiftStopEntry.NoDoor, stop.DoorX);
                Assert.Equal(LiftStopEntry.NoDoor, stop.DoorY);
                Assert.Equal(LiftStopEntry.NoDoor, stop.DoorZ);
            }
        }

        [Fact]
        public void NoDoorSentinel_IsOutsideRealCoordinateRange()
        {
            // Карта адресуется 21 битом на ось со знаком ⇒ |координата| < 2^20. Часовой обязан лежать ВНЕ
            // этого диапазона, иначе настоящая дверь могла бы прочитаться как «двери нет».
            // (short.MinValue = −32768 сюда ПОПАДАЕТ — поэтому поля int, а не short.)
            Assert.True(LiftStopEntry.NoDoor < -(1 << 20));
            Assert.False(LiftStopEntry.WithoutDoor(3, 1f).HasDoor);
            Assert.True(new LiftStopEntry(3, 1f, 0, 0, 0).HasDoor);
        }
    }
}
