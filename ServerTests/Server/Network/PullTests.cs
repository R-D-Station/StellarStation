using System;
using System.Reflection;
using System.Threading;
using LiteNetLib;
using Shared.Configs;
using Shared.Messages.Interaction;
using Shared.Simulation;
using Shared.Simulation.Blocks;
using Shared.World;
using Shared.World.Items;
using Server.Network;

namespace ServerTests.Server.Network
{
    public class PullTests : IDisposable
    {
        private const ushort PullDef = 80;
        private const ushort PlainDef = 81;

        private readonly SVars _config;
        private GameServer? _server;
        private readonly int _testPort;
        private static int _portCounter = 8600;
        private static readonly object _portLock = new object();

        public PullTests()
        {
            lock (_portLock)
            {
                _testPort = _portCounter++;
                if (_testPort > 8800) _portCounter = 8600;
            }

            _config = new SVars
            {
                Ip = "127.0.0.1",
                Port = _testPort,
                MaxPlayers = 10,
                TickRate = 30,
                ConnectionKey = $"PullTest_{_testPort}"
            };
        }

        public void Dispose()
        {
            _server?.Stop();
            Thread.Sleep(50);
        }

        private GameServer StartServer()
        {
            _config.MapPath = "";
            _server = new GameServer(_config);
            _server.Start();
            Thread.Sleep(50);
            _server.ProtoLookup = defId => defId == PullDef
                ? new ItemProto(PullDef, SlotCategory.None, false, 1, 1, false, 0, pullable: true)
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

        private static ref HeldItem Hand(ClientConnection c, byte idx) => ref c.Slots[(int)SlotCategory.Hand][idx];

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

        private static void InvokePull(GameServer s, ClientConnection client, int netId)
        {
            var pull = typeof(GameServer).GetField("_pull", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s)!;
            var m = pull.GetType().GetMethod("HandlePull", BindingFlags.NonPublic | BindingFlags.Instance)!;
            m.Invoke(pull, new object[] { client, netId });
        }

        private static void InvokePullDisconnect(GameServer s, ClientConnection client)
        {
            var pull = typeof(GameServer).GetField("_pull", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s)!;
            var m = pull.GetType().GetMethod("OnClientDisconnect", BindingFlags.Public | BindingFlags.Instance)!;
            m.Invoke(pull, new object[] { client });
        }

        private static void InvokePickup(GameServer s, ClientConnection client, int targetNetId)
        {
            var inv = typeof(GameServer).GetField("_inventory", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s)!;
            var m = inv.GetType().GetMethod("HandlePickup", BindingFlags.NonPublic | BindingFlags.Instance)!;
            m.Invoke(inv, new object[] { client, new PickupItem { TargetNetId = targetNetId } });
        }

        [Fact]
        public void Grab_And_Release_SendPullSyncToOwner()
        {
            var server = StartServer();

            ClientConnection? serverClient = null;
            server.OnClientConnected += c => { if (serverClient == null) serverClient = c; };

            var listener = new EventBasedNetListener();
            var manager = new NetManager(listener);
            manager.Start();

            var syncs = new System.Collections.Generic.List<PullSync>();
            listener.NetworkReceiveEvent += (peer, reader, channel, method) =>
            {
                if (reader.GetUShort() == (ushort)global::Shared.Messages.MessageType.PullSync)
                {
                    var s = new PullSync();
                    s.Deserialize(reader.GetBytesWithLength());
                    syncs.Add(s);
                }
                reader.Recycle();
            };

            manager.Connect("127.0.0.1", _testPort, _config.ConnectionKey);
            for (int i = 0; i < 200 && serverClient == null; i++) { manager.PollEvents(); Thread.Sleep(10); }
            Assert.NotNull(serverClient);
            for (int i = 0; i < 80; i++) { manager.PollEvents(); Thread.Sleep(10); }

            int netId = 0;
            OnGameLoop(server, () =>
            {
                netId = server.SpawnGroundItem(PullDef, 1, 5, 5, 0);
                serverClient!.X = 5.5f;
                serverClient.Y = 5.5f;
                serverClient.Z = 0;
                serverClient.Mover = new BlockMoverState(5.5f, 0f, 5.5f);
                InvokePull(server, serverClient, netId);
            });
            Assert.Equal(netId, serverClient!.PulledNetId);
            for (int i = 0; i < 100 && !syncs.Exists(s => s.PulledNetId == netId); i++) { manager.PollEvents(); Thread.Sleep(10); }

            Assert.Contains(syncs, s => s.PulledNetId == netId && s.ItemDefId == PullDef);

            syncs.Clear();
            OnGameLoop(server, () => InvokePull(server, serverClient!, netId));
            for (int i = 0; i < 100 && !syncs.Exists(s => s.PulledNetId == 0); i++) { manager.PollEvents(); Thread.Sleep(10); }

            Assert.Contains(syncs, s => s.PulledNetId == 0 && s.ItemDefId == 0);

            manager.Stop();
        }

        private void CleanupPeer(NetPeer peer)
        {
            peer?.Disconnect();
            Thread.Sleep(30);
        }

        [Fact]
        public void Grab_ReachablePullableEmptyHands_SetsStateAndHalvesSpeed()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 0 };

            int netId = 0;
            OnGameLoop(server, () =>
            {
                netId = server.SpawnGroundItem(PullDef, 1, 5, 5, 0);
                InvokePull(server, client, netId);
            });

            Assert.Equal(netId, client.PulledNetId);
            Assert.Equal(MovementLogic.StepPerTick * 0.5f, client.Speed.CurrentValue, 6);
            CleanupPeer(peer);
        }

        [Fact]
        public void Grab_NotPullable_Rejected()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 0 };

            OnGameLoop(server, () =>
            {
                int netId = server.SpawnGroundItem(PlainDef, 1, 5, 5, 0);
                InvokePull(server, client, netId);
            });

            Assert.Equal(0, client.PulledNetId);
            Assert.Equal(MovementLogic.StepPerTick, client.Speed.CurrentValue, 6);
            CleanupPeer(peer);
        }

        [Fact]
        public void Grab_OutOfReach_Rejected()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 0 };

            OnGameLoop(server, () =>
            {
                int netId = server.SpawnGroundItem(PullDef, 1, 20, 20, 0);
                InvokePull(server, client, netId);
            });

            Assert.Equal(0, client.PulledNetId);
            Assert.Equal(MovementLogic.StepPerTick, client.Speed.CurrentValue, 6);
            CleanupPeer(peer);
        }

        [Fact]
        public void Grab_HandOccupied_Rejected()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 0 };
            Hand(client, 1) = new HeldItem { NetId = 555, ItemDefId = 1, StackCount = 1 };

            OnGameLoop(server, () =>
            {
                int netId = server.SpawnGroundItem(PullDef, 1, 5, 5, 0);
                InvokePull(server, client, netId);
            });

            Assert.Equal(0, client.PulledNetId);
            Assert.Equal(MovementLogic.StepPerTick, client.Speed.CurrentValue, 6);
            CleanupPeer(peer);
        }

        [Fact]
        public void Toggle_SameNetId_Releases_RestoresSpeed()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 0 };

            OnGameLoop(server, () =>
            {
                int netId = server.SpawnGroundItem(PullDef, 1, 5, 5, 0);
                InvokePull(server, client, netId);
                InvokePull(server, client, netId);
            });

            Assert.Equal(0, client.PulledNetId);
            Assert.Equal(MovementLogic.StepPerTick, client.Speed.CurrentValue, 6);
            CleanupPeer(peer);
        }

        [Fact]
        public void SwapCrate_SingleScale_NotStacked()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 0 };

            int idB = 0;
            OnGameLoop(server, () =>
            {
                int idA = server.SpawnGroundItem(PullDef, 1, 5, 5, 0);
                idB = server.SpawnGroundItem(PullDef, 1, 6, 5, 0);
                InvokePull(server, client, idA);
                InvokePull(server, client, idB);
            });

            Assert.Equal(idB, client.PulledNetId);
            Assert.Equal(MovementLogic.StepPerTick * 0.5f, client.Speed.CurrentValue, 6);
            CleanupPeer(peer);
        }

        [Fact]
        public void Grab_Rejected_CurrentPullIntact()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 0 };

            int idA = 0;
            OnGameLoop(server, () =>
            {
                idA = server.SpawnGroundItem(PullDef, 1, 5, 5, 0);
                int idFar = server.SpawnGroundItem(PullDef, 1, 20, 20, 0);
                InvokePull(server, client, idA);
                InvokePull(server, client, idFar);
            });

            Assert.Equal(idA, client.PulledNetId);
            Assert.Equal(MovementLogic.StepPerTick * 0.5f, client.Speed.CurrentValue, 6);
            CleanupPeer(peer);
        }

        [Fact]
        public void Pickup_BlockedWhilePulling()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 0 };
            client.PulledNetId = 999;

            int netId = 0;
            OnGameLoop(server, () =>
            {
                netId = server.SpawnGroundItem(1, 1, 5, 5, 0);
                InvokePickup(server, client, netId);
            });

            Assert.Equal(0, Hand(client, 0).NetId);
            Assert.True(OnGameLoopFunc(server, () => server.DespawnGroundItem(netId)));
            CleanupPeer(peer);
        }

        [Fact]
        public void Disconnect_WhilePulling_ReleasesScale_CrateStaysGround()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 0 };

            int netId = 0;
            OnGameLoop(server, () =>
            {
                netId = server.SpawnGroundItem(PullDef, 1, 5, 5, 0);
                InvokePull(server, client, netId);
                InvokePullDisconnect(server, client);
            });

            Assert.Equal(0, client.PulledNetId);
            Assert.Equal(MovementLogic.StepPerTick, client.Speed.CurrentValue, 6);
            Assert.Contains(server.GroundItemsInInterest(5.5f, 5.5f, 0), it => it.NetId == netId);
            CleanupPeer(peer);
        }

        private static T OnGameLoopFunc<T>(GameServer server, Func<T> func)
        {
            var queue = (System.Collections.Concurrent.ConcurrentQueue<Action>)MainThreadActionsField.GetValue(server)!;
            T result = default!;
            Exception? error = null;
            bool completed = false;
            queue.Enqueue(() =>
            {
                try { result = func(); }
                catch (Exception e) { error = e; }
                finally { Volatile.Write(ref completed, true); }
            });
            for (int i = 0; i < 500 && !Volatile.Read(ref completed); i++) Thread.Sleep(10);
            if (!Volatile.Read(ref completed)) throw new TimeoutException("GameLoop did not run the queued action");
            if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
            return result;
        }
    }
}
