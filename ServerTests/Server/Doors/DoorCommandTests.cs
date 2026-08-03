using System.Collections.Generic;
using System.Runtime.CompilerServices;
using LiteNetLib;
using Server.Doors;
using Server.Network;
using Shared.Configs;
using Shared.Simulation.Blocks;
using Shared.World.Atmos;
using Shared.World.Blocks;

namespace ServerTests.Server.Doors
{
    /// <summary>IDoorCommands поверх DoorSystem: ключ клетки, резолв якоря, идемпотентность, анти-прищем.</summary>
    public class DoorCommandTests
    {
        private const int DoorX = 5, DoorY = 1, DoorZ = 6;

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

        private DoorSystem System()
        {
            var sys = new DoorSystem(_grid, _atmos, _config, _clients);
            sys.Build();
            return sys;
        }

        private void Place(ushort type, int x = DoorX, int y = DoorY, int z = DoorZ, int facing = 0)
        {
            var info = BlockCatalog.Get(type);
            int parts = MultiBlock.PartCount(info.SizeX, info.SizeY, info.SizeZ);
            for (int p = 0; p < parts; p++)
            {
                MultiBlock.PartWorldOffset(p, info.SizeX, info.SizeZ, facing, out int dx, out int dy, out int dz);
                _grid.SetBlock(x + dx, y + dy, z + dz, 0);
            }
            Assert.True(_grid.PlaceMultiBlock(x, y, z, type, facing), "мульти-блок должен встать");
        }

        private ClientConnection AddClient(float planX, float height, float planZ)
        {
            var c = new ClientConnection(FakePeer(), 1)
            {
                PlayerNetId = 1,
                Mover = new BlockMoverState(planX, height, planZ)
            };
            _clients[c.Peer] = c;
            return c;
        }

        private bool IsOpenAt(int x, int y, int z) => BlockState.GetOpen(_grid.GetState(x, y, z));

        [Fact]
        public void TrySetOpen_SecondCallIsIdempotent()
        {
            Place(TestDoorCatalog.InteractDoor);
            var sys = System();
            Assert.True(sys.TryGetAnchorKey(DoorX, DoorY, DoorZ, out long key));

            Assert.Equal(DoorCommandResult.Applied, sys.TrySetOpen(key, true));
            Assert.True(sys.IsOpen(key));
            Assert.True(IsOpenAt(DoorX, DoorY, DoorZ));

            Assert.Equal(DoorCommandResult.AlreadyInState, sys.TrySetOpen(key, true));
            Assert.True(sys.IsOpen(key), "повторная команда open не должна перевернуть дверь");
            Assert.True(IsOpenAt(DoorX, DoorY, DoorZ));

            Assert.Equal(DoorCommandResult.Applied, sys.TrySetOpen(key, false));
            Assert.Equal(DoorCommandResult.AlreadyInState, sys.TrySetOpen(key, false));
            Assert.False(IsOpenAt(DoorX, DoorY, DoorZ));
        }

        [Fact]
        public void TrySetOpen_UnknownKey_ReturnsNoSuchDoor()
        {
            Place(TestDoorCatalog.InteractDoor);
            var sys = System();

            long absent = BlockGrid.PackCell(DoorX + 3, DoorY, DoorZ);
            Assert.Equal(DoorCommandResult.NoSuchDoor, sys.TrySetOpen(absent, true));
        }

        [Fact]
        public void TryGetAnchorKey_FromNonAnchorPart_ResolvesAnchor()
        {
            Place(TestDoorCatalog.InteractDoorTall);
            var sys = System();

            Assert.True(sys.TryGetAnchorKey(DoorX, DoorY + 1, DoorZ, out long fromPart));
            Assert.True(sys.TryGetAnchorKey(DoorX, DoorY, DoorZ, out long fromAnchor));
            Assert.Equal(fromAnchor, fromPart);

            Assert.Equal(DoorCommandResult.Applied, sys.TrySetOpen(fromPart, true));
            Assert.True(IsOpenAt(DoorX, DoorY, DoorZ));
            Assert.True(IsOpenAt(DoorX, DoorY + 1, DoorZ));
        }

        [Fact]
        public void TryGetAnchorKey_SingleCellDoor_FallsBackToCellItself()
        {
            Place(TestDoorCatalog.InteractDoor);
            var sys = System();

            Assert.False(_grid.TryGetStructOffset(DoorX, DoorY, DoorZ, out _, out _, out _),
                "у одноклеточной двери нет записи слоя структур — иначе тест не проверяет фолбэк");
            Assert.True(sys.TryGetAnchorKey(DoorX, DoorY, DoorZ, out long key));
            Assert.Equal(BlockGrid.PackCell(DoorX, DoorY, DoorZ), key);
        }

        [Fact]
        public void PackCell_SeparatesDoorsInsideOneSection()
        {
            const int OtherX = DoorX + 2;
            Assert.Equal(BlockGrid.KeyOfBlock(DoorX, DoorY, DoorZ), BlockGrid.KeyOfBlock(OtherX, DoorY, DoorZ));
            Assert.NotEqual(BlockGrid.PackCell(DoorX, DoorY, DoorZ), BlockGrid.PackCell(OtherX, DoorY, DoorZ));

            Place(TestDoorCatalog.InteractDoor);
            Place(TestDoorCatalog.InteractDoor, x: OtherX);
            var sys = System();

            Assert.Equal(2, sys.Registry.Count);
            Assert.True(sys.TryGetAnchorKey(DoorX, DoorY, DoorZ, out long a));
            Assert.True(sys.TryGetAnchorKey(OtherX, DoorY, DoorZ, out long b));

            Assert.Equal(DoorCommandResult.Applied, sys.TrySetOpen(a, true));
            Assert.True(IsOpenAt(DoorX, DoorY, DoorZ));
            Assert.False(IsOpenAt(OtherX, DoorY, DoorZ), "соседняя дверь той же секции не должна открыться");
            Assert.False(sys.IsOpen(b));
        }

        [Fact]
        public void TrySetOpen_Close_BlockedByOccupantInDoorway()
        {
            Place(TestDoorCatalog.InteractDoor);
            var sys = System();
            Assert.True(sys.TryGetAnchorKey(DoorX, DoorY, DoorZ, out long key));
            Assert.Equal(DoorCommandResult.Applied, sys.TrySetOpen(key, true));

            AddClient(DoorX + 0.5f, DoorY, DoorZ + 0.5f);

            Assert.Equal(DoorCommandResult.BlockedByOccupant, sys.TrySetOpen(key, false));
            Assert.True(IsOpenAt(DoorX, DoorY, DoorZ), "дверь не закрывается на игроке в проёме");

            Assert.Equal(DoorCommandResult.Applied, sys.TrySetOpen(key, false, force: true));
            Assert.False(IsOpenAt(DoorX, DoorY, DoorZ), "force игнорирует анти-прищем");
        }

        [Fact]
        public void Tick_KeepsOpen_WhileOccupantStandsInDoorwayOutsideTrigger()
        {
            Place(ServerTests.Server.Airlocks.TestAirlockCatalog.NarrowTriggerAuto);
            var sys = System();
            var info = BlockCatalog.Get(ServerTests.Server.Airlocks.TestAirlockCatalog.NarrowTriggerAuto);

            var client = AddClient(DoorX + 0.5f, DoorY, DoorZ - 0.5f);
            Assert.True(AutoDoorLogic.PlayerInTrigger(client.Mover.X, client.Mover.Y, client.Mover.Z,
                BlockMovementConfig.HalfWidth, BlockMovementConfig.StandHeight,
                DoorX, DoorY, DoorZ, info.Triggers, info.SizeX, info.SizeZ, 0), "стартовая поза обязана быть в триггере");

            uint tick = 0;
            sys.Tick(tick++);
            Assert.True(IsOpenAt(DoorX, DoorY, DoorZ), "авто-дверь должна открыться от триггера");

            client.Mover = new BlockMoverState(DoorX + 0.5f, DoorY, DoorZ + 0.5f);
            Assert.False(AutoDoorLogic.PlayerInTrigger(client.Mover.X, client.Mover.Y, client.Mover.Z,
                BlockMovementConfig.HalfWidth, BlockMovementConfig.StandHeight,
                DoorX, DoorY, DoorZ, info.Triggers, info.SizeX, info.SizeZ, 0),
                "в проёме игрок ВНЕ триггера — иначе тест проверяет удержание, а не анти-прищем");

            for (int i = 0; i < 120; i++)
                sys.Tick(tick++);
            Assert.True(IsOpenAt(DoorX, DoorY, DoorZ), "анти-прищем обязан держать дверь открытой");

            client.Mover = new BlockMoverState(DoorX + 0.5f, DoorY, DoorZ + 3.5f);
            for (int i = 0; i < 120; i++)
                sys.Tick(tick++);
            Assert.False(IsOpenAt(DoorX, DoorY, DoorZ), "после ухода игрока дверь должна закрыться");
        }
    }
}
