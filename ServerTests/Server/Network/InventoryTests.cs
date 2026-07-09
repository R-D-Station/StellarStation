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
    /// <summary>Инвентарь (4.5a): pickup/drop/swap-hand/move-slot хендлеры сквозь реальный NetPeer (SendInventorySyncToOwner
    /// шлёт по Peer.Send). Покрывает DoD a-i: атомарность pickup-гонки, owner-only send, disconnect drop-all.</summary>
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

        // (a) pickup в пустую активную руку: Ground->Held, второй наблюдатель больше не видит его на земле.
        [Fact]
        public void Pickup_EmptyActiveHand_MovesGroundToHeld_RemovedFromGround()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 0, ActiveHand = InventorySlot.HandLeft };

            int netId = server.SpawnGroundItem(10, 3, 5, 5, 0);
            Assert.Contains(server.GroundItemsInInterest(5.5f, 5.5f, 0), it => it.NetId == netId); // на земле до подбора

            InvokeHandlePickup(server, client, new PickupItem { TargetNetId = netId });

            // ground-стрим больше не отдаёт предмет второму наблюдателю — точно не в _entities.
            Assert.DoesNotContain(server.GroundItemsInInterest(5.5f, 5.5f, 0), it => it.NetId == netId);
            Assert.False(server.DespawnGroundItem(netId));
            Assert.Equal(netId, client.Slots[InventorySlot.HandLeft].NetId);
            Assert.Equal((ushort)10, client.Slots[InventorySlot.HandLeft].ItemDefId);
            Assert.Equal((byte)3, client.Slots[InventorySlot.HandLeft].StackCount);

            CleanupPeer(peer);
        }

        // (b) pickup при занятой активной руке: no-op, наземный предмет цел, нет осиротевшего NetId.
        [Fact]
        public void Pickup_ActiveHandOccupied_NoOp_GroundItemIntact()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 5.5f, Y = 5.5f, Z = 0, ActiveHand = InventorySlot.HandLeft };
            client.Slots[InventorySlot.HandLeft] = new HeldItem { NetId = 999, ItemDefId = 1, StackCount = 1 };

            int netId = server.SpawnGroundItem(10, 1, 5, 5, 0);

            InvokeHandlePickup(server, client, new PickupItem { TargetNetId = netId });

            Assert.True(server.DespawnGroundItem(netId)); // предмет всё ещё на земле — цел, не осиротел
            Assert.Equal(999, client.Slots[InventorySlot.HandLeft].NetId); // рука не тронута (не перезаписана)

            CleanupPeer(peer);
        }

        // (c) два клиента pickup один NetId в один тик: ровно ОДИН получает предмет, второй — тихий no-op.
        [Fact]
        public void Pickup_TwoClientsSameNetId_ExactlyOneWins_NoDuplication()
        {
            var server = StartServer();
            var peerA = CreateConnectedPeer();
            var peerB = CreateConnectedPeer();
            var clientA = new ClientConnection(peerA, 1) { X = 5.5f, Y = 5.5f, Z = 0, ActiveHand = InventorySlot.HandLeft };
            var clientB = new ClientConnection(peerB, 2) { X = 5.5f, Y = 5.5f, Z = 0, ActiveHand = InventorySlot.HandLeft };

            int netId = server.SpawnGroundItem(10, 1, 5, 5, 0);

            // Один тик: оба хендлера дренируются последовательно (ProcessPickups по клиентам) — второй TryGetValue
            // на уже удалённый NetId эмулирует гонку двух pickup за один тик (DespawnGroundItem второго → false).
            InvokeHandlePickup(server, clientA, new PickupItem { TargetNetId = netId });
            InvokeHandlePickup(server, clientB, new PickupItem { TargetNetId = netId });

            bool aHas = clientA.Slots[InventorySlot.HandLeft].NetId == netId;
            bool bHas = clientB.Slots[InventorySlot.HandLeft].NetId == netId;
            Assert.True(aHas ^ bHas); // ровно один выиграл (XOR)
            Assert.False(server.DespawnGroundItem(netId)); // не осталось на земле, не задублировалось

            CleanupPeer(peerA);
            CleanupPeer(peerB);
        }

        // (d) drop разворачивает pickup с ТЕМ ЖЕ NetId на floor(client.X/Y) даже при дробном X, InReach туда-обратно.
        [Fact]
        public void Drop_ReversesPickup_SameNetId_AtFloorOfFractionalPosition()
        {
            var map = new GridMap();
            map.SetTile(4, 4, 0, Tile.Floor());
            var server = StartServer(map);
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 4.9f, Y = 4.5f, Z = 0, ActiveHand = InventorySlot.HandLeft };

            int netId = server.SpawnGroundItem(10, 2, 4, 4, 0);
            InvokeHandlePickup(server, client, new PickupItem { TargetNetId = netId });
            Assert.Equal(netId, client.Slots[InventorySlot.HandLeft].NetId);
            Assert.False(server.DespawnGroundItem(netId)); // подобран — больше не на земле

            InvokeHandleDrop(server, client, new DropItem { SlotIndex = InventorySlot.HandLeft });

            Assert.Equal(0, client.Slots[InventorySlot.HandLeft].NetId); // рука снова пуста
            var seen = server.GroundItemsInInterest(4.9f, 4.5f, 0);
            Assert.Contains(seen, it => it.NetId == netId && it.X == 4 && it.Y == 4); // floor(4.9)=4, floor(4.5)=4

            CleanupPeer(peer);
        }

        // (e) drop на не-Walkable тайл отклонён.
        [Fact]
        public void Drop_OnNonWalkableTile_Rejected()
        {
            var server = StartServer(new GridMap()); // пустая карта = Tile.Space везде = не-Walkable
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 10.5f, Y = 10.5f, Z = 0, ActiveHand = InventorySlot.HandLeft };
            client.Slots[InventorySlot.HandLeft] = new HeldItem { NetId = 42, ItemDefId = 1, StackCount = 1 };

            InvokeHandleDrop(server, client, new DropItem { SlotIndex = InventorySlot.HandLeft });

            Assert.Equal(42, client.Slots[InventorySlot.HandLeft].NetId); // рука не опустела
            Assert.False(server.DespawnGroundItem(42)); // предмет НЕ заспавнен на земле (rejected до SpawnGroundItemWithId)

            CleanupPeer(peer);
        }

        // (f) disconnect с предметами в руках роняет все held на землю на последнем тайле с сохранёнными NetId.
        [Fact]
        public void Disconnect_DropsAllHeldItems_AtLastTile_WithPreservedNetIds()
        {
            var map = new GridMap();
            map.SetTile(3, 3, 0, Tile.Floor());
            var server = StartServer(map);

            // OnClientConnected бежит через _mainThreadActions → колбэк исполняется СИНХРОННО на GameLoop-потоке
            // (см. GameLoop: PollEvents → drain _mainThreadActions → Process*). Мутируем X/Y/Z/Slots ПРЯМО в колбэке,
            // а не с тест-потока: так запись гарантированно происходит до любого Process*(ProcessFalls и т.п.) того же
            // тика и без гонки с GameLoop — не полагаемся на «сервер не успеет тикнуть» (было источником флейков 1/315).
            ClientConnection? spawned = null;
            server.OnClientConnected += c =>
            {
                c.X = 3.5f;
                c.Y = 3.5f;
                c.Z = 0;
                c.Slots[InventorySlot.HandLeft] = new HeldItem { NetId = 501, ItemDefId = 7, StackCount = 1 };
                c.Slots[InventorySlot.Belt] = new HeldItem { NetId = 502, ItemDefId = 8, StackCount = 5 };
                spawned = c; // публикуем ссылку ПОСЛЕДНЕЙ (после того как поля уже выставлены)
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

            // Игрок ушёл из реестра, но оба held-предмета должны были вернуться на землю (drop-all СИНХРОННО до Remove).
            var ground = server.GroundItemsInInterest(3.5f, 3.5f, 0);
            Assert.Contains(ground, it => it.NetId == 501);
            Assert.Contains(ground, it => it.NetId == 502);
            Assert.Equal(2, server.EntityCount); // ровно 2 предмета остались (игрок удалён)

            clientManager.Stop();
        }

        // (h) смена инвентаря игрока A шлёт НОЛЬ байт InventorySync игроку B (owner-only) — wire-level: слушаем
        // сырой NetworkReceiveEvent на КЛИЕНТСКОМ NetManager B и считаем InventorySync-пакеты после pickup игрока A.
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
            // Первый увиденный сервером клиент (порядок accept не гарантирован — не важно КАКОЙ это физически A/B).
            for (int i = 0; i < 100 && server.EntityCount < 2; i++) { managerA.PollEvents(); managerB.PollEvents(); Thread.Sleep(10); }
            Assert.Equal(2, server.EntityCount);
            Assert.NotNull(serverClient);

            int netId = server.SpawnGroundItem(10, 1, 0, 0, 0);
            server.UpdatePlayerPosition(serverClient!, 0.5f, 0.5f, 0, 0);
            InvokeHandlePickup(server, serverClient!, new PickupItem { TargetNetId = netId });

            for (int i = 0; i < 30; i++) { managerA.PollEvents(); managerB.PollEvents(); Thread.Sleep(10); }

            // Owner-only: ровно ОДИН из двух клиентских NetManager получил InventorySync (сам владелец), другой — ноль.
            Assert.True((inventorySyncReceivedByA > 0) ^ (inventorySyncReceivedByB > 0));
            Assert.Equal(1, inventorySyncReceivedByA + inventorySyncReceivedByB);

            managerA.Stop();
            managerB.Stop();
        }

        // (i) SwapHand ставит ActiveHand + bump version; MoveSlot в пустой слот успех, в занятый — fail.
        [Fact]
        public void SwapHand_SetsActiveHand_BumpsVersion()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 0.5f, Y = 0.5f, Z = 0, ActiveHand = InventorySlot.HandLeft };
            uint before = client.InventoryVersion;

            InvokeHandleSwapHand(server, client, new SwapHandRequest { Hand = 1 });

            Assert.Equal((byte)1, client.ActiveHand);
            Assert.True(client.InventoryVersion > before);

            CleanupPeer(peer);
        }

        [Fact]
        public void MoveSlot_EmptyDest_Succeeds()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 0.5f, Y = 0.5f, Z = 0 };
            client.Slots[InventorySlot.HandLeft] = new HeldItem { NetId = 10, ItemDefId = 1, StackCount = 1 };

            InvokeHandleMoveSlot(server, client, new MoveSlotRequest { FromSlot = InventorySlot.HandLeft, ToSlot = InventorySlot.Belt });

            Assert.Equal(0, client.Slots[InventorySlot.HandLeft].NetId);
            Assert.Equal(10, client.Slots[InventorySlot.Belt].NetId);

            CleanupPeer(peer);
        }

        [Fact]
        public void MoveSlot_OccupiedDest_Fails()
        {
            var server = StartServer();
            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1) { X = 0.5f, Y = 0.5f, Z = 0 };
            client.Slots[InventorySlot.HandLeft] = new HeldItem { NetId = 10, ItemDefId = 1, StackCount = 1 };
            client.Slots[InventorySlot.Belt] = new HeldItem { NetId = 20, ItemDefId = 2, StackCount = 1 };

            InvokeHandleMoveSlot(server, client, new MoveSlotRequest { FromSlot = InventorySlot.HandLeft, ToSlot = InventorySlot.Belt });

            Assert.Equal(10, client.Slots[InventorySlot.HandLeft].NetId); // не сдвинуто
            Assert.Equal(20, client.Slots[InventorySlot.Belt].NetId);     // dest не тронут

            CleanupPeer(peer);
        }

        private void CleanupPeer(NetPeer peer)
        {
            peer?.Disconnect();
            Thread.Sleep(30);
        }

        // Хендлеры private — зовём через рефлексию (мимо GameLoop-очередей, детерминированно, без гонки таймингов теста).
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
