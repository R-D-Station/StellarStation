using System;
using System.Threading;
using LiteNetLib;
using Shared.Configs;
using Shared.Messages.Interaction;
using Shared.World;
using Shared.World.Blocks;
using Shared.World.Items;
using Server.Network;

namespace ServerTests.Server.Network
{
    public class InventoryTests : IDisposable
    {
        private readonly SVars _config;
        private GameServer? _server;
        private int _testPort;

        public InventoryTests()
        {
            _config = new SVars
            {
                Ip = "127.0.0.1",
                Port = 0,
                MaxPlayers = 10,
                TickRate = 30,
                ConnectionKey = "InvTest"
            };
        }

        public void Dispose()
        {
            _server?.Stop();
        }

        private GameServer StartServer()
        {
            _config.MapPath = "";
            _server = new GameServer(_config);
            _server.Start();
            _testPort = _server.BoundPort;
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
            try
            {
                TestWait.Until(() => connectedPeer != null, () => clientManager.PollEvents(), what: "peer connected");
            }
            catch (TimeoutException)
            {
                clientManager.Stop();
                throw new Exception($"Failed to connect to server on port {_testPort}");
            }
            return connectedPeer;
        }

        private static ref HeldItem Hand(ClientConnection c, byte idx) => ref c.Slots[(int)SlotCategory.Hand][idx];
        private static ref HeldItem Slot(ClientConnection c, SlotCategory cat, byte idx) => ref c.Slots[(int)cat][idx];

        private static readonly System.Reflection.FieldInfo MainThreadActionsField =
            typeof(GameServer).GetField("_mainThreadActions",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        private static T OnGameLoop<T>(GameServer server, Func<T> func)
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
            TestWait.Until(() => Volatile.Read(ref completed), TestWait.DefaultTimeoutMs, "GameLoop ran queued action");
            if (error != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
            return result;
        }

        private static void OnGameLoop(GameServer server, Action action)
            => OnGameLoop<object?>(server, () => { action(); return null; });

        [Fact]
        public void Pickup_EmptyActiveHand_MovesGroundToHeld_RemovedFromGround()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 0, ActiveHand = 0 };

            int netId = OnGameLoop(server, () => server.SpawnGroundItem(10, 3, 5, 5, 0));
            Assert.Contains(server.GroundItemsInInterest(5.5f, 5.5f, 0), it => it.NetId == netId);

            OnGameLoop(server, () => InvokeHandlePickup(server, client, new PickupItem { TargetNetId = netId }));

            Assert.DoesNotContain(server.GroundItemsInInterest(5.5f, 5.5f, 0), it => it.NetId == netId);
            Assert.False(OnGameLoop(server, () => server.DespawnGroundItem(netId)));
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

            int netId = OnGameLoop(server, () => server.SpawnGroundItem(10, 1, 5, 5, 0));

            OnGameLoop(server, () => InvokeHandlePickup(server, client, new PickupItem { TargetNetId = netId }));

            Assert.True(OnGameLoop(server, () => server.DespawnGroundItem(netId)));
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

            int netId = OnGameLoop(server, () => server.SpawnGroundItem(10, 1, 5, 5, 0));

            OnGameLoop(server, () =>
            {
                InvokeHandlePickup(server, clientA, new PickupItem { TargetNetId = netId });
                InvokeHandlePickup(server, clientB, new PickupItem { TargetNetId = netId });
            });

            bool aHas = Hand(clientA, 0).NetId == netId;
            bool bHas = Hand(clientB, 0).NetId == netId;
            Assert.True(aHas ^ bHas);
            Assert.False(OnGameLoop(server, () => server.DespawnGroundItem(netId)));

            CleanupPeer(peerA);
            CleanupPeer(peerB);
        }

        [Fact]
        public void Drop_ReversesPickup_SameNetId_AtFloorOfFractionalPosition()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 4.9f, Y = 4.5f, Z = 1, ActiveHand = 0 };

            int netId = OnGameLoop(server, () => server.SpawnGroundItem(10, 2, 4, 4, 1));
            OnGameLoop(server, () => InvokeHandlePickup(server, client, new PickupItem { TargetNetId = netId }));
            Assert.Equal(netId, Hand(client, 0).NetId);
            Assert.False(OnGameLoop(server, () => server.DespawnGroundItem(netId)));

            OnGameLoop(server, () => InvokeHandleDrop(server, client, new DropItem { Category = SlotCategory.Hand, Index = 0 }));

            Assert.Equal(0, Hand(client, 0).NetId);
            var seen = server.GroundItemsInInterest(4.9f, 4.5f, 1);
            Assert.Contains(seen, it => it.NetId == netId && it.X == 4.9f && it.Y == 4.5f);

            CleanupPeer(peer);
        }

        [Fact]
        public void Disconnect_DropsAllHeldItems_AtLastTile_WithPreservedNetIds()
        {
            var server = StartServer();

            ClientConnection? spawned = null;
            server.OnClientConnected += c =>
            {
                c.Mover = new global::Shared.Simulation.Blocks.BlockMoverState(3.5f, 1f, 3.5f);
                c.X = c.Mover.X;
                c.Y = c.Mover.Z;
                c.Z = (int)MathF.Floor(c.Mover.Y);
                c.Slots[(int)SlotCategory.Hand][0] = new HeldItem { NetId = 501, ItemDefId = 7, StackCount = 1 };
                c.Slots[(int)SlotCategory.Belt][0] = new HeldItem { NetId = 502, ItemDefId = 8, StackCount = 5 };
                spawned = c;
            };

            var clientListener = new EventBasedNetListener();
            var clientManager = new NetManager(clientListener);
            clientManager.Start();
            var peer = clientManager.Connect("127.0.0.1", _testPort, _config.ConnectionKey);

            TestWait.Until(() => spawned != null, () => clientManager.PollEvents(), what: "item spawn received");
            Assert.NotNull(spawned);

            bool disconnected = false;
            server.OnClientDisconnected += _ => disconnected = true;
            peer.Disconnect();
            TestWait.Until(() => disconnected, () => clientManager.PollEvents(), what: "disconnect received");
            Assert.True(disconnected);

            var ground = server.GroundItemsInInterest(3.5f, 3.5f, 1);
            Assert.Contains(ground, it => it.NetId == 501);
            Assert.Contains(ground, it => it.NetId == 502);
            Assert.Equal(2, server.EntityCount);

            clientManager.Stop();
        }

        [Fact]
        public void Pickup_SendsInventorySync_OnlyToOwner_NotToOtherClient()
        {
            var server = StartServer();

            ClientConnection? serverClient = null;
            server.OnClientConnected += c => { if (serverClient == null) serverClient = c; };

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

            TestWait.Until(() => connectedA && connectedB,
                () => { managerA.PollEvents(); managerB.PollEvents(); }, what: "both clients connected");
            Assert.True(connectedA && connectedB);

            TestWait.Until(() => server.EntityCount >= 2 && serverClient != null,
                () => { managerA.PollEvents(); managerB.PollEvents(); }, what: "both entities registered");
            Assert.Equal(2, server.EntityCount);
            Assert.NotNull(serverClient);

            for (int i = 0; i < 20; i++) { managerA.PollEvents(); managerB.PollEvents(); Thread.Sleep(10); }
            inventorySyncReceivedByA = 0;
            inventorySyncReceivedByB = 0;

            OnGameLoop(server, () =>
            {
                int netId = server.SpawnGroundItem(10, 1, 0, 0, 0);
                server.UpdatePlayerPosition(serverClient!, 0.5f, 0.5f, 0, 0);
                InvokeHandlePickup(server, serverClient!, new PickupItem { TargetNetId = netId });
            });

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

            OnGameLoop(server, () => InvokeHandleSwapHand(server, client, new SwapHandRequest { Hand = 1 }));

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

            OnGameLoop(server, () => InvokeHandleMoveSlot(server, client, new MoveSlotRequest { FromCategory = SlotCategory.Belt, FromIndex = 0, ToCategory = SlotCategory.Hand, ToIndex = 1 }));

            Assert.Equal(0, Slot(client, SlotCategory.Belt, 0).NetId);
            Assert.Equal(10, Hand(client, 1).NetId);

            CleanupPeer(peer);
        }

        [Fact]
        public void MoveSlot_ToMatchingEquipCategory_Allowed()
        {
            var server = StartServer();
            server.ProtoLookup = _ => new ItemProto(1, SlotCategory.Belt, true, 1, 1, false, 0);
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 0.5f, Y = 0.5f, Z = 0 };
            Hand(client, 0) = new HeldItem { NetId = 10, ItemDefId = 1, StackCount = 1 };

            OnGameLoop(server, () => InvokeHandleMoveSlot(server, client, new MoveSlotRequest { FromCategory = SlotCategory.Hand, FromIndex = 0, ToCategory = SlotCategory.Belt, ToIndex = 0 }));

            Assert.Equal(0, Hand(client, 0).NetId);
            Assert.Equal(10, Slot(client, SlotCategory.Belt, 0).NetId);

            CleanupPeer(peer);
        }

        [Fact]
        public void MoveSlot_ToWrongEquipCategory_Rejected()
        {
            var server = StartServer();
            server.ProtoLookup = _ => new ItemProto(1, SlotCategory.Ear, true, 1, 1, false, 0);
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 0.5f, Y = 0.5f, Z = 0 };
            Hand(client, 0) = new HeldItem { NetId = 10, ItemDefId = 1, StackCount = 1 };

            OnGameLoop(server, () => InvokeHandleMoveSlot(server, client, new MoveSlotRequest { FromCategory = SlotCategory.Hand, FromIndex = 0, ToCategory = SlotCategory.Belt, ToIndex = 0 }));

            Assert.Equal(10, Hand(client, 0).NetId);
            Assert.Equal(0, Slot(client, SlotCategory.Belt, 0).NetId);

            CleanupPeer(peer);
        }

        [Fact]
        public void MoveSlot_OccupiedDest_Fails()
        {
            var server = StartServer();
            server.ProtoLookup = _ => new ItemProto(1, SlotCategory.Belt, true, 1, 1, false, 0);
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 0.5f, Y = 0.5f, Z = 0 };
            Hand(client, 0) = new HeldItem { NetId = 10, ItemDefId = 1, StackCount = 1 };
            Slot(client, SlotCategory.Belt, 0) = new HeldItem { NetId = 20, ItemDefId = 2, StackCount = 1 };

            OnGameLoop(server, () => InvokeHandleMoveSlot(server, client, new MoveSlotRequest { FromCategory = SlotCategory.Hand, FromIndex = 0, ToCategory = SlotCategory.Belt, ToIndex = 0 }));

            Assert.Equal(10, Hand(client, 0).NetId);
            Assert.Equal(20, Slot(client, SlotCategory.Belt, 0).NetId);

            CleanupPeer(peer);
        }

        [Fact]
        public void Join_SendsInitialInventorySync_AndSwapHandUpdatesActiveHand()
        {
            var server = StartServer();

            var listener = new EventBasedNetListener();
            var manager = new NetManager(listener);
            manager.Start();

            var activeHands = new System.Collections.Generic.List<byte>();
            listener.NetworkReceiveEvent += (peer, reader, channel, method) =>
            {
                if (reader.GetUShort() == (ushort)global::Shared.Messages.MessageType.InventorySync)
                {
                    var sync = new InventorySync();
                    sync.Deserialize(reader.GetBytesWithLength());
                    activeHands.Add(sync.ActiveHand);
                }
                reader.Recycle();
            };

            var peer = manager.Connect("127.0.0.1", _testPort, _config.ConnectionKey);

            TestWait.Until(() => activeHands.Count >= 1, () => manager.PollEvents(), what: "active hands received");
            Assert.True(activeHands.Count >= 1);
            Assert.Equal((byte)0, activeHands[0]);

            var writer = new LiteNetLib.Utils.NetDataWriter();
            writer.Put((ushort)global::Shared.Messages.MessageType.SwapHand);
            writer.PutBytesWithLength(new SwapHandRequest { Hand = 1 }.Serialize());
            peer.Send(writer, DeliveryMethod.ReliableOrdered);

            TestWait.Until(() => activeHands.Contains((byte)1), () => manager.PollEvents(), what: "hand slot 1 active");
            Assert.Contains((byte)1, activeHands);

            manager.Stop();
        }

        [Fact]
        public void Drop_InBlockWorld_SkipsTileWalkableGate()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 0 };
            Hand(client, 0) = new HeldItem { NetId = 77, ItemDefId = 3, StackCount = 1 };

            OnGameLoop(server, () => InvokeHandleDrop(server, client, new DropItem { Category = SlotCategory.Hand, Index = 0 }));

            Assert.Equal(0, Hand(client, 0).NetId);
            Assert.True(OnGameLoop(server, () => server.DespawnGroundItem(77)));

            CleanupPeer(peer);
        }

        [Fact]
        public void Pickup_InBlockWorld_ReachesItemOneCellUp()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 0 };

            int netId = OnGameLoop(server, () => server.SpawnGroundItem(9, 1, 5, 5, 1));
            OnGameLoop(server, () => InvokeHandlePickup(server, client, new PickupItem { TargetNetId = netId }));

            Assert.Equal(netId, Hand(client, 0).NetId);
            Assert.False(OnGameLoop(server, () => server.DespawnGroundItem(netId)));

            CleanupPeer(peer);
        }

        [Fact]
        public void Pickup_InBlockWorld_TwoCellsUp_NotReached()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 0 };

            int netId = OnGameLoop(server, () => server.SpawnGroundItem(9, 1, 5, 5, 2));
            OnGameLoop(server, () => InvokeHandlePickup(server, client, new PickupItem { TargetNetId = netId }));

            Assert.Equal(0, Hand(client, 0).NetId);
            Assert.True(OnGameLoop(server, () => server.DespawnGroundItem(netId)));

            CleanupPeer(peer);
        }

        [Fact]
        public void Drop_InBlockWorld_MidAir_SnapsToFloorCell()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 5 };
            Hand(client, 0) = new HeldItem { NetId = 88, ItemDefId = 3, StackCount = 1 };

            OnGameLoop(server, () => InvokeHandleDrop(server, client, new DropItem { Category = SlotCategory.Hand, Index = 0 }));

            Assert.Equal(0, Hand(client, 0).NetId);
            Assert.Contains(server.GroundItemsInInterest(5.5f, 5.5f, 1), it => it.NetId == 88 && it.Z == 1);
        }

        [Fact]
        public void Drop_InBlockWorld_StandingOnFloor_RestsInFeetCell()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 1 };
            Hand(client, 0) = new HeldItem { NetId = 89, ItemDefId = 3, StackCount = 1 };

            OnGameLoop(server, () => InvokeHandleDrop(server, client, new DropItem { Category = SlotCategory.Hand, Index = 0 }));

            Assert.Contains(server.GroundItemsInInterest(5.5f, 5.5f, 1), it => it.NetId == 89 && it.Z == 1);
        }

        [Fact]
        public void Drop_InBlockWorld_AboveHalfStep_RestsInStepCell()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 6.5f, Y = 0.5f, Z = 5 };
            Hand(client, 0) = new HeldItem { NetId = 90, ItemDefId = 3, StackCount = 1 };

            OnGameLoop(server, () => InvokeHandleDrop(server, client, new DropItem { Category = SlotCategory.Hand, Index = 0 }));

            Assert.Contains(server.GroundItemsInInterest(6.5f, 0.5f, 1), it => it.NetId == 90 && it.Z == 1);
        }

        [Fact]
        public void Drop_InBlockWorld_Vacuum_StaysAtDropCell()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 40.5f, Y = 40.5f, Z = 5 };
            Hand(client, 0) = new HeldItem { NetId = 91, ItemDefId = 3, StackCount = 1 };

            OnGameLoop(server, () => InvokeHandleDrop(server, client, new DropItem { Category = SlotCategory.Hand, Index = 0 }));

            Assert.Contains(server.GroundItemsInInterest(40.5f, 40.5f, 5), it => it.NetId == 91 && it.Z == 5);
        }

        [Fact]
        public void DropAllHeld_InBlockWorld_MidAir_AllSnapToFloor_NetIdsPreserved()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 5 };
            Hand(client, 0) = new HeldItem { NetId = 501, ItemDefId = 7, StackCount = 1 };
            Slot(client, SlotCategory.Belt, 0) = new HeldItem { NetId = 502, ItemDefId = 8, StackCount = 5 };

            OnGameLoop(server, () => Invoke(server, "DropAllHeldOnDisconnect", client));

            var ground = server.GroundItemsInInterest(5.5f, 5.5f, 1);
            Assert.Contains(ground, it => it.NetId == 501 && it.Z == 1);
            Assert.Contains(ground, it => it.NetId == 502 && it.Z == 1);

            CleanupPeer(peer);
        }

        [Fact]
        public void Pickup_SpanTwo_BothHandsFree_OccupiesBoth_SameNetId()
        {
            var server = StartServer();
            server.ProtoLookup = _ => new ItemProto(1, SlotCategory.Hand, false, 2, 1, false, 0);
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 0, ActiveHand = 0 };

            int netId = OnGameLoop(server, () => server.SpawnGroundItem(10, 1, 5, 5, 0));
            OnGameLoop(server, () => InvokeHandlePickup(server, client, new PickupItem { TargetNetId = netId }));

            Assert.Equal(netId, Hand(client, 0).NetId);
            Assert.Equal(netId, Hand(client, 1).NetId);
            Assert.False(OnGameLoop(server, () => server.DespawnGroundItem(netId)));

            CleanupPeer(peer);
        }

        [Fact]
        public void Pickup_SpanTwo_OneHandOccupied_Rejected_GroundIntact()
        {
            var server = StartServer();
            server.ProtoLookup = _ => new ItemProto(1, SlotCategory.Hand, false, 2, 1, false, 0);
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 0, ActiveHand = 0 };
            Hand(client, 0) = new HeldItem { NetId = 999, ItemDefId = 1, StackCount = 1 };

            int netId = OnGameLoop(server, () => server.SpawnGroundItem(10, 1, 5, 5, 0));
            OnGameLoop(server, () => InvokeHandlePickup(server, client, new PickupItem { TargetNetId = netId }));

            Assert.Equal(999, Hand(client, 0).NetId);
            Assert.Equal(0, Hand(client, 1).NetId);
            Assert.True(OnGameLoop(server, () => server.DespawnGroundItem(netId)));

            CleanupPeer(peer);
        }

        [Fact]
        public void Pickup_SpanExceedsHandCount_Rejected()
        {
            var server = StartServer();
            server.ProtoLookup = _ => new ItemProto(1, SlotCategory.Hand, false, 3, 1, false, 0);
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 0, ActiveHand = 0 };

            int netId = OnGameLoop(server, () => server.SpawnGroundItem(10, 1, 5, 5, 0));
            OnGameLoop(server, () => InvokeHandlePickup(server, client, new PickupItem { TargetNetId = netId }));

            Assert.Equal(0, Hand(client, 0).NetId);
            Assert.Equal(0, Hand(client, 1).NetId);
            Assert.True(OnGameLoop(server, () => server.DespawnGroundItem(netId)));

            CleanupPeer(peer);
        }

        [Fact]
        public void Drop_SpanTwo_ClearsBothSlots_SpawnsSingleGroundItem()
        {
            var server = StartServer();
            server.ProtoLookup = _ => new ItemProto(1, SlotCategory.Hand, false, 2, 1, false, 0);
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 1, ActiveHand = 0 };
            Hand(client, 0) = new HeldItem { NetId = 300, ItemDefId = 3, StackCount = 1 };
            Hand(client, 1) = new HeldItem { NetId = 300, ItemDefId = 3, StackCount = 1 };

            OnGameLoop(server, () => InvokeHandleDrop(server, client, new DropItem { Category = SlotCategory.Hand, Index = 1 }));

            Assert.Equal(0, Hand(client, 0).NetId);
            Assert.Equal(0, Hand(client, 1).NetId);
            int count = 0;
            foreach (var it in server.GroundItemsInInterest(5.5f, 5.5f, 1)) if (it.NetId == 300) count++;
            Assert.Equal(1, count);

            CleanupPeer(peer);
        }

        [Fact]
        public void MoveSlot_SpanTwo_TwoFreeInDest_OccupiesBoth()
        {
            var server = StartServer();
            server.ProtoLookup = _ => new ItemProto(1, SlotCategory.Ear, true, 2, 1, false, 0);
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 0.5f, Y = 0.5f, Z = 0 };
            Hand(client, 0) = new HeldItem { NetId = 10, ItemDefId = 1, StackCount = 1 };
            Hand(client, 1) = new HeldItem { NetId = 10, ItemDefId = 1, StackCount = 1 };

            OnGameLoop(server, () => InvokeHandleMoveSlot(server, client, new MoveSlotRequest { FromCategory = SlotCategory.Hand, FromIndex = 0, ToCategory = SlotCategory.Ear, ToIndex = 0 }));

            Assert.Equal(0, Hand(client, 0).NetId);
            Assert.Equal(0, Hand(client, 1).NetId);
            Assert.Equal(10, Slot(client, SlotCategory.Ear, 0).NetId);
            Assert.Equal(10, Slot(client, SlotCategory.Ear, 1).NetId);

            CleanupPeer(peer);
        }

        [Fact]
        public void MoveSlot_SpanTwo_OneFreeInDest_Rejected()
        {
            var server = StartServer();
            server.ProtoLookup = _ => new ItemProto(1, SlotCategory.Ear, true, 2, 1, false, 0);
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 0.5f, Y = 0.5f, Z = 0 };
            Hand(client, 0) = new HeldItem { NetId = 10, ItemDefId = 1, StackCount = 1 };
            Slot(client, SlotCategory.Ear, 0) = new HeldItem { NetId = 20, ItemDefId = 2, StackCount = 1 };

            OnGameLoop(server, () => InvokeHandleMoveSlot(server, client, new MoveSlotRequest { FromCategory = SlotCategory.Hand, FromIndex = 0, ToCategory = SlotCategory.Ear, ToIndex = 1 }));

            Assert.Equal(10, Hand(client, 0).NetId);
            Assert.Equal(20, Slot(client, SlotCategory.Ear, 0).NetId);
            Assert.Equal(0, Slot(client, SlotCategory.Ear, 1).NetId);

            CleanupPeer(peer);
        }

        [Fact]
        public void MoveSlot_SpanTwo_WithinOwnCategory_NotFalselyBlocked()
        {
            var server = StartServer();
            server.ProtoLookup = _ => new ItemProto(1, SlotCategory.Ear, true, 2, 1, false, 0);
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 0.5f, Y = 0.5f, Z = 0 };
            Slot(client, SlotCategory.Ear, 0) = new HeldItem { NetId = 10, ItemDefId = 1, StackCount = 1 };
            Slot(client, SlotCategory.Ear, 1) = new HeldItem { NetId = 10, ItemDefId = 1, StackCount = 1 };

            OnGameLoop(server, () => InvokeHandleMoveSlot(server, client, new MoveSlotRequest { FromCategory = SlotCategory.Ear, FromIndex = 0, ToCategory = SlotCategory.Ear, ToIndex = 1 }));

            Assert.Equal(10, Slot(client, SlotCategory.Ear, 0).NetId);
            Assert.Equal(10, Slot(client, SlotCategory.Ear, 1).NetId);

            CleanupPeer(peer);
        }

        [Fact]
        public void SendInventorySync_SpanTwoInBothHands_EmitsTwoRecords()
        {
            var server = StartServer();
            ClientConnection? serverClient = null;
            server.OnClientConnected += c => { if (serverClient == null) serverClient = c; };

            var listener = new EventBasedNetListener();
            var manager = new NetManager(listener);
            manager.Start();

            var received = new System.Collections.Generic.List<SlotRecord[]>();
            listener.NetworkReceiveEvent += (peer, reader, channel, method) =>
            {
                if (reader.GetUShort() == (ushort)global::Shared.Messages.MessageType.InventorySync)
                {
                    var sync = new InventorySync();
                    sync.Deserialize(reader.GetBytesWithLength());
                    received.Add(sync.Slots);
                }
                reader.Recycle();
            };

            manager.Connect("127.0.0.1", _testPort, _config.ConnectionKey);
            TestWait.Until(() => serverClient != null, () => manager.PollEvents(), what: "server accepted client");
            Assert.NotNull(serverClient);

            OnGameLoop(server, () =>
            {
                serverClient!.Slots[(int)SlotCategory.Hand][0] = new HeldItem { NetId = 10, ItemDefId = 5, StackCount = 1 };
                serverClient!.Slots[(int)SlotCategory.Hand][1] = new HeldItem { NetId = 10, ItemDefId = 5, StackCount = 1 };
                Invoke(server, "SendInventorySyncToOwner", serverClient!);
            });

            TestWait.Until(() => received.Count > 0 && received[received.Count - 1].Length == 2,
                () => manager.PollEvents(), what: "последний InventorySync содержит 2 записи");

            Assert.NotEmpty(received);
            var last = received[received.Count - 1];
            Assert.Equal(2, last.Length);
            Assert.All(last, r => Assert.Equal((ushort)5, r.ItemDefId));

            manager.Stop();
        }

        [Fact]
        public void DropAllHeld_SpanTwoInBothHands_SpawnsSingleGroundItem_NetIdDeduped()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 1 };
            Hand(client, 0) = new HeldItem { NetId = 700, ItemDefId = 3, StackCount = 1 };
            Hand(client, 1) = new HeldItem { NetId = 700, ItemDefId = 3, StackCount = 1 };

            OnGameLoop(server, () => Invoke(server, "DropAllHeldOnDisconnect", client));

            int count = 0;
            foreach (var it in server.GroundItemsInInterest(5.5f, 5.5f, 1)) if (it.NetId == 700) count++;
            Assert.Equal(1, count);

            CleanupPeer(peer);
        }

        [Fact]
        public void Drop_CollisionItem_LandsInFacingNeighborCell_NotUnderfoot()
        {
            var server = StartServer();
            server.ProtoLookup = _ => new ItemProto(1, SlotCategory.Hand, false, 1, 1, false, 0, true, BlockBox.Full);
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 1, ActiveHand = 0 };
            Hand(client, 0) = new HeldItem { NetId = 900, ItemDefId = 70, StackCount = 1 };

            OnGameLoop(server, () => InvokeHandleDrop(server, client, new DropItem { Category = SlotCategory.Hand, Index = 0 }));

            Assert.Equal(0, Hand(client, 0).NetId);
            Assert.Contains(server.GroundItemsInInterest(5.5f, 5.5f, 1), it => it.NetId == 900 && it.X == 5.5f && it.Y == 6.5f && it.Z == 1);

            CleanupPeer(peer);
        }

        [Fact]
        public void Drop_CollisionItem_AllNeighborsBlocked_Refused()
        {
            var server = StartServer();
            server.ProtoLookup = _ => new ItemProto(1, SlotCategory.Hand, false, 1, 1, false, 0, true, BlockBox.Full);
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 1, ActiveHand = 0 };
            Hand(client, 0) = new HeldItem { NetId = 900, ItemDefId = 70, StackCount = 1 };

            OnGameLoop(server, () =>
            {
                server.SpawnGroundItem(70, 1, 5, 6, 1);
                server.SpawnGroundItem(70, 1, 6, 5, 1);
                server.SpawnGroundItem(70, 1, 5, 4, 1);
                server.SpawnGroundItem(70, 1, 4, 5, 1);
                InvokeHandleDrop(server, client, new DropItem { Category = SlotCategory.Hand, Index = 0 });
            });

            Assert.Equal(900, Hand(client, 0).NetId);
            Assert.False(OnGameLoop(server, () => server.DespawnGroundItem(900)));

            CleanupPeer(peer);
        }

        [Fact]
        public void Disconnect_CollisionItem_AllNeighborsBlocked_ForcedUnderfoot()
        {
            var server = StartServer();
            server.ProtoLookup = _ => new ItemProto(1, SlotCategory.Hand, false, 1, 1, false, 0, true, BlockBox.Full);
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 1 };
            Hand(client, 0) = new HeldItem { NetId = 900, ItemDefId = 70, StackCount = 1 };

            OnGameLoop(server, () =>
            {
                server.SpawnGroundItem(70, 1, 5, 6, 1);
                server.SpawnGroundItem(70, 1, 6, 5, 1);
                server.SpawnGroundItem(70, 1, 5, 4, 1);
                server.SpawnGroundItem(70, 1, 4, 5, 1);
                Invoke(server, "DropAllHeldOnDisconnect", client);
            });

            Assert.Equal(0, Hand(client, 0).NetId);
            Assert.Contains(server.GroundItemsInInterest(5.5f, 5.5f, 1), it => it.NetId == 900 && it.X == 5.5f && it.Y == 5.5f && it.Z == 1);

            CleanupPeer(peer);
        }

        [Fact]
        public void Pickup_FractionalItemPosition_ReachableViaFloor()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 0, ActiveHand = 0 };

            int netId = OnGameLoop(server, () => server.SpawnGroundItem(10, 1, 5.3f, 5.7f, 0f));
            OnGameLoop(server, () => InvokeHandlePickup(server, client, new PickupItem { TargetNetId = netId }));

            Assert.Equal(netId, Hand(client, 0).NetId);
            Assert.False(OnGameLoop(server, () => server.DespawnGroundItem(netId)));

            CleanupPeer(peer);
        }

        [Fact]
        public void Pickup_FractionalItemPosition_OutOfReachViaFloor()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 0, ActiveHand = 0 };

            int netId = OnGameLoop(server, () => server.SpawnGroundItem(10, 1, 7.1f, 5.5f, 0f));
            OnGameLoop(server, () => InvokeHandlePickup(server, client, new PickupItem { TargetNetId = netId }));

            Assert.Equal(0, Hand(client, 0).NetId);
            Assert.True(OnGameLoop(server, () => server.DespawnGroundItem(netId)));

            CleanupPeer(peer);
        }

        [Fact]
        public void MoveSlot_ToUniform_Equippable_Allowed()
        {
            var server = StartServer();
            server.ProtoLookup = _ => new ItemProto(1, SlotCategory.Uniform, true, 1, 1, false, 0);
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 0.5f, Y = 0.5f, Z = 0, ActiveHand = 0 };
            Hand(client, 0) = new HeldItem { NetId = 10, ItemDefId = 1, StackCount = 1 };

            OnGameLoop(server, () => InvokeHandleMoveSlot(server, client, new MoveSlotRequest { FromCategory = SlotCategory.Hand, FromIndex = 0, ToCategory = SlotCategory.Uniform, ToIndex = 0 }));

            Assert.Equal(0, Hand(client, 0).NetId);
            Assert.Equal(10, Slot(client, SlotCategory.Uniform, 0).NetId);

            CleanupPeer(peer);
        }

        [Fact]
        public void MoveSlot_FromUniform_ToHand_Allowed()
        {
            var server = StartServer();
            server.ProtoLookup = _ => new ItemProto(1, SlotCategory.Uniform, true, 1, 1, false, 0);
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 0.5f, Y = 0.5f, Z = 0, ActiveHand = 0 };
            Slot(client, SlotCategory.Uniform, 0) = new HeldItem { NetId = 10, ItemDefId = 1, StackCount = 1 };

            OnGameLoop(server, () => InvokeHandleMoveSlot(server, client, new MoveSlotRequest { FromCategory = SlotCategory.Uniform, FromIndex = 0, ToCategory = SlotCategory.Hand, ToIndex = 0 }));

            Assert.Equal(0, Slot(client, SlotCategory.Uniform, 0).NetId);
            Assert.Equal(10, Hand(client, 0).NetId);

            CleanupPeer(peer);
        }

        [Fact]
        public void MoveSlot_ToUniform_NotEquippable_Rejected()
        {
            var server = StartServer();
            server.ProtoLookup = _ => new ItemProto(1, SlotCategory.Uniform, false, 1, 1, false, 0);
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 0.5f, Y = 0.5f, Z = 0, ActiveHand = 0 };
            Hand(client, 0) = new HeldItem { NetId = 10, ItemDefId = 1, StackCount = 1 };

            OnGameLoop(server, () => InvokeHandleMoveSlot(server, client, new MoveSlotRequest { FromCategory = SlotCategory.Hand, FromIndex = 0, ToCategory = SlotCategory.Uniform, ToIndex = 0 }));

            Assert.Equal(10, Hand(client, 0).NetId);
            Assert.Equal(0, Slot(client, SlotCategory.Uniform, 0).NetId);

            CleanupPeer(peer);
        }

        [Fact]
        public void MoveSlot_ToUniform_WrongEquipSlot_Rejected()
        {
            var server = StartServer();
            server.ProtoLookup = _ => new ItemProto(1, SlotCategory.Belt, true, 1, 1, false, 0);
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 0.5f, Y = 0.5f, Z = 0, ActiveHand = 0 };
            Hand(client, 0) = new HeldItem { NetId = 10, ItemDefId = 1, StackCount = 1 };

            OnGameLoop(server, () => InvokeHandleMoveSlot(server, client, new MoveSlotRequest { FromCategory = SlotCategory.Hand, FromIndex = 0, ToCategory = SlotCategory.Uniform, ToIndex = 0 }));

            Assert.Equal(10, Hand(client, 0).NetId);
            Assert.Equal(0, Slot(client, SlotCategory.Uniform, 0).NetId);

            CleanupPeer(peer);
        }

        [Fact]
        public void Broadcast_CarriesWornUniformDefId_ZeroThenSet()
        {
            var server = StartServer();
            ClientConnection? sc = null;
            server.OnClientConnected += c => { if (sc == null) sc = c; };
            var peer = CreateConnectedPeer();
            TestWait.Until(() => sc != null, what: "server registered client");
            Assert.NotNull(sc);

            ushort empty = OnGameLoop(server, () =>
            {
                InvokeBroadcast(server);
                return WornOf(server, sc!.PlayerNetId);
            });
            Assert.Equal((ushort)0, empty);

            ushort worn = OnGameLoop(server, () =>
            {
                sc!.Slots[(int)SlotCategory.Uniform][0] = new HeldItem { NetId = 55, ItemDefId = 77, StackCount = 1 };
                InvokeBroadcast(server);
                return WornOf(server, sc!.PlayerNetId);
            });
            Assert.Equal((ushort)77, worn);

            CleanupPeer(peer);
        }

        private static void InvokeBroadcast(GameServer s)
            => typeof(GameServer).GetMethod("BroadcastWorldSnapshot", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(s, null);

        private static ushort WornOf(GameServer s, int netId)
        {
            var ents = (global::Shared.Messages.Core.EntitySnapshot[])typeof(GameServer)
                .GetField("_broadcastEntities", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(s)!;
            for (int i = 0; i < ents.Length; i++)
                if (ents[i].NetId == netId) return ents[i].WornUniformDefId;
            return 0;
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

        private static readonly System.Reflection.FieldInfo InventoryField =
            typeof(GameServer).GetField("_inventory",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

        private static void Invoke(GameServer server, string methodName, params object[] args)
        {
            object inventory = InventoryField.GetValue(server)
                ?? throw new InvalidOperationException("Field _inventory not found on GameServer");
            var method = inventory.GetType().GetMethod(methodName,
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?? inventory.GetType().GetMethod(methodName,
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (method == null)
                throw new InvalidOperationException($"Method {methodName} not found on ServerInventorySystem");
            method.Invoke(inventory, args);
        }
    }
}
