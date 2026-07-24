using System;
using System.Collections.Generic;
using LiteNetLib;
using Server.Items;
using Server.Network;
using Shared.Configs;
using Shared.Simulation.Blocks;
using Shared.World;
using Shared.World.Blocks;
using Shared.World.Items;

namespace ServerTests.Server.Items
{
    public class GroundItemWorldObstacleTests
    {
        private const ushort SolidDef = 70;
        private const ushort PlainDef = 71;

        private static readonly Func<ushort, ItemProto?> Lookup = defId =>
            defId == SolidDef
                ? new ItemProto(SolidDef, SlotCategory.None, false, 1, 1, false, 0, true, BlockBox.Full)
                : new ItemProto(defId, SlotCategory.None, false, 1, 1, false, 0);

        private static GroundItemWorld World()
        {
            var w = new GroundItemWorld(new Dictionary<int, IWorldEntity>(), new NetIdAllocator(),
                new Dictionary<NetPeer, ClientConnection>());
            w.ProtoLookup = Lookup;
            return w;
        }

        private static (float minX, float minY, float minZ, float maxX, float maxY, float maxZ) Get0(IDynamicObstacles o)
        {
            o.Get(0, out float minX, out float minY, out float minZ, out float maxX, out float maxY, out float maxZ);
            return (minX, minY, minZ, maxX, maxY, maxZ);
        }

        [Fact]
        public void Rebuild_IndexesAabbCenteredOnPosition()
        {
            var w = World();
            w.SpawnGroundItem(SolidDef, 1, 3, 7, 2);
            w.RebuildObstacles();

            Assert.Equal(1, w.Obstacles.Count);
            Assert.Equal((2.5f, 2f, 6.5f, 3.5f, 3f, 7.5f), Get0(w.Obstacles));
        }

        [Fact]
        public void Rebuild_FractionalPosition_AabbCenteredExactly()
        {
            var w = World();
            int id = w.SpawnGroundItem(SolidDef, 1, 3.7f, 7.2f, 2.9f);
            w.RebuildObstacles();

            var a = Get0(w.Obstacles);
            Assert.Equal(3.2f, a.minX, 4);
            Assert.Equal(4.2f, a.maxX, 4);
            Assert.Equal(2.9f, a.minY, 4);
            Assert.Equal(3.9f, a.maxY, 4);
            Assert.Equal(6.7f, a.minZ, 4);
            Assert.Equal(7.7f, a.maxZ, 4);

            w.DespawnGroundItem(id);
            w.RebuildObstacles();
            Assert.Equal(0, w.Obstacles.Count);
        }

        [Fact]
        public void PlainItem_NotIndexed()
        {
            var w = World();
            w.SpawnGroundItem(PlainDef, 1, 3, 7, 2);
            w.RebuildObstacles();
            Assert.Equal(0, w.Obstacles.Count);
        }

        [Fact]
        public void MixedItems_OnlyCollisionIndexed()
        {
            var w = World();
            w.SpawnGroundItem(SolidDef, 1, 3, 7, 2);
            w.SpawnGroundItem(PlainDef, 1, 3, 7, 2);
            w.RebuildObstacles();
            Assert.Equal(1, w.Obstacles.Count);
        }

        [Fact]
        public void ClientAxisFormula_MirrorsServerAabb()
        {
            var w = World();
            w.SpawnGroundItem(SolidDef, 1, 3.5f, 7.25f, 2f);
            w.RebuildObstacles();

            var gi = new GroundItemEntity(999, SolidDef, 1, 3.5f, 7.25f, 2f);
            var inst = GroundItemWorld.ToInstance(gi);
            var box = BlockBox.Full;
            var clientSet = new DynamicObstacleSet();
            clientSet.Add(inst.X, inst.Z + box.MinYf, inst.Y, (box.MaxXf - box.MinXf) * 0.5f, box.MaxYf - box.MinYf);

            Assert.Equal(Get0(w.Obstacles), Get0(clientSet));
        }

        [Fact]
        public void GameServerProtoLookupSetter_ReachesGroundItemIndex()
        {
            var config = new SVars
            {
                Ip = "127.0.0.1",
                Port = 8999,
                MaxPlayers = 2,
                TickRate = 30,
                ConnectionKey = "ObstacleTest",
                MapPath = ""
            };
            var server = new GameServer(config);
            server.ProtoLookup = Lookup;

            server.SpawnGroundItem(SolidDef, 1, 4, 5, 0);

            var gw = (GroundItemWorld)typeof(GameServer)
                .GetField("_groundItems", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .GetValue(server)!;
            gw.RebuildObstacles();
            Assert.Equal(1, gw.Obstacles.Count);
            Assert.Equal((3.5f, 0f, 4.5f, 4.5f, 1f, 5.5f), Get0(gw.Obstacles));
        }
    }
}
