using System.Collections.Generic;
using System.Runtime.CompilerServices;
using LiteNetLib;
using Server.Doors;
using Server.Lifts;
using Server.Network;
using Shared.Configs;
using Shared.Simulation.Blocks;
using Shared.World.Atmos;
using Shared.World.Blocks;

namespace ServerTests.Server.Doors
{
    internal static class TestShaftDoorCatalog
    {
        internal const ushort WideShaftDoor = 920;

        [ModuleInitializer]
        internal static void Seed()
        {
            var field = typeof(BlockCatalog).GetField("_byId",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            var byId = (Dictionary<ushort, BlockInfo>)field.GetValue(null)!;

            int parts = 3 * 1 * 2;
            var closed = new BlockBox[parts][];
            var open = new BlockBox[parts][];
            for (int p = 0; p < parts; p++)
            {
                closed[p] = new[] { BlockBox.Full };
                open[p] = System.Array.Empty<BlockBox>();
            }

            byId[WideShaftDoor] = new BlockInfo(WideShaftDoor, "TestWideShaftDoor", BlockCategory.Door,
                BlockFaceFlags.All, BlockFaceFlags.All, 0, closed,
                3, 1, 2, open, DoorOpening.External, System.Array.Empty<TriggerBox>(), 0.1f);
        }
    }

    /// <summary>Анти-прищем на НЕквадратной шахтной двери (3×1×2): поосевая привязка, игрок вне центра, все 4 facing.</summary>
    public class DoorOccupancyAxisTests
    {
        private const int Ax = 5, Ay = 1, Az = 6;

        private sealed class PeerRefComparer : IEqualityComparer<NetPeer>
        {
            public static readonly PeerRefComparer Instance = new();
            public bool Equals(NetPeer? a, NetPeer? b) => ReferenceEquals(a, b);
            public int GetHashCode(NetPeer p) => RuntimeHelpers.GetHashCode(p);
        }

        private static NetPeer FakePeer() => (NetPeer)RuntimeHelpers.GetUninitializedObject(typeof(NetPeer));

        private readonly BlockGrid _grid = new();
        private readonly AtmosGrid _atmos = new();
        private readonly Dictionary<NetPeer, ClientConnection> _clients = new(PeerRefComparer.Instance);
        private readonly SVars _config = new() { TickRate = 30, AirlockMaxDeltaKpa = 20f };

        private DoorSystem Build(int facing)
        {
            Assert.True(_grid.PlaceMultiBlock(Ax, Ay, Az, TestShaftDoorCatalog.WideShaftDoor, facing));
            var sys = new DoorSystem(_grid, _atmos, _config, _clients);
            sys.Build();
            return sys;
        }

        private void AddClientAtCell(int cx, int cy, int cz)
        {
            var c = new ClientConnection(FakePeer(), 1)
            {
                PlayerNetId = 1,
                Mover = new BlockMoverState(cx + 0.3f, cy, cz + 0.7f)
            };
            _clients[c.Peer] = c;
        }

        private void AddClientAtCellCenter(int cx, int cy, int cz)
        {
            var c = new ClientConnection(FakePeer(), 1)
            {
                PlayerNetId = 1,
                Mover = new BlockMoverState(cx + 0.5f, cy, cz + 0.5f)
            };
            _clients[c.Peer] = c;
        }

        // Ячейки посчитаны ВРУЧНУЮ, а не формулой движка: (w,d) -> мир по facing.
        // A = локальная (w=2,d=0) — внутри футпринта 3×2. B = локальная (w=0,d=2) — ВНЕ него,
        // но попала бы внутрь, если перепутать SizeX и SizeZ.
        public static IEnumerable<object[]> Facings => new[]
        {
            new object[] { 0, Ax + 2, Az + 0, Ax + 0, Az + 2 },
            new object[] { 1, Ax + 0, Az - 2, Ax + 2, Az + 0 },
            new object[] { 2, Ax - 2, Az + 0, Ax + 0, Az - 2 },
            new object[] { 3, Ax + 0, Az + 2, Ax - 2, Az + 0 }
        };

        [Theory]
        [MemberData(nameof(Facings))]
        public void Close_BlockedByOccupant_OnFarWidthCell(int facing, int ax, int az, int bx, int bz)
        {
            var sys = Build(facing);
            Assert.True(sys.TryGetAnchorKey(Ax, Ay, Az, out long key));
            Assert.Equal(DoorCommandResult.Applied, sys.TrySetOpen(key, true));

            AddClientAtCell(ax, Ay, az);

            Assert.Equal(DoorCommandResult.BlockedByOccupant, sys.TrySetOpen(key, false));
            Assert.True(BlockState.GetOpen(_grid.GetState(Ax, Ay, Az)),
                "дверь обязана остаться открытой на игроке в дальней по ШИРИНЕ клетке");
        }

        [Theory]
        [MemberData(nameof(Facings))]
        public void Close_Allowed_WhenPlayerOnSwappedAxisCellOnly(int facing, int ax, int az, int bx, int bz)
        {
            var sys = Build(facing);
            Assert.True(sys.TryGetAnchorKey(Ax, Ay, Az, out long key));
            Assert.Equal(DoorCommandResult.Applied, sys.TrySetOpen(key, true));

            AddClientAtCellCenter(bx, Ay, bz);

            Assert.Equal(DoorCommandResult.Applied, sys.TrySetOpen(key, false));
            Assert.False(BlockState.GetOpen(_grid.GetState(Ax, Ay, Az)),
                "клетка вне футпринта (она внутри лишь при перепутанных осях) закрытию не мешает");
        }

        [Theory]
        [MemberData(nameof(Facings))]
        public void Open_IsAllowed_EvenWithOccupant(int facing, int ax, int az, int bx, int bz)
        {
            var sys = Build(facing);
            Assert.True(sys.TryGetAnchorKey(Ax, Ay, Az, out long key));

            AddClientAtCell(ax, Ay, az);

            Assert.Equal(DoorCommandResult.Applied, sys.TrySetOpen(key, true));
            Assert.True(BlockState.GetOpen(_grid.GetState(Ax, Ay, Az)), "открытие на игроке безопасно и обязано проходить");
        }

        [Fact]
        public void BuildNormalization_DoesNotCloseShaftDoorOnPlayer()
        {
            const int facing = 0;
            Assert.True(_grid.PlaceMultiBlock(Ax, Ay, Az, TestShaftDoorCatalog.WideShaftDoor, facing));

            var info = BlockCatalog.Get(TestShaftDoorCatalog.WideShaftDoor);
            int parts = MultiBlock.PartCount(info.SizeX, info.SizeY, info.SizeZ);
            for (int p = 0; p < parts; p++)
            {
                MultiBlock.PartWorldOffset(p, info.SizeX, info.SizeZ, facing, out int dx, out int dy, out int dz);
                byte st = _grid.GetState(Ax + dx, Ay + dy, Az + dz);
                _grid.SetState(Ax + dx, Ay + dy, Az + dz, BlockState.WithOpen(st, true));
            }

            var doors = new DoorSystem(_grid, _atmos, _config, _clients);
            doors.Build();
            AddClientAtCell(Ax + 2, Ay, Az);

            var lifts = new LiftSystem(_grid, doors, _config);
            lifts.Build();

            Assert.Equal(0, lifts.NormalizedDoors);
            Assert.True(BlockState.GetOpen(_grid.GetState(Ax, Ay, Az)),
                "нормализация при загрузке не имеет права захлопнуть дверь на игроке");
        }

        [Fact]
        public void BuildNormalization_ClosesShaftDoor_WhenNobodyStandsInIt()
        {
            const int facing = 0;
            Assert.True(_grid.PlaceMultiBlock(Ax, Ay, Az, TestShaftDoorCatalog.WideShaftDoor, facing));

            var info = BlockCatalog.Get(TestShaftDoorCatalog.WideShaftDoor);
            int parts = MultiBlock.PartCount(info.SizeX, info.SizeY, info.SizeZ);
            for (int p = 0; p < parts; p++)
            {
                MultiBlock.PartWorldOffset(p, info.SizeX, info.SizeZ, facing, out int dx, out int dy, out int dz);
                byte st = _grid.GetState(Ax + dx, Ay + dy, Az + dz);
                _grid.SetState(Ax + dx, Ay + dy, Az + dz, BlockState.WithOpen(st, true));
            }

            var doors = new DoorSystem(_grid, _atmos, _config, _clients);
            doors.Build();

            var lifts = new LiftSystem(_grid, doors, _config);
            lifts.Build();

            Assert.Equal(1, lifts.NormalizedDoors);
            Assert.False(BlockState.GetOpen(_grid.GetState(Ax, Ay, Az)));
        }
    }
}
