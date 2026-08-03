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
    /// <summary>Анти-прищем по БОКСУ игрока: центр вне клетки двери, но тело перекрывает проём.</summary>
    public class DoorOccupancyBoxTests
    {
        private const int Ax = 5, Az = 6;

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

        private DoorSystem Build(int facing, int ay)
        {
            Assert.True(_grid.PlaceMultiBlock(Ax, ay, Az, TestShaftDoorCatalog.WideShaftDoor, facing));
            var sys = new DoorSystem(_grid, _atmos, _config, _clients);
            sys.Build();
            return sys;
        }

        private void AddClient(float x, float feetY, float z)
        {
            var c = new ClientConnection(FakePeer(), 1)
            {
                PlayerNetId = 1,
                Mover = new BlockMoverState(x, feetY, z)
            };
            _clients[c.Peer] = c;
        }

        private static DoorCommandResult Close(DoorSystem sys, int ay)
        {
            Assert.True(sys.TryGetAnchorKey(Ax, ay, Az, out long key));
            Assert.Equal(DoorCommandResult.Applied, sys.TrySetOpen(key, true));
            return sys.TrySetOpen(key, false);
        }

        // Всё посчитано ВРУЧНУЮ. Подход СНАРУЖИ футпринта по оси ширины:
        // near — центр за внешней гранью, но бокс (±0.4) заходит в клетку на 0.2;
        // far  — центр на 0.45 за гранью, бокс не достаёт (0.05 зазора).
        // Перпендикулярная координата взята внутри глубины футпринта, иначе перекрытия не было бы вовсе.
        public static IEnumerable<object[]> Approach => new[]
        {
            new object[] { 0, Ax + 3.2f, Az + 0.5f, Ax + 3.45f, Az + 0.5f },
            new object[] { 1, Ax + 0.5f, Az - 2.2f, Ax + 0.5f, Az - 2.45f },
            new object[] { 2, Ax - 2.2f, Az + 0.5f, Ax - 2.45f, Az + 0.5f },
            new object[] { 3, Ax + 0.5f, Az + 3.2f, Ax + 0.5f, Az + 3.45f }
        };

        [Theory]
        [MemberData(nameof(Approach))]
        public void Close_Blocked_WhenBoxOverlaps_ButCenterIsOutsideFootprint(int facing,
            float nearX, float nearZ, float farX, float farZ)
        {
            const int ay = 1;
            var sys = Build(facing, ay);

            AddClient(nearX, ay, nearZ);

            Assert.Equal(DoorCommandResult.BlockedByOccupant, Close(sys, ay));
            Assert.True(BlockState.GetOpen(_grid.GetState(Ax, ay, Az)),
                "тело в проёме — створка не имеет права закрыться, даже если ЦЕНТР игрока в соседней клетке");
        }

        [Theory]
        [MemberData(nameof(Approach))]
        public void Close_Allowed_WhenBoxStopsShortOfFootprint(int facing,
            float nearX, float nearZ, float farX, float farZ)
        {
            const int ay = 1;
            var sys = Build(facing, ay);

            AddClient(farX, ay, farZ);

            Assert.Equal(DoorCommandResult.Applied, Close(sys, ay));
            Assert.False(BlockState.GetOpen(_grid.GetState(Ax, ay, Az)),
                "бокс не достаёт до клетки — закрытие обязано пройти");
        }

        [Theory]
        [MemberData(nameof(Approach))]
        public void Close_Allowed_WhenPlayerStandsOnAnotherFloor(int facing,
            float nearX, float nearZ, float farX, float farZ)
        {
            const int ay = 5;
            var sys = Build(facing, ay);

            AddClient(nearX, 1f, nearZ);

            Assert.Equal(DoorCommandResult.Applied, Close(sys, ay));
        }

        [Theory]
        [MemberData(nameof(Approach))]
        public void Close_Blocked_WhenHeadReachesIntoDoorCell(int facing,
            float nearX, float nearZ, float farX, float farZ)
        {
            const int ay = 2;
            var sys = Build(facing, ay);

            AddClient(nearX, 1f, nearZ);

            Assert.Equal(DoorCommandResult.BlockedByOccupant, Close(sys, ay));
            Assert.True(BlockState.GetOpen(_grid.GetState(Ax, ay, Az)),
                "голова торчит в клетку двери — прищемлять нельзя");
        }

        [Fact]
        public void Close_Allowed_WhenPlayerNotSpawned()
        {
            const int ay = 1;
            var sys = Build(0, ay);

            var c = new ClientConnection(FakePeer(), 1)
            {
                PlayerNetId = 0,
                Mover = new BlockMoverState(Ax + 0.5f, ay, Az + 0.5f)
            };
            _clients[c.Peer] = c;

            Assert.Equal(DoorCommandResult.Applied, Close(sys, ay));
        }
    }
}
