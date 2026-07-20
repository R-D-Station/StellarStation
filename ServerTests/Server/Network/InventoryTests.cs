using System;
using System.Threading;
using LiteNetLib;
using Shared.Configs;
using Shared.Messages.Interaction;
using Shared.World;
using Shared.World.Items;
using Server.Network;

namespace ServerTests.Server.Network
{
    public class InventoryTests : IDisposable
    {
        private readonly SVars _config;
        private GameServer? _server;
        private readonly int _testPort;
        private static int _portCounter = 8100;
        private static readonly object _portLock = new object();

        public InventoryTests()
        {
            lock (_portLock)
            {
                _testPort = _portCounter++;
                if (_testPort > 8300) _portCounter = 8100;
            }

            _config = new SVars
            {
                Ip = "127.0.0.1",
                Port = _testPort,
                MaxPlayers = 10,
                TickRate = 30,
                ConnectionKey = $"InvTest_{_testPort}"
            };
        }

        public void Dispose()
        {
            _server?.Stop();
            Thread.Sleep(50);
        }

        private GameServer StartServer(GridMap? map = null)
        {
            _server = new GameServer(_config, map);
            _server.Start();
            Thread.Sleep(50);
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
        private static ref HeldItem Slot(ClientConnection c, SlotCategory cat, byte idx) => ref c.Slots[(int)cat][idx];

        [Fact]
        public void Pickup_EmptyActiveHand_MovesGroundToHeld_RemovedFromGround()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 0, ActiveHand = 0 };

            int netId = server.SpawnGroundItem(10, 3, 5, 5, 0);
            Assert.Contains(server.GroundItemsInInterest(5.5f, 5.5f, 0), it => it.NetId == netId);

            InvokeHandlePickup(server, client, new PickupItem { TargetNetId = netId });

            Assert.DoesNotContain(server.GroundItemsInInterest(5.5f, 5.5f, 0), it => it.NetId == netId);
            Assert.False(server.DespawnGroundItem(netId));
            Assert.Equal(netId, Hand(client, 0).NetId);
            Assert.Equal((ushort)10, Hand(client, 0).ItemDefId);
            Assert.Equal((byte)3, Hand(client, 0).StackCount);

            CleanupPeer(peer);
        }

        [Fact]
        public void Pickup_ActiveHandOccupied_NoOp_GroundItemIntact()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 0, ActiveHand = 0 };
            Hand(client, 0) = new HeldItem { NetId = 999, ItemDefId = 1, StackCount = 1 };

            int netId = server.SpawnGroundItem(10, 1, 5, 5, 0);

            InvokeHandlePickup(server, client, new PickupItem { TargetNetId = netId });

            Assert.True(server.DespawnGroundItem(netId));
            Assert.Equal(999, Hand(client, 0).NetId);

            CleanupPeer(peer);
        }

        [Fact]
        public void Pickup_TwoClientsSameNetId_ExactlyOneWins_NoDuplication()
        {
            var server = StartServer();
            var peerA = CreateConnectedPeer();
            var peerB = CreateConnectedPeer();
            var clientA = new ClientConnection(peerA, 1) { X = 5.5f, Y = 5.5f, Z = 0, ActiveHand = 0 };
            var clientB = new ClientConnection(peerB, 2) { X = 5.5f, Y = 5.5f, Z = 0, ActiveHand = 0 };

            int netId = server.SpawnGroundItem(10, 1, 5, 5, 0);

            InvokeHandlePickup(server, clientA, new PickupItem { TargetNetId = netId });
            InvokeHandlePickup(server, clientB, new PickupItem { TargetNetId = netId });

            bool aHas = Hand(clientA, 0).NetId == netId;
            bool bHas = Hand(clientB, 0).NetId == netId;
            Assert.True(aHas ^ bHas);
            Assert.False(server.DespawnGroundItem(netId));

            CleanupPeer(peerA);
            CleanupPeer(peerB);
        }

        [Fact]
        public void Drop_ReversesPickup_SameNetId_AtFloorOfFractionalPosition()
        {
            var map = new GridMap();
            map.SetTile(4, 4, 0, Tile.Floor());
            var server = StartServer(map);
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 4.9f, Y = 4.5f, Z = 0, ActiveHand = 0 };

            int netId = server.SpawnGroundItem(10, 2, 4, 4, 0);
            InvokeHandlePickup(server, client, new PickupItem { TargetNetId = netId });
            Assert.Equal(netId, Hand(client, 0).NetId);
            Assert.False(server.DespawnGroundItem(netId));

            InvokeHandleDrop(server, client, new DropItem { Category = SlotCategory.Hand, Index = 0 });

            Assert.Equal(0, Hand(client, 0).NetId);
            var seen = server.GroundItemsInInterest(4.9f, 4.5f, 0);
            Assert.Contains(seen, it => it.NetId == netId && it.X == 4 && it.Y == 4);

            CleanupPeer(peer);
        }

        [Fact]
        public void Drop_OnNonWalkableTile_Rejected()
        {
            var server = StartServer(new GridMap());
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 10.5f, Y = 10.5f, Z = 0, ActiveHand = 0 };
            Hand(client, 0) = new HeldItem { NetId = 42, ItemDefId = 1, StackCount = 1 };

            InvokeHandleDrop(server, client, new DropItem { Category = SlotCategory.Hand, Index = 0 });

            Assert.Equal(42, Hand(client, 0).NetId);
            Assert.False(server.DespawnGroundItem(42));

            CleanupPeer(peer);
        }

        [Fact]
        public void Disconnect_DropsAllHeldItems_AtLastTile_WithPreservedNetIds()
        {
            var map = new GridMap();
            map.SetTile(3, 3, 0, Tile.Floor());
            var server = StartServer(map);

            ClientConnection? spawned = null;
            server.OnClientConnected += c =>
            {
                c.X = 3.5f;
                c.Y = 3.5f;
                c.Z = 0;
                c.Slots[(int)SlotCategory.Hand][0] = new HeldItem { NetId = 501, ItemDefId = 7, StackCount = 1 };
                c.Slots[(int)SlotCategory.Belt][0] = new HeldItem { NetId = 502, ItemDefId = 8, StackCount = 5 };
                spawned = c;
            };

            var clientListener = new EventBasedNetListener();
            var clientManager = new NetManager(clientListener);
            clientManager.Start();
            var peer = clientManager.Connect("127.0.0.1", _testPort, _config.ConnectionKey);

            for (int i = 0; i < 200 && spawned == null; i++) { clientManager.PollEvents(); Thread.Sleep(10); }
            Assert.NotNull(spawned);

            bool disconnected = false;
            server.OnClientDisconnected += _ => disconnected = true;
            peer.Disconnect();
            for (int i = 0; i < 200 && !disconnected; i++) { clientManager.PollEvents(); Thread.Sleep(10); }
            Assert.True(disconnected);

            var ground = server.GroundItemsInInterest(3.5f, 3.5f, 0);
            Assert.Contains(ground, it => it.NetId == 501);
            Assert.Contains(ground, it => it.NetId == 502);
            Assert.Equal(2, server.EntityCount);

            clientManager.Stop();
        }

        [Fact]
        public void Pickup_SendsInventorySync_OnlyToOwner_NotToOtherClient()
        {
            var server = StartServer();

            bool connectedA = false, connectedB = false;

            var listenerA = new EventBasedNetListener();
            var managerA = new NetManager(listenerA);
            managerA.Start();
            listenerA.PeerConnectedEvent += _ => connectedA = true;
            managerA.Connect("127.0.0.1", _testPort, _config.ConnectionKey);

            var listenerB = new EventBasedNetListener();
            var managerB = new NetManager(listenerB);
            managerB.Start();
            listenerB.PeerConnectedEvent += _ => connectedB = true;
            managerB.Connect("127.0.0.1", _testPort, _config.ConnectionKey);

            int inventorySyncReceivedByA = 0, inventorySyncReceivedByB = 0;
            listenerA.NetworkReceiveEvent += (peer, reader, channel, method) =>
            {
                if (reader.GetUShort() == (ushort)global::Shared.Messages.MessageType.InventorySync) inventorySyncReceivedByA++;
                reader.Recycle();
            };
            listenerB.NetworkReceiveEvent += (peer, reader, channel, method) =>
            {
                if (reader.GetUShort() == (ushort)global::Shared.Messages.MessageType.InventorySync) inventorySyncReceivedByB++;
                reader.Recycle();
            };

            for (int i = 0; i < 100 && !(connectedA && connectedB); i++)
            {
                managerA.PollEvents(); managerB.PollEvents(); Thread.Sleep(10);
            }
            Assert.True(connectedA && connectedB);

            ClientConnection? serverClient = null;
            server.OnClientConnected += c => { if (serverClient == null) serverClient = c; };
            for (int i = 0; i < 100 && server.EntityCount < 2; i++) { managerA.PollEvents(); managerB.PollEvents(); Thread.Sleep(10); }
            Assert.Equal(2, server.EntityCount);
            Assert.NotNull(serverClient);

            int netId = server.SpawnGroundItem(10, 1, 0, 0, 0);
            server.UpdatePlayerPosition(serverClient!, 0.5f, 0.5f, 0, 0);
            InvokeHandlePickup(server, serverClient!, new PickupItem { TargetNetId = netId });

            for (int i = 0; i < 30; i++) { managerA.PollEvents(); managerB.PollEvents(); Thread.Sleep(10); }

            Assert.True((inventorySyncReceivedByA > 0) ^ (inventorySyncReceivedByB > 0));
            Assert.Equal(1, inventorySyncReceivedByA + inventorySyncReceivedByB);

            managerA.Stop();
            managerB.Stop();
        }

        [Fact]
        public void SwapHand_SetsActiveHand()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 0.5f, Y = 0.5f, Z = 0, ActiveHand = 0 };

            InvokeHandleSwapHand(server, client, new SwapHandRequest { Hand = 1 });

            Assert.Equal((byte)1, client.ActiveHand);

            CleanupPeer(peer);
        }

        [Fact]
        public void MoveSlot_ToHand_AlwaysAllowed()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 0.5f, Y = 0.5f, Z = 0 };
            Slot(client, SlotCategory.Belt, 0) = new HeldItem { NetId = 10, ItemDefId = 1, StackCount = 1 };

            InvokeHandleMoveSlot(server, client, new MoveSlotRequest { FromCategory = SlotCategory.Belt, FromIndex = 0, ToCategory = SlotCategory.Hand, ToIndex = 1 });

            Assert.Equal(0, Slot(client, SlotCategory.Belt, 0).NetId);
            Assert.Equal(10, Hand(client, 1).NetId);

            CleanupPeer(peer);
        }

        [Fact]
        public void MoveSlot_ToMatchingEquipCategory_Allowed()
        {
            var server = StartServer();
            server.EquipLookup = _ => SlotCategory.Belt;
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 0.5f, Y = 0.5f, Z = 0 };
            Hand(client, 0) = new HeldItem { NetId = 10, ItemDefId = 1, StackCount = 1 };

            InvokeHandleMoveSlot(server, client, new MoveSlotRequest { FromCategory = SlotCategory.Hand, FromIndex = 0, ToCategory = SlotCategory.Belt, ToIndex = 0 });

            Assert.Equal(0, Hand(client, 0).NetId);
            Assert.Equal(10, Slot(client, SlotCategory.Belt, 0).NetId);

            CleanupPeer(peer);
        }

        [Fact]
        public void MoveSlot_ToWrongEquipCategory_Rejected()
        {
            var server = StartServer();
            server.EquipLookup = _ => SlotCategory.Ear;
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 0.5f, Y = 0.5f, Z = 0 };
            Hand(client, 0) = new HeldItem { NetId = 10, ItemDefId = 1, StackCount = 1 };

            InvokeHandleMoveSlot(server, client, new MoveSlotRequest { FromCategory = SlotCategory.Hand, FromIndex = 0, ToCategory = SlotCategory.Belt, ToIndex = 0 });

            Assert.Equal(10, Hand(client, 0).NetId);
            Assert.Equal(0, Slot(client, SlotCategory.Belt, 0).NetId);

            CleanupPeer(peer);
        }

        [Fact]
        public void MoveSlot_OccupiedDest_Fails()
        {
            var server = StartServer();
            server.EquipLookup = _ => SlotCategory.Belt;
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 0.5f, Y = 0.5f, Z = 0 };
            Hand(client, 0) = new HeldItem { NetId = 10, ItemDefId = 1, StackCount = 1 };
            Slot(client, SlotCategory.Belt, 0) = new HeldItem { NetId = 20, ItemDefId = 2, StackCount = 1 };

            InvokeHandleMoveSlot(server, client, new MoveSlotRequest { FromCategory = SlotCategory.Hand, FromIndex = 0, ToCategory = SlotCategory.Belt, ToIndex = 0 });

            Assert.Equal(10, Hand(client, 0).NetId);
            Assert.Equal(20, Slot(client, SlotCategory.Belt, 0).NetId);

            CleanupPeer(peer);
        }

        private void CleanupPeer(NetPeer peer)
        {
            peer?.Disconnect();
            Thread.Sleep(30);
        }

        private static void InvokeHandlePickup(GameServer server, ClientConnection client, PickupItem msg)
            => Invoke(server, "HandlePickup", client, msg);

        private static void InvokeHandleDrop(GameServer server, ClientConnection client, DropItem msg)
            => Invoke(server, "HandleDrop", client, msg);

        private static void InvokeHandleSwapHand(GameServer server, ClientConnection client, SwapHandRequest msg)
            => Invoke(server, "HandleSwapHand", client, msg);

        private static void InvokeHandleMoveSlot(GameServer server, ClientConnection client, MoveSlotRequest msg)
            => Invoke(server, "HandleMoveSlot", client, msg);

        private static void Invoke(GameServer server, string methodName, params object[] args)
        {
            var method = typeof(GameServer).GetMethod(methodName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (method == null)
                throw new InvalidOperationException($"Method {methodName} not found on GameServer");
            method.Invoke(server, args);
        }
    }
}
