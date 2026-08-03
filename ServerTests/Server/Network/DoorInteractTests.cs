using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using LiteNetLib;
using Server.Network;
using Server.Network.Interaction;
using Shared.Configs;
using Shared.Messages.Interaction;
using Shared.Simulation.Blocks;
using Shared.World.Blocks;

namespace ServerTests.Server.Doors
{
    internal static class TestDoorCatalog
    {
        internal const ushort InteractDoor = 900;
        internal const ushort InteractDoorTall = 901;
        internal const ushort InteractDoorWide = 902;
        internal const ushort AutoDoor = 6;

        private static readonly TriggerBox[] Triggers =
        {
            new TriggerBox(0, 0, -20, 16, 32, 4),
            new TriggerBox(0, 0, 12, 16, 32, 36)
        };

        [ModuleInitializer]
        internal static void Seed()
        {
            var field = typeof(BlockCatalog).GetField("_byId", BindingFlags.NonPublic | BindingFlags.Static)!;
            var byId = (Dictionary<ushort, BlockInfo>)field.GetValue(null)!;

            byId[InteractDoor] = new BlockInfo(InteractDoor, "TestInteractDoor", BlockCategory.Door,
                BlockFaceFlags.All, BlockFaceFlags.All, 0,
                new[] { new[] { BlockBox.Full } }, 1, 1, 1,
                new[] { Array.Empty<BlockBox>() }, DoorOpening.Interact, Triggers, 0.1f);

            byId[InteractDoorTall] = new BlockInfo(InteractDoorTall, "TestInteractDoorTall", BlockCategory.Door,
                BlockFaceFlags.All, BlockFaceFlags.All, 0,
                new[] { new[] { BlockBox.Full }, new[] { BlockBox.Full } }, 1, 2, 1,
                new[] { Array.Empty<BlockBox>(), Array.Empty<BlockBox>() }, DoorOpening.Interact, Triggers, 0.1f);

            byId[InteractDoorWide] = new BlockInfo(InteractDoorWide, "TestInteractDoorWide", BlockCategory.Door,
                BlockFaceFlags.All, BlockFaceFlags.All, 0,
                new[] { new[] { BlockBox.Full }, new[] { BlockBox.Full } }, 2, 1, 1,
                new[] { Array.Empty<BlockBox>(), Array.Empty<BlockBox>() }, DoorOpening.Interact, Triggers, 0.1f);
        }
    }

    /// <summary>DoorOpening.Interact: тоггл через реестр интеракций, резолв якоря, авто-закрытие, изоляция от Auto.</summary>
    public class DoorInteractTests : IDisposable
    {
        private const int DoorX = 5, DoorY = 1, DoorZ = 6;

        private readonly SVars _config;
        private readonly NetManager _dummy;
        private readonly NetPeer _peer;
        private GameServer? _server;

        public DoorInteractTests()
        {
            _config = new SVars
            {
                Ip = "127.0.0.1",
                Port = 0,
                MaxPlayers = 10,
                TickRate = 30,
                MapPath = "",
                ConnectionKey = "DoorTest"
            };

            _dummy = new NetManager(new EventBasedNetListener());
            _dummy.Start();
            _peer = _dummy.Connect("127.0.0.1", 1, _config.ConnectionKey);
            _dummy.Stop(); // нужен только объект-ключ словаря клиентов; трафик тестам не нужен
        }

        public void Dispose()
        {
            _server?.Stop();
            _dummy.Stop();
        }

        private GameServer Server() => _server ??= new GameServer(_config);

        private static readonly MethodInfo RebuildMethod =
            typeof(GameServer).GetMethod("BuildAutoDoorRegistry", BindingFlags.NonPublic | BindingFlags.Instance)!;
        private static readonly MethodInfo DoorTickMethod =
            typeof(GameServer).GetMethod("ProcessBlockDoors", BindingFlags.NonPublic | BindingFlags.Instance)!;
        private static readonly FieldInfo TickField =
            typeof(GameServer).GetField("_currentTick", BindingFlags.NonPublic | BindingFlags.Instance)!;
        private static readonly FieldInfo ClientsField =
            typeof(GameServer).GetField("_clients", BindingFlags.NonPublic | BindingFlags.Instance)!;

        private static void RebuildDoors(GameServer s) => RebuildMethod.Invoke(s, null);

        private static void DoorTicks(GameServer s, int count)
        {
            for (int i = 0; i < count; i++)
            {
                DoorTickMethod.Invoke(s, null);
                TickField.SetValue(s, (uint)TickField.GetValue(s)! + 1u);
            }
        }

        private void Register(GameServer s, ClientConnection client)
            => ((Dictionary<NetPeer, ClientConnection>)ClientsField.GetValue(s)!)[_peer] = client;

        private GameServer WithDoor(ushort type, int facing = 0, int x = DoorX, int y = DoorY, int z = DoorZ)
        {
            var s = Server();
            var grid = s.BlockWorld!;

            var info = BlockCatalog.Get(type);
            int parts = MultiBlock.PartCount(info.SizeX, info.SizeY, info.SizeZ);
            for (int p = 0; p < parts; p++)
            {
                MultiBlock.PartWorldOffset(p, info.SizeX, info.SizeZ, facing, out int dx, out int dy, out int dz);
                grid.SetBlock(x + dx, y + dy, z + dz, 0);
            }
            Assert.True(grid.PlaceMultiBlock(x, y, z, type, facing), "мульти-блок должен встать");

            RebuildDoors(s);
            return s;
        }

        private ClientConnection PlayerNextTo(int planX = DoorX, int planZ = DoorZ - 1, int height = DoorY)
        {
            var client = new ClientConnection(_peer, 1)
            {
                PlayerNetId = 1,
                X = planX + 0.5f,
                Y = planZ + 0.5f,
                Z = height,
                Mover = new BlockMoverState(planX + 0.5f, height, planZ + 0.5f)
            };
            return client;
        }

        private static InteractIntent TileIntent(int planX, int planZ, int height, InteractVerb verb = InteractVerb.Primary)
            => new InteractIntent
            {
                TargetKind = (byte)InteractTargetKind.Tile,
                Verb = (byte)verb,
                HandIndex = 0,
                TileX = planX,
                TileY = planZ,
                TileZ = height,
                TargetNetId = -1
            };

        private static bool IsOpen(GameServer s, int x, int y, int z)
            => BlockState.GetOpen(s.BlockWorld!.GetState(x, y, z));

        [Fact]
        public void InteractDoor_TogglesOpenThenClosed()
        {
            var s = WithDoor(TestDoorCatalog.InteractDoor);
            var client = PlayerNextTo();
            Assert.False(IsOpen(s, DoorX, DoorY, DoorZ));

            s.ResolveAndDispatchInteraction(client, TileIntent(DoorX, DoorZ, DoorY));
            Assert.True(IsOpen(s, DoorX, DoorY, DoorZ), "первая интеракция должна открыть дверь");

            s.ResolveAndDispatchInteraction(client, TileIntent(DoorX, DoorZ, DoorY));
            Assert.False(IsOpen(s, DoorX, DoorY, DoorZ), "повторная интеракция должна закрыть дверь");
        }

        [Fact]
        public void ClickOnNonAnchorPart_ResolvesAnchor_OpensWholeDoor()
        {
            var s = WithDoor(TestDoorCatalog.InteractDoorTall);
            var client = PlayerNextTo();

            s.ResolveAndDispatchInteraction(client, TileIntent(DoorX, DoorZ, DoorY + 1));

            Assert.True(IsOpen(s, DoorX, DoorY, DoorZ), "якорная часть должна открыться");
            Assert.True(IsOpen(s, DoorX, DoorY + 1, DoorZ), "верхняя часть должна открыться");
        }

        [Fact]
        public void ClickOnPlanPart_OfRotatedWideDoor_ResolvesAnchor()
        {
            const int Facing = 1;
            var s = WithDoor(TestDoorCatalog.InteractDoorWide, Facing);
            var info = BlockCatalog.Get(TestDoorCatalog.InteractDoorWide);
            MultiBlock.PartWorldOffset(1, info.SizeX, info.SizeZ, Facing, out int dx, out int dy, out int dz);
            Assert.True(dx != 0 || dz != 0, "часть должна смещаться по ПЛАНУ, иначе тест не проверяет facing");

            int px = DoorX + dx, py = DoorY + dy, pz = DoorZ + dz;
            var client = PlayerNextTo(planX: px, planZ: pz - 1, height: py);

            s.ResolveAndDispatchInteraction(client, TileIntent(px, pz, py));

            Assert.True(IsOpen(s, DoorX, DoorY, DoorZ), "якорь должен открыться по клику в план-часть");
            Assert.True(IsOpen(s, px, py, pz), "сама план-часть должна открыться");
        }

        [Fact]
        public void AutoDoor_IsNotToggled_ByInteraction()
        {
            var s = WithDoor(TestDoorCatalog.AutoDoor);
            var client = PlayerNextTo();

            bool handled = new DoorHandler(s).TryHandle(
                new InteractContext(client, DoorX, DoorZ, DoorY, (byte)InteractVerb.Primary, 0));

            Assert.False(handled, "авто-дверь не должна поглощаться обработчиком");
            Assert.False(IsOpen(s, DoorX, DoorY, DoorZ));
        }

        [Fact]
        public void OutOfReach_DoorUntouched()
        {
            var s = WithDoor(TestDoorCatalog.InteractDoor);
            var client = PlayerNextTo(planZ: DoorZ - 4);

            s.ResolveAndDispatchInteraction(client, TileIntent(DoorX, DoorZ, DoorY));

            Assert.False(IsOpen(s, DoorX, DoorY, DoorZ));
        }

        [Fact]
        public void InteractDoor_DoesNotOpen_OnProximity()
        {
            var s = WithDoor(TestDoorCatalog.InteractDoor);
            var client = PlayerNextTo();
            Register(s, client);

            DoorTicks(s, 60);

            Assert.False(IsOpen(s, DoorX, DoorY, DoorZ), "Interact-дверь не открывается от приближения");
        }

        [Fact]
        public void InteractDoor_AutoCloses_AfterDelay()
        {
            var s = WithDoor(TestDoorCatalog.InteractDoor);
            var client = PlayerNextTo();
            s.ResolveAndDispatchInteraction(client, TileIntent(DoorX, DoorZ, DoorY));
            Assert.True(IsOpen(s, DoorX, DoorY, DoorZ));

            DoorTicks(s, 1);
            Assert.True(IsOpen(s, DoorX, DoorY, DoorZ), "сразу после открытия дверь ещё открыта (идёт отсчёт)");

            DoorTicks(s, 30);
            Assert.False(IsOpen(s, DoorX, DoorY, DoorZ), "дверь должна закрыться сама по DoorCloseDelay");
        }

        [Fact]
        public void AutoDoor_StillOpens_OnProximity()
        {
            var s = WithDoor(TestDoorCatalog.AutoDoor);
            var client = PlayerNextTo();
            Register(s, client);
            Assert.False(IsOpen(s, DoorX, DoorY, DoorZ));

            DoorTicks(s, 2);

            Assert.True(IsOpen(s, DoorX, DoorY, DoorZ), "авто-дверь обязана открыться от приближения (регресс)");
        }

        [Fact]
        public void NonDoorAndEmptyTargets_AreNotConsumed()
        {
            var s = Server();
            var client = PlayerNextTo();
            var handler = new DoorHandler(s);

            s.BlockWorld!.SetBlock(DoorX, DoorY, DoorZ, 0);
            Assert.False(handler.TryHandle(
                new InteractContext(client, DoorX, DoorZ, DoorY, (byte)InteractVerb.Primary, 0)));

            s.BlockWorld.SetBlock(DoorX, DoorY, DoorZ, 3);
            Assert.False(handler.TryHandle(
                new InteractContext(client, DoorX, DoorZ, DoorY, (byte)InteractVerb.Primary, 0)));

            s.ResolveAndDispatchInteraction(client, TileIntent(DoorX, DoorZ, DoorY));
        }

        [Fact]
        public void AltVerb_IsNotConsumed()
        {
            var s = WithDoor(TestDoorCatalog.InteractDoor);
            var client = PlayerNextTo();

            bool handled = new DoorHandler(s).TryHandle(
                new InteractContext(client, DoorX, DoorZ, DoorY, (byte)InteractVerb.Alt, 0));

            Assert.False(handled);
            Assert.False(IsOpen(s, DoorX, DoorY, DoorZ));
        }

        [Fact]
        public void Registry_Default_ContainsDoorHandler()
        {
            var handlers = InteractionRegistry.Default(Server());
            Assert.Equal(2, handlers.Length);
            Assert.IsType<LiftCallHandler>(handlers[0]);
            Assert.IsType<DoorHandler>(handlers[1]);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void Click_CannotCloseDoorOnOccupant_ButStillConsumesTheClick(int facing)
        {
            var s = WithDoor(TestDoorCatalog.InteractDoorWide, facing);
            var opener = PlayerNextTo();
            var handler = new DoorHandler(s);

            Assert.True(handler.TryHandle(
                new InteractContext(opener, DoorX, DoorZ, DoorY, (byte)InteractVerb.Primary, 0)));
            Assert.True(IsOpen(s, DoorX, DoorY, DoorZ), "открытие кликом обязано проходить всегда");

            var occupant = new ClientConnection(_peer, 1)
            {
                PlayerNetId = 1,
                Mover = new BlockMoverState(DoorX + 0.5f, DoorY, DoorZ + 0.5f)
            };
            Register(s, occupant);

            Assert.True(handler.TryHandle(
                new InteractContext(occupant, DoorX, DoorZ, DoorY, (byte)InteractVerb.Primary, 0)),
                "отказ по занятости обязан ПОГЛОТИТЬ клик, а не молча провалиться дальше по реестру");
            Assert.True(IsOpen(s, DoorX, DoorY, DoorZ), "кликом больше нельзя прищемить человека");
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void Click_ClosesDoor_WhenDoorwayIsClear(int facing)
        {
            var s = WithDoor(TestDoorCatalog.InteractDoorWide, facing);
            var client = PlayerNextTo();
            var handler = new DoorHandler(s);

            Assert.True(handler.TryHandle(
                new InteractContext(client, DoorX, DoorZ, DoorY, (byte)InteractVerb.Primary, 0)));
            Assert.True(IsOpen(s, DoorX, DoorY, DoorZ));

            Assert.True(handler.TryHandle(
                new InteractContext(client, DoorX, DoorZ, DoorY, (byte)InteractVerb.Primary, 0)));
            Assert.False(IsOpen(s, DoorX, DoorY, DoorZ), "пустой проём — клик закрывает как раньше");
        }
    }
}
