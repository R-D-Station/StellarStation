using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using LiteNetLib;
using Shared.Configs;
using Shared.Messages.Interaction;
using Shared.World;
using Shared.World.Items;
using Server.Network;

namespace ServerTests.Server.Network
{
    /// <summary>UI-режим контейнеров: viewer-refcount, open/close/put/take, фильтры, разрыв соединения.</summary>
    public class ContainerTests : IDisposable
    {
        private const ushort ContDef = 50;
        private const ushort InnerContDef = 51;
        private const ushort ItemDef = 60;

        private readonly SVars _config;
        private GameServer? _server;
        private readonly int _testPort;
        private static int _portCounter = 8400;
        private static readonly object _portLock = new object();

        public ContainerTests()
        {
            lock (_portLock)
            {
                _testPort = _portCounter++;
                if (_testPort > 8600) _portCounter = 8400;
            }

            _config = new SVars
            {
                Ip = "127.0.0.1",
                Port = _testPort,
                MaxPlayers = 10,
                TickRate = 30,
                ConnectionKey = $"ContTest_{_testPort}"
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
            SetContainerProto(_server);
            return _server;
        }

        private static void SetContainerProto(GameServer server)
        {
            server.ProtoLookup = defId => defId >= 50 && defId < 60
                ? new ItemProto(defId, SlotCategory.None, false, 1, 1, true, 2)
                : new ItemProto(defId, SlotCategory.None, false, 1, 1, false, 0);
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
        private static ref HeldItem Slot(ClientConnection c, SlotCategory cat, byte idx) => ref c.Slots[(int)cat][idx];

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

        private static object ContainersObj(GameServer s) =>
            typeof(GameServer).GetField("_containers", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s)!;

        private static void InvokeContainer(GameServer s, string method, params object[] args)
        {
            var obj = ContainersObj(s);
            var m = obj.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? obj.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.Instance);
            if (m == null) throw new InvalidOperationException($"Method {method} not found");
            m.Invoke(obj, args);
        }

        private static Dictionary<int, List<HeldItem>> ContentsOf(GameServer s)
        {
            var obj = ContainersObj(s);
            return (Dictionary<int, List<HeldItem>>)obj.GetType()
                .GetField("_contents", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(obj)!;
        }

        private static void InvokeInventory(GameServer s, string method, params object[] args)
        {
            var inv = typeof(GameServer).GetField("_inventory", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(s)!;
            var m = inv.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!;
            m.Invoke(inv, args);
        }

        private static bool IsWorldOpen(GameServer s, int netId)
        {
            var obj = ContainersObj(s);
            return (bool)obj.GetType().GetMethod("IsWorldOpen")!.Invoke(obj, new object[] { netId })!;
        }

        private void CleanupPeer(NetPeer peer)
        {
            peer?.Disconnect();
            Thread.Sleep(30);
        }

        [Fact]
        public void Open_HeldContainer_AddsViewer()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 0.5f, Y = 0.5f, Z = 0 };
            Slot(client, SlotCategory.Belt, 0) = new HeldItem { NetId = 1000, ItemDefId = ContDef, StackCount = 1 };

            OnGameLoop(server, () => InvokeContainer(server, "HandleOpen", client, 1000));

            Assert.Contains(1000, client.OpenContainers);
            CleanupPeer(peer);
        }

        [Fact]
        public void Open_NonContainer_Rejected()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 0.5f, Y = 0.5f, Z = 0 };
            Slot(client, SlotCategory.Belt, 0) = new HeldItem { NetId = 1001, ItemDefId = ItemDef, StackCount = 1 };

            OnGameLoop(server, () => InvokeContainer(server, "HandleOpen", client, 1001));

            Assert.DoesNotContain(1001, client.OpenContainers);
            CleanupPeer(peer);
        }

        [Fact]
        public void Open_GroundContainer_InReach_AddsViewer()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 0 };

            int netId = 0;
            OnGameLoop(server, () =>
            {
                netId = server.SpawnGroundItem(ContDef, 1, 5, 5, 0);
                InvokeContainer(server, "HandleOpen", client, netId);
            });

            Assert.Contains(netId, client.OpenContainers);
            CleanupPeer(peer);
        }

        [Fact]
        public void Open_GroundContainer_OutOfReach_Rejected()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 0 };

            int netId = 0;
            OnGameLoop(server, () =>
            {
                netId = server.SpawnGroundItem(ContDef, 1, 20, 20, 0);
                InvokeContainer(server, "HandleOpen", client, netId);
            });

            Assert.DoesNotContain(netId, client.OpenContainers);
            CleanupPeer(peer);
        }

        [Fact]
        public void Close_RemovesViewer()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 0.5f, Y = 0.5f, Z = 0 };
            Slot(client, SlotCategory.Belt, 0) = new HeldItem { NetId = 1000, ItemDefId = ContDef, StackCount = 1 };

            OnGameLoop(server, () =>
            {
                InvokeContainer(server, "HandleOpen", client, 1000);
                InvokeContainer(server, "HandleClose", client, 1000);
            });

            Assert.DoesNotContain(1000, client.OpenContainers);
            CleanupPeer(peer);
        }

        [Fact]
        public void Put_HandItem_MovesToContainer_HandEmpties()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 0.5f, Y = 0.5f, Z = 0, ActiveHand = 0 };
            Slot(client, SlotCategory.Belt, 0) = new HeldItem { NetId = 1000, ItemDefId = ContDef, StackCount = 1 };
            Hand(client, 0) = new HeldItem { NetId = 2000, ItemDefId = ItemDef, StackCount = 1 };

            OnGameLoop(server, () =>
            {
                InvokeContainer(server, "HandleOpen", client, 1000);
                InvokeContainer(server, "HandlePut", client, 1000);
            });

            Assert.Equal(0, Hand(client, 0).NetId);
            var list = ContentsOf(server)[1000];
            Assert.Single(list);
            Assert.Equal(2000, list[0].NetId);
            CleanupPeer(peer);
        }

        [Fact]
        public void Put_ContainerFull_Rejected()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 0.5f, Y = 0.5f, Z = 0, ActiveHand = 0 };
            Slot(client, SlotCategory.Belt, 0) = new HeldItem { NetId = 1000, ItemDefId = ContDef, StackCount = 1 };

            OnGameLoop(server, () =>
            {
                InvokeContainer(server, "HandleOpen", client, 1000);
                Hand(client, 0) = new HeldItem { NetId = 2001, ItemDefId = ItemDef, StackCount = 1 };
                InvokeContainer(server, "HandlePut", client, 1000);
                Hand(client, 0) = new HeldItem { NetId = 2002, ItemDefId = ItemDef, StackCount = 1 };
                InvokeContainer(server, "HandlePut", client, 1000);
                Hand(client, 0) = new HeldItem { NetId = 2003, ItemDefId = ItemDef, StackCount = 1 };
                InvokeContainer(server, "HandlePut", client, 1000);
            });

            Assert.Equal(2, ContentsOf(server)[1000].Count);
            Assert.Equal(2003, Hand(client, 0).NetId);
            CleanupPeer(peer);
        }

        [Fact]
        public void Put_EmptyHand_Rejected()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 0.5f, Y = 0.5f, Z = 0, ActiveHand = 0 };
            Slot(client, SlotCategory.Belt, 0) = new HeldItem { NetId = 1000, ItemDefId = ContDef, StackCount = 1 };

            OnGameLoop(server, () =>
            {
                InvokeContainer(server, "HandleOpen", client, 1000);
                InvokeContainer(server, "HandlePut", client, 1000);
            });

            Assert.False(ContentsOf(server).TryGetValue(1000, out var list) && list.Count > 0);
            CleanupPeer(peer);
        }

        [Fact]
        public void Put_EmptyNestedContainer_Allowed()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 0.5f, Y = 0.5f, Z = 0, ActiveHand = 0 };
            Slot(client, SlotCategory.Belt, 0) = new HeldItem { NetId = 1000, ItemDefId = ContDef, StackCount = 1 };
            Hand(client, 0) = new HeldItem { NetId = 1001, ItemDefId = InnerContDef, StackCount = 1 };

            OnGameLoop(server, () =>
            {
                InvokeContainer(server, "HandleOpen", client, 1000);
                InvokeContainer(server, "HandlePut", client, 1000);
            });

            Assert.Equal(0, Hand(client, 0).NetId);
            Assert.Equal(1001, ContentsOf(server)[1000][0].NetId);
            CleanupPeer(peer);
        }

        [Fact]
        public void Put_NonEmptyNestedContainer_Rejected()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 0.5f, Y = 0.5f, Z = 0, ActiveHand = 0 };
            Slot(client, SlotCategory.Belt, 0) = new HeldItem { NetId = 1000, ItemDefId = ContDef, StackCount = 1 };
            Hand(client, 0) = new HeldItem { NetId = 1001, ItemDefId = InnerContDef, StackCount = 1 };

            OnGameLoop(server, () =>
            {
                ContentsOf(server)[1001] = new List<HeldItem> { new HeldItem { NetId = 3000, ItemDefId = ItemDef, StackCount = 1 } };
                InvokeContainer(server, "HandleOpen", client, 1000);
                InvokeContainer(server, "HandlePut", client, 1000);
            });

            Assert.Equal(1001, Hand(client, 0).NetId);
            Assert.False(ContentsOf(server).TryGetValue(1000, out var list) && list.Count > 0);
            CleanupPeer(peer);
        }

        [Fact]
        public void Put_IntoItself_Rejected()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 0.5f, Y = 0.5f, Z = 0, ActiveHand = 0 };
            Hand(client, 0) = new HeldItem { NetId = 1000, ItemDefId = ContDef, StackCount = 1 };

            OnGameLoop(server, () =>
            {
                InvokeContainer(server, "HandleOpen", client, 1000);
                InvokeContainer(server, "HandlePut", client, 1000);
            });

            Assert.Equal(1000, Hand(client, 0).NetId);
            Assert.False(ContentsOf(server).TryGetValue(1000, out var list) && list.Count > 0);
            CleanupPeer(peer);
        }

        [Fact]
        public void Take_ToEmptyHand_MovesItem()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 0.5f, Y = 0.5f, Z = 0, ActiveHand = 0 };
            Slot(client, SlotCategory.Belt, 0) = new HeldItem { NetId = 1000, ItemDefId = ContDef, StackCount = 1 };

            OnGameLoop(server, () =>
            {
                ContentsOf(server)[1000] = new List<HeldItem> { new HeldItem { NetId = 2000, ItemDefId = ItemDef, StackCount = 1 } };
                InvokeContainer(server, "HandleOpen", client, 1000);
                InvokeContainer(server, "HandleTake", client, 1000, (ushort)0);
            });

            Assert.Equal(2000, Hand(client, 0).NetId);
            Assert.Empty(ContentsOf(server)[1000]);
            CleanupPeer(peer);
        }

        [Fact]
        public void Take_OccupiedHand_Rejected()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 0.5f, Y = 0.5f, Z = 0, ActiveHand = 0 };
            Slot(client, SlotCategory.Belt, 0) = new HeldItem { NetId = 1000, ItemDefId = ContDef, StackCount = 1 };
            Hand(client, 0) = new HeldItem { NetId = 3000, ItemDefId = ItemDef, StackCount = 1 };

            OnGameLoop(server, () =>
            {
                ContentsOf(server)[1000] = new List<HeldItem> { new HeldItem { NetId = 2000, ItemDefId = ItemDef, StackCount = 1 } };
                InvokeContainer(server, "HandleOpen", client, 1000);
                InvokeContainer(server, "HandleTake", client, 1000, (ushort)0);
            });

            Assert.Equal(3000, Hand(client, 0).NetId);
            Assert.Single(ContentsOf(server)[1000]);
            CleanupPeer(peer);
        }

        [Fact]
        public void Take_BadIndex_Rejected()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 0.5f, Y = 0.5f, Z = 0, ActiveHand = 0 };
            Slot(client, SlotCategory.Belt, 0) = new HeldItem { NetId = 1000, ItemDefId = ContDef, StackCount = 1 };

            OnGameLoop(server, () =>
            {
                ContentsOf(server)[1000] = new List<HeldItem> { new HeldItem { NetId = 2000, ItemDefId = ItemDef, StackCount = 1 } };
                InvokeContainer(server, "HandleOpen", client, 1000);
                InvokeContainer(server, "HandleTake", client, 1000, (ushort)5);
            });

            Assert.Equal(0, Hand(client, 0).NetId);
            Assert.Single(ContentsOf(server)[1000]);
            CleanupPeer(peer);
        }

        [Fact]
        public void Put_GroundContainerOutOfReach_Rejected()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 0, ActiveHand = 0 };
            Hand(client, 0) = new HeldItem { NetId = 2000, ItemDefId = ItemDef, StackCount = 1 };

            int netId = 0;
            OnGameLoop(server, () =>
            {
                netId = server.SpawnGroundItem(ContDef, 1, 20, 20, 0);
                client.OpenContainers.Add(netId);
                InvokeContainer(server, "HandlePut", client, netId);
            });

            Assert.Equal(2000, Hand(client, 0).NetId);
            Assert.False(ContentsOf(server).TryGetValue(netId, out var list) && list.Count > 0);
            CleanupPeer(peer);
        }

        [Fact]
        public void Open_SendsContainerSync_ToOwner()
        {
            var server = StartServer();

            ClientConnection? serverClient = null;
            server.OnClientConnected += c => { if (serverClient == null) serverClient = c; };

            var listener = new EventBasedNetListener();
            var manager = new NetManager(listener);
            manager.Start();

            var syncs = new List<ContainerSync>();
            listener.NetworkReceiveEvent += (peer, reader, channel, method) =>
            {
                if (reader.GetUShort() == (ushort)global::Shared.Messages.MessageType.ContainerSync)
                {
                    var sync = new ContainerSync();
                    sync.Deserialize(reader.GetBytesWithLength());
                    syncs.Add(sync);
                }
                reader.Recycle();
            };

            manager.Connect("127.0.0.1", _testPort, _config.ConnectionKey);
            for (int i = 0; i < 200 && serverClient == null; i++) { manager.PollEvents(); Thread.Sleep(10); }
            Assert.NotNull(serverClient);

            OnGameLoop(server, () =>
            {
                serverClient!.Slots[(int)SlotCategory.Belt][0] = new HeldItem { NetId = 1000, ItemDefId = ContDef, StackCount = 1 };
                ContentsOf(server)[1000] = new List<HeldItem> { new HeldItem { NetId = 2000, ItemDefId = ItemDef, StackCount = 7 } };
                InvokeContainer(server, "HandleOpen", serverClient!, 1000);
            });

            for (int i = 0; i < 30; i++) { manager.PollEvents(); Thread.Sleep(10); }

            Assert.Contains(syncs, s => s.ContainerNetId == 1000 && s.Items.Length == 1 && s.Items[0].ItemDefId == ItemDef);
            manager.Stop();
        }

        [Fact]
        public void Disconnect_ClearsViewers_ContentsIntact_ContainerOnGround()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 1 };
            Slot(client, SlotCategory.Belt, 0) = new HeldItem { NetId = 1000, ItemDefId = ContDef, StackCount = 1 };
            client.OpenContainers.Add(1000);

            OnGameLoop(server, () =>
            {
                ContentsOf(server)[1000] = new List<HeldItem> { new HeldItem { NetId = 2000, ItemDefId = ItemDef, StackCount = 1 } };
                InvokeInventory(server, "DropAllHeldOnDisconnect", client);
                InvokeContainer(server, "OnClientDisconnect", client);
            });

            Assert.Empty(client.OpenContainers);
            Assert.True(ContentsOf(server).ContainsKey(1000));
            Assert.Single(ContentsOf(server)[1000]);
            Assert.Contains(server.GroundItemsInInterest(5.5f, 5.5f, 1), it => it.NetId == 1000);
            CleanupPeer(peer);
        }

        [Fact]
        public void WorldOpen_NoViewers_False()
        {
            var server = StartServer();
            Assert.False(IsWorldOpen(server, 999));
        }

        [Fact]
        public void WorldOpen_ViewerRefcount_TwoOpenTwoClose()
        {
            var server = StartServer();
            var peerA = CreateConnectedPeer();
            var peerB = CreateConnectedPeer();
            var a = new ClientConnection(peerA, 1) { X = 5.5f, Y = 5.5f, Z = 0 };
            var b = new ClientConnection(peerB, 2) { X = 5.5f, Y = 5.5f, Z = 0 };

            int netId = 0;
            OnGameLoop(server, () =>
            {
                netId = server.SpawnGroundItem(ContDef, 1, 5, 5, 0);
                InvokeContainer(server, "HandleOpen", a, netId);
            });
            Assert.True(IsWorldOpen(server, netId));

            OnGameLoop(server, () => InvokeContainer(server, "HandleOpen", b, netId));
            Assert.True(IsWorldOpen(server, netId));

            OnGameLoop(server, () => InvokeContainer(server, "HandleClose", a, netId));
            Assert.True(IsWorldOpen(server, netId));

            OnGameLoop(server, () => InvokeContainer(server, "HandleClose", b, netId));
            Assert.False(IsWorldOpen(server, netId));

            CleanupPeer(peerA);
            CleanupPeer(peerB);
        }

        [Fact]
        public void WorldOpen_RepeatedOpenSameClient_NoDoubleCount()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var a = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 0 };

            int netId = 0;
            OnGameLoop(server, () =>
            {
                netId = server.SpawnGroundItem(ContDef, 1, 5, 5, 0);
                InvokeContainer(server, "HandleOpen", a, netId);
                InvokeContainer(server, "HandleOpen", a, netId);
            });
            Assert.True(IsWorldOpen(server, netId));

            OnGameLoop(server, () => InvokeContainer(server, "HandleClose", a, netId));
            Assert.False(IsWorldOpen(server, netId));

            CleanupPeer(peer);
        }

        [Fact]
        public void WorldOpen_DisconnectLastViewer_Closes()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var a = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 0 };

            int netId = 0;
            OnGameLoop(server, () =>
            {
                netId = server.SpawnGroundItem(ContDef, 1, 5, 5, 0);
                InvokeContainer(server, "HandleOpen", a, netId);
            });
            Assert.True(IsWorldOpen(server, netId));

            OnGameLoop(server, () => InvokeContainer(server, "OnClientDisconnect", a));
            Assert.False(IsWorldOpen(server, netId));

            CleanupPeer(peer);
        }

        [Fact]
        public void WorldOpen_DisconnectOneOfTwo_StaysOpen()
        {
            var server = StartServer();
            var peerA = CreateConnectedPeer();
            var peerB = CreateConnectedPeer();
            var a = new ClientConnection(peerA, 1) { X = 5.5f, Y = 5.5f, Z = 0 };
            var b = new ClientConnection(peerB, 2) { X = 5.5f, Y = 5.5f, Z = 0 };

            int netId = 0;
            OnGameLoop(server, () =>
            {
                netId = server.SpawnGroundItem(ContDef, 1, 5, 5, 0);
                InvokeContainer(server, "HandleOpen", a, netId);
                InvokeContainer(server, "HandleOpen", b, netId);
            });

            OnGameLoop(server, () => InvokeContainer(server, "OnClientDisconnect", a));
            Assert.True(IsWorldOpen(server, netId));

            CleanupPeer(peerA);
            CleanupPeer(peerB);
        }

        [Fact]
        public void Put_Whitelist_AllowedItem_Accepted()
        {
            var server = StartServer();
            server.ProtoLookup = defId => defId == ContDef
                ? new ItemProto(ContDef, SlotCategory.None, false, 1, 1, true, 2, filterMode: ContainerFilterMode.Whitelist, filterItemIds: new ushort[] { ItemDef })
                : new ItemProto(defId, SlotCategory.None, false, 1, 1, false, 0);
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 0.5f, Y = 0.5f, Z = 0, ActiveHand = 0 };
            Slot(client, SlotCategory.Belt, 0) = new HeldItem { NetId = 1000, ItemDefId = ContDef, StackCount = 1 };
            Hand(client, 0) = new HeldItem { NetId = 2000, ItemDefId = ItemDef, StackCount = 1 };

            OnGameLoop(server, () =>
            {
                InvokeContainer(server, "HandleOpen", client, 1000);
                InvokeContainer(server, "HandlePut", client, 1000);
            });

            Assert.Equal(0, Hand(client, 0).NetId);
            Assert.Single(ContentsOf(server)[1000]);
            CleanupPeer(peer);
        }

        [Fact]
        public void Put_Whitelist_DisallowedItem_Rejected()
        {
            var server = StartServer();
            server.ProtoLookup = defId => defId == ContDef
                ? new ItemProto(ContDef, SlotCategory.None, false, 1, 1, true, 2, filterMode: ContainerFilterMode.Whitelist, filterItemIds: new ushort[] { ItemDef })
                : new ItemProto(defId, SlotCategory.None, false, 1, 1, false, 0);
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 0.5f, Y = 0.5f, Z = 0, ActiveHand = 0 };
            Slot(client, SlotCategory.Belt, 0) = new HeldItem { NetId = 1000, ItemDefId = ContDef, StackCount = 1 };
            Hand(client, 0) = new HeldItem { NetId = 2000, ItemDefId = 61, StackCount = 1 };

            OnGameLoop(server, () =>
            {
                InvokeContainer(server, "HandleOpen", client, 1000);
                InvokeContainer(server, "HandlePut", client, 1000);
            });

            Assert.Equal(2000, Hand(client, 0).NetId);
            Assert.False(ContentsOf(server).TryGetValue(1000, out var list) && list.Count > 0);
            CleanupPeer(peer);
        }

        [Fact]
        public void Put_Blacklist_DisallowedItem_Rejected()
        {
            var server = StartServer();
            server.ProtoLookup = defId => defId == ContDef
                ? new ItemProto(ContDef, SlotCategory.None, false, 1, 1, true, 2, filterMode: ContainerFilterMode.Blacklist, filterItemIds: new ushort[] { ItemDef })
                : new ItemProto(defId, SlotCategory.None, false, 1, 1, false, 0);
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 0.5f, Y = 0.5f, Z = 0, ActiveHand = 0 };
            Slot(client, SlotCategory.Belt, 0) = new HeldItem { NetId = 1000, ItemDefId = ContDef, StackCount = 1 };
            Hand(client, 0) = new HeldItem { NetId = 2000, ItemDefId = ItemDef, StackCount = 1 };

            OnGameLoop(server, () =>
            {
                InvokeContainer(server, "HandleOpen", client, 1000);
                InvokeContainer(server, "HandlePut", client, 1000);
            });

            Assert.Equal(2000, Hand(client, 0).NetId);
            Assert.False(ContentsOf(server).TryGetValue(1000, out var list) && list.Count > 0);
            CleanupPeer(peer);
        }
    }
}
