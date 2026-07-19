using Shared.Configs;
using Shared.World;
using Shared.World.Items;
using Server.Network;

namespace ServerTests.Server.Network
{
    /// <summary>SpawnGroundItem/DespawnGroundItem регистрируют/снимают предметы в общем реестре; PVS-выборка GroundItemsInInterest;
    /// предмет — не ClientConnection (не течёт в player-снапшот).</summary>
    public class GroundItemSpawnTests
    {
        private static SVars Config() => new SVars
        {
            Ip = "127.0.0.1",
            Port = 0,
            MaxPlayers = 4,
            TickRate = 30,
            ConnectionKey = "t"
        };

        [Fact]
        public void Spawn_RegistersInEntities_ReturnsUniquePositiveIds()
        {
            var server = new GameServer(Config(), new GridMap());
            Assert.Equal(0, server.EntityCount);

            int a = server.SpawnGroundItem(10, 1, 5, 5, 0);
            int b = server.SpawnGroundItem(11, 2, 6, 6, 0);

            Assert.Equal(2, server.EntityCount);
            Assert.True(a > 0 && b > 0);
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void Despawn_RemovesItem_ReturnsTrue()
        {
            var server = new GameServer(Config(), new GridMap());
            int id = server.SpawnGroundItem(10, 1, 5, 5, 0);
            Assert.Equal(1, server.EntityCount);

            Assert.True(server.DespawnGroundItem(id));
            Assert.Equal(0, server.EntityCount);
        }

        [Fact]
        public void Despawn_UnknownId_ReturnsFalse_NoChange()
        {
            var server = new GameServer(Config(), new GridMap());
            server.SpawnGroundItem(10, 1, 5, 5, 0);

            Assert.False(server.DespawnGroundItem(9999));
            Assert.Equal(1, server.EntityCount);
        }

        [Fact]
        public void Despawn_Twice_SecondReturnsFalse()
        {
            var server = new GameServer(Config(), new GridMap());
            int id = server.SpawnGroundItem(10, 1, 5, 5, 0);

            Assert.True(server.DespawnGroundItem(id));
            Assert.False(server.DespawnGroundItem(id));
        }

        [Fact]
        public void GroundItemsInInterest_IncludesSameCell_ExcludesFarXY_AndFarZ()
        {
            var server = new GameServer(Config(), new GridMap());
            int near = server.SpawnGroundItem(1, 1, 10, 10, 0);
            server.SpawnGroundItem(2, 1, 100000, 100000, 0); // далеко по XY
            server.SpawnGroundItem(3, 1, 10, 10, 1000);      // далеко по Z

            var seen = server.GroundItemsInInterest(10f, 10f, 0);

            Assert.Contains(seen, it => it.NetId == near);
            Assert.DoesNotContain(seen, it => it.X == 100000);
            Assert.DoesNotContain(seen, it => it.Z == 1000);
        }

        [Fact]
        public void GroundItemEntity_IsNotClientConnection()
        {
            // BroadcastWorldSnapshot фильтрует `is ClientConnection` → предмет не должен попасть в player-снапшот.
            IWorldEntity item = new GroundItemEntity(1, 10, 1, 5, 5, 0);
            Assert.False(item is ClientConnection);
        }
    }
}
