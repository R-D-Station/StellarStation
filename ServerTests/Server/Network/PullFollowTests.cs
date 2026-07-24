using System;
using System.Reflection;
using System.Threading;
using LiteNetLib;
using Shared.Configs;
using Shared.Simulation;
using Shared.Simulation.Blocks;
using Shared.World.Blocks;
using Shared.World.Items;
using Server.Items;
using Server.Network;

namespace ServerTests.Server.Network
{
    public class PullFollowTests : IDisposable
    {
        private const ushort CrateDef = 90;
        private const ushort PlainDef = 91;

        private readonly SVars _config;
        private GameServer? _server;
        private readonly int _testPort;
        private static int _portCounter = 8850;
        private static readonly object _portLock = new object();

        public PullFollowTests()
        {
            lock (_portLock)
            {
                _testPort = _portCounter++;
                if (_testPort > 9050) _portCounter = 8850;
            }

            _config = new SVars
            {
                Ip = "127.0.0.1",
                Port = _testPort,
                MaxPlayers = 10,
                TickRate = 30,
                ConnectionKey = $"FollowTest_{_testPort}",
                BlocksWorld = true,
                MapPath = ""
            };
        }

        public void Dispose()
        {
            _server?.Stop();
            Thread.Sleep(50);
        }

        private GameServer StartServer()
        {
            _server = new GameServer(_config);
            _server.Start();
            Thread.Sleep(50);
            _server.ProtoLookup = defId => defId == CrateDef
                ? new ItemProto(CrateDef, SlotCategory.None, false, 1, 1, false, 0, true, BlockBox.Full, true)
                : new ItemProto(defId, SlotCategory.None, false, 1, 1, false, 0);
            return _server;
        }

        private NetPeer CreateConnectedPeer()
        {
            var clientListener = new EventBasedNetListener();
            var clientManager = new NetManager(clientListener);
            clientManager.Start();

            NetPeer? connectedPeer = null;
            bool connected = false;
            clientListener.PeerConnectedEvent += peer => { connectedPeer = peer; connected = true; };

            clientManager.Connect("127.0.0.1", _testPort, _config.ConnectionKey);
            for (int i = 0; i < 100 && !connected; i++) { clientManager.PollEvents(); Thread.Sleep(10); }

            if (connectedPeer == null)
            {
                clientManager.Stop();
                throw new Exception($"Failed to connect to server on port {_testPort}");
            }
            return connectedPeer;
        }

        private static readonly FieldInfo MainThreadActionsField =
            typeof(GameServer).GetField("_mainThreadActions", BindingFlags.NonPublic | BindingFlags.Instance)!;

        private static void OnGameLoop(GameServer server, Action action)
        {
            var queue = (System.Collections.Concurrent.ConcurrentQueue<Action>)MainThreadActionsField.GetValue(server)!;
            Exception? error = null;
            bool completed = false;
            queue.Enqueue(() =>
            {
                try { action(); }
                catch (Exception e) { error = e; }
                finally { Volatile.Write(ref completed, true); }
            });
            for (int i = 0; i < 500 && !Volatile.Read(ref completed); i++) Thread.Sleep(10);
            if (!Volatile.Read(ref completed)) throw new TimeoutException("GameLoop did not run the queued action");
            if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }

        private static void FollowOne(GameServer s, ClientConnection client)
        {
            var pull = typeof(GameServer).GetField("_pull", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s)!;
            var m = pull.GetType().GetMethod("FollowOne", BindingFlags.NonPublic | BindingFlags.Instance)!;
            m.Invoke(pull, new object[] { client });
        }

        private static void InvokePull(GameServer s, ClientConnection client, int netId)
        {
            var pull = typeof(GameServer).GetField("_pull", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s)!;
            var m = pull.GetType().GetMethod("HandlePull", BindingFlags.NonPublic | BindingFlags.Instance)!;
            m.Invoke(pull, new object[] { client, netId });
        }

        private static GroundItemWorld Ground(GameServer s) =>
            (GroundItemWorld)typeof(GameServer).GetField("_groundItems", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s)!;

        private static GroundItemEntity Entity(GameServer s, int netId)
        {
            var entities = (System.Collections.Generic.Dictionary<int, IWorldEntity>)typeof(GameServer)
                .GetField("_entities", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s)!;
            return (GroundItemEntity)entities[netId];
        }

        private static ClientConnection Puller(NetPeer peer, float x, float h, float z, int pulled)
        {
            var c = new ClientConnection(peer, 1) { X = x, Y = z, Z = (int)MathF.Floor(h) };
            c.Mover = new BlockMoverState(x, h, z);
            c.PulledNetId = pulled;
            c.AddSpeedScale(0.5f);
            return c;
        }

        private void CleanupPeer(NetPeer peer)
        {
            peer?.Disconnect();
            Thread.Sleep(30);
        }

        [Fact]
        public void Follow_MovesCrateTowardPlayer_StopsAtFollowDistance()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();

            int netId = 0;
            ClientConnection client = null!;
            OnGameLoop(server, () =>
            {
                netId = server.SpawnGroundItem(CrateDef, 1, 5.0f, 5.5f, 1f);
                client = Puller(peer, 7.0f, 1f, 5.5f, netId);
                for (int i = 0; i < 30; i++) FollowOne(server, client);
            });

            var gi = Entity(server, netId);
            Assert.Equal(5.6f, gi.CellX, 3);
            Assert.Equal(5.5f, gi.CellY, 3);
            Assert.Equal(1f, gi.Z, 3);
            Assert.Equal(netId, client.PulledNetId);

            CleanupPeer(peer);
        }

        [Fact]
        public void Follow_StopsAtWall_DoesNotPassThrough()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();

            int netId = 0;
            ClientConnection client = null!;
            OnGameLoop(server, () =>
            {
                server.BlockWorld!.SetBlock(6, 1, 5, DevBlockWorld.Full);
                netId = server.SpawnGroundItem(CrateDef, 1, 5.0f, 5.5f, 1f);
                client = Puller(peer, 7.4f, 1f, 5.5f, netId);
                for (int i = 0; i < 30; i++) FollowOne(server, client);
            });

            var gi = Entity(server, netId);
            Assert.True(gi.CellX <= 5.5f + 0.001f, $"X={gi.CellX}");
            Assert.True(gi.CellX >= 5.4f, $"X={gi.CellX}");
            Assert.Equal(netId, client.PulledNetId);

            CleanupPeer(peer);
        }

        [Fact]
        public void Follow_StopsAtOtherCollisionCrate()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();

            int netId = 0;
            ClientConnection client = null!;
            OnGameLoop(server, () =>
            {
                server.SpawnGroundItem(CrateDef, 1, 6.5f, 5.5f, 1f);
                netId = server.SpawnGroundItem(CrateDef, 1, 5.0f, 5.5f, 1f);
                client = Puller(peer, 7.4f, 1f, 5.5f, netId);
                for (int i = 0; i < 30; i++) FollowOne(server, client);
            });

            var gi = Entity(server, netId);
            Assert.True(gi.CellX <= 5.5f + 0.001f, $"X={gi.CellX}");
            Assert.True(gi.CellX >= 5.4f, $"X={gi.CellX}");

            CleanupPeer(peer);
        }

        [Fact]
        public void Follow_PlainItemInPath_DoesNotBlock()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();

            int netId = 0;
            ClientConnection client = null!;
            OnGameLoop(server, () =>
            {
                server.SpawnGroundItem(PlainDef, 1, 6.0f, 5.5f, 1f);
                netId = server.SpawnGroundItem(CrateDef, 1, 5.0f, 5.5f, 1f);
                client = Puller(peer, 7.0f, 1f, 5.5f, netId);
                for (int i = 0; i < 30; i++) FollowOne(server, client);
            });

            Assert.Equal(5.6f, Entity(server, netId).CellX, 3);

            CleanupPeer(peer);
        }

        [Fact]
        public void Leash_PlayerTooFar_ReleasesCrateStays()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();

            int netId = 0;
            ClientConnection client = null!;
            OnGameLoop(server, () =>
            {
                netId = server.SpawnGroundItem(CrateDef, 1, 5.0f, 5.5f, 1f);
                client = Puller(peer, 7.6f, 1f, 5.5f, netId);
                FollowOne(server, client);
            });

            Assert.Equal(0, client.PulledNetId);
            Assert.Equal(MovementLogic.StepPerTick, client.Speed.CurrentValue, 6);
            Assert.Equal(5.0f, Entity(server, netId).CellX, 3);

            CleanupPeer(peer);
        }

        [Fact]
        public void Leash_HeightDiverged_Releases()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();

            int netId = 0;
            ClientConnection client = null!;
            OnGameLoop(server, () =>
            {
                netId = server.SpawnGroundItem(CrateDef, 1, 5.0f, 5.5f, 1f);
                client = Puller(peer, 5.8f, 3f, 5.5f, netId);
                FollowOne(server, client);
            });

            Assert.Equal(0, client.PulledNetId);
            Assert.Equal(MovementLogic.StepPerTick, client.Speed.CurrentValue, 6);

            CleanupPeer(peer);
        }

        [Fact]
        public void MissingCrate_Releases()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();

            ClientConnection client = null!;
            OnGameLoop(server, () =>
            {
                client = Puller(peer, 5.0f, 1f, 5.0f, 999);
                FollowOne(server, client);
            });

            Assert.Equal(0, client.PulledNetId);
            Assert.Equal(MovementLogic.StepPerTick, client.Speed.CurrentValue, 6);

            CleanupPeer(peer);
        }

        [Fact]
        public void Grab_CrateAlreadyPulledByAnother_Rejected()
        {
            var server = StartServer();

            ClientConnection? realClient = null;
            server.OnClientConnected += c => { realClient ??= c; };
            var peer = CreateConnectedPeer();
            for (int i = 0; i < 200 && realClient == null; i++) Thread.Sleep(10);
            Assert.NotNull(realClient);

            int netId = 0;
            ClientConnection testClient = null!;
            OnGameLoop(server, () =>
            {
                netId = server.SpawnGroundItem(CrateDef, 1, 5.0f, 5.5f, 1f);
                realClient!.X = 5.5f;
                realClient.Y = 5.5f;
                realClient.Z = 1;
                realClient.Mover = new BlockMoverState(5.5f, 1f, 5.5f);
                InvokePull(server, realClient, netId);

                testClient = new ClientConnection(peer, 2) { X = 5.5f, Y = 5.5f, Z = 1 };
                InvokePull(server, testClient, netId);
            });

            Assert.Equal(netId, realClient!.PulledNetId);
            Assert.Equal(0, testClient.PulledNetId);
            Assert.Equal(MovementLogic.StepPerTick, testClient.Speed.CurrentValue, 6);

            CleanupPeer(peer);
        }

        [Fact]
        public void MoveGroundItem_UpdatesPositionAndObstacleAabb()
        {
            var server = StartServer();

            int netId = 0;
            OnGameLoop(server, () =>
            {
                netId = server.SpawnGroundItem(CrateDef, 1, 5.0f, 5.5f, 1f);
                Assert.True(Ground(server).MoveGroundItem(netId, 6.25f, 5.75f, 1f));
                Ground(server).RebuildObstacles();
            });

            Assert.Contains(server.GroundItemsInInterest(6.25f, 5.75f, 1), it => it.NetId == netId && it.X == 6.25f && it.Y == 5.75f);

            var obstacles = Ground(server).Obstacles;
            Assert.Equal(1, obstacles.Count);
            obstacles.Get(0, out float minX, out float minY, out float minZ, out float maxX, out float maxY, out float maxZ);
            Assert.Equal(5.75f, minX, 3);
            Assert.Equal(6.75f, maxX, 3);
            Assert.Equal(1f, minY, 3);
            Assert.Equal(2f, maxY, 3);
            Assert.Equal(5.25f, minZ, 3);
            Assert.Equal(6.25f, maxZ, 3);
        }
    }
}
