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
    /// <summary>Источник истины для створок кабины: клетка из LiftStopEntry — та же, чей бит Open авторитетен,
    /// и лифт командует дверью РОВНО своего этажа. На это обопрётся клиентское зеркалирование.</summary>
    public class LiftDoorTruthTests
    {
        // Отрицательные и разные по модулю координаты: перестановка X/Z или сдвиг этажа обязаны быть видны.
        private const int RX = -2, RZ = -3;
        private const int Floor0DoorX = RX + 1, Floor0DoorY = 0, Floor0DoorZ = RZ - 1;
        private const int Floor1DoorX = RX + 2, Floor1DoorY = TestLiftCatalog.WideStep, Floor1DoorZ = RZ + 2;
        private const int ExtraFloor0DoorX = RX + 2, ExtraFloor0DoorZ = RZ + 2;

        private sealed class PeerRefComparer : IEqualityComparer<NetPeer>
        {
            public static readonly PeerRefComparer Instance = new();
            public bool Equals(NetPeer? a, NetPeer? b) => ReferenceEquals(a, b);
            public int GetHashCode(NetPeer p) => RuntimeHelpers.GetHashCode(p);
        }

        private BlockGrid _grid = null!;
        private DoorSystem _doors = null!;

        private LiftSystem BuildShaft(bool secondDoorOnFloor0 = false)
        {
            _grid = new BlockGrid();
            for (int k = 0; k < 2; k++)
            {
                _grid.SetBlock(RX, k * TestLiftCatalog.WideStep, RZ, TestLiftCatalog.RailWide);
                _grid.SetState(RX, k * TestLiftCatalog.WideStep, RZ, BlockState.WithFacing(0, 0));
            }
            _grid.SetBlock(RX + 1, 0, RZ, TestLiftCatalog.CabinWide);
            _grid.SetBlock(Floor0DoorX, Floor0DoorY, Floor0DoorZ, TestLiftCatalog.ShaftDoor);
            _grid.SetBlock(Floor1DoorX, Floor1DoorY, Floor1DoorZ, TestLiftCatalog.ShaftDoor);
            if (secondDoorOnFloor0)
                _grid.SetBlock(ExtraFloor0DoorX, Floor0DoorY, ExtraFloor0DoorZ, TestLiftCatalog.ShaftDoor);

            var clients = new Dictionary<NetPeer, ClientConnection>(PeerRefComparer.Instance);
            _doors = new DoorSystem(_grid, new AtmosGrid(), new SVars { TickRate = 30 }, clients);
            _doors.Build();
            var lifts = new LiftSystem(_grid, _doors, new SVars { TickRate = 30 });
            lifts.Build();
            return lifts;
        }

        private static LiftRegistry Wire(LiftSystem lifts)
        {
            var runtimes = new List<LiftRuntime>();
            for (int i = 0; i < lifts.Controllers.Count; i++)
                runtimes.Add(lifts.Controllers[i].Runtime);
            var wire = new LiftRegistry();
            wire.Deserialize(LiftRegistryBuilder.Build(runtimes, lifts.Controllers, lifts.Result.Shafts).Serialize());
            return wire;
        }

        private static LiftStopEntry StopOf(LiftRegistry wire, int floor)
        {
            var lift = Assert.Single(wire.Lifts);
            foreach (var stop in lift.Stops)
                if (stop.Floor == floor)
                    return stop;
            Assert.Fail($"в реестре нет остановки этажа {floor}");
            return default;
        }

        private bool OpenAt(int x, int y, int z) => BlockState.GetOpen(_grid.GetState(x, y, z));

        // Крутит лифт, пока какая-нибудь из шахтных дверей не откроется; возвращает ЧЬЯ клетка открылась.
        private (int x, int y, int z) RunUntilAnyDoorOpens(LiftController c)
        {
            for (uint t = 0; t < 400; t++)
            {
                c.Decide(t);
                if (OpenAt(Floor0DoorX, Floor0DoorY, Floor0DoorZ))
                    return (Floor0DoorX, Floor0DoorY, Floor0DoorZ);
                if (OpenAt(Floor1DoorX, Floor1DoorY, Floor1DoorZ))
                    return (Floor1DoorX, Floor1DoorY, Floor1DoorZ);
                if (OpenAt(ExtraFloor0DoorX, Floor0DoorY, ExtraFloor0DoorZ))
                    return (ExtraFloor0DoorX, Floor0DoorY, ExtraFloor0DoorZ);
            }
            Assert.Fail("ни одна дверь шахты не открылась");
            return default;
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void OpenedDoorCell_IsExactlyTheCellAdvertisedForThatFloor(int floor)
        {
            var lifts = BuildShaft();
            var controller = Assert.Single(lifts.Controllers);
            var stop = StopOf(Wire(lifts), floor);

            Assert.True(controller.Request(floor));
            var opened = RunUntilAnyDoorOpens(controller);

            Assert.Equal(stop.DoorX, opened.x);
            Assert.Equal(stop.DoorY, opened.y);
            Assert.Equal(stop.DoorZ, opened.z);
        }

        [Theory]
        [InlineData(0, Floor0DoorX, Floor0DoorY, Floor0DoorZ)]
        [InlineData(1, Floor1DoorX, Floor1DoorY, Floor1DoorZ)]
        public void WireStop_CarriesItsOwnFloorDoor_ComponentWise(int floor, int x, int y, int z)
        {
            var lifts = BuildShaft();
            var stop = StopOf(Wire(lifts), floor);

            Assert.True(stop.HasDoor);
            Assert.Equal(x, stop.DoorX);
            Assert.Equal(y, stop.DoorY);
            Assert.Equal(z, stop.DoorZ);
        }

        [Fact]
        public void OpeningOneFloor_LeavesTheOtherFloorDoorClosed()
        {
            var lifts = BuildShaft();
            var controller = Assert.Single(lifts.Controllers);

            Assert.True(controller.Request(1));
            RunUntilAnyDoorOpens(controller);

            Assert.True(OpenAt(Floor1DoorX, Floor1DoorY, Floor1DoorZ));
            Assert.False(OpenAt(Floor0DoorX, Floor0DoorY, Floor0DoorZ),
                "лифт обязан командовать дверью СВОЕГО этажа, а не соседнего");
        }

        [Fact]
        public void SecondDoorOnSameFloor_IsDropped_AndReported()
        {
            var lifts = BuildShaft(secondDoorOnFloor0: true);

            Assert.Contains(lifts.Result.Issues, i => i.Kind == LiftScanIssueKind.DuplicateFloorDoor);

            var shaft = Assert.Single(lifts.Result.Shafts);
            int onFloor0 = 0;
            foreach (var stop in shaft.Stops)
                if (stop.FloorIndex == 0)
                    onFloor0++;
            Assert.Equal(1, onFloor0);
        }

        [Fact]
        public void SecondDoorOnSameFloor_CommandedDoorAndAdvertisedDoorStayTheSame()
        {
            var lifts = BuildShaft(secondDoorOnFloor0: true);
            var controller = Assert.Single(lifts.Controllers);
            var stop = StopOf(Wire(lifts), 0);

            Assert.True(controller.Request(0));
            var opened = RunUntilAnyDoorOpens(controller);

            Assert.Equal(stop.DoorX, opened.x);
            Assert.Equal(stop.DoorY, opened.y);
            Assert.Equal(stop.DoorZ, opened.z);
        }

        [Fact]
        public void SecondDoorOnSameFloor_KeptDoorIsDeterministic_LowestXThenZ()
        {
            var stop = StopOf(Wire(BuildShaft(secondDoorOnFloor0: true)), 0);

            Assert.Equal(Floor0DoorX, stop.DoorX);
            Assert.Equal(Floor0DoorZ, stop.DoorZ);
        }

        [Fact]
        public void OpenBit_IsWrittenToTheAnchorCell_ThatTheWirePointsAt()
        {
            var lifts = BuildShaft();
            var stop = StopOf(Wire(lifts), 0);

            Assert.True(_doors.TryGetAnchorKey(stop.DoorX, stop.DoorY, stop.DoorZ, out long key));
            Assert.Equal(DoorCommandResult.Applied, _doors.TrySetOpen(key, true));

            Assert.True(OpenAt(stop.DoorX, stop.DoorY, stop.DoorZ),
                "клиент читает бит именно из клетки LiftStopEntry — она обязана быть якорем двери");
        }
    }
}
