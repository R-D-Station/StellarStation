using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using LiteNetLib;
using Server.Network;
using Shared.Configs;
using Shared.Messages.Interaction;
using Shared.Simulation.Blocks;
using Shared.World.Atmos;
using Shared.World.Blocks;

namespace ServerTests.Server.Airlocks
{
    internal static class TestAirlockCatalog
    {
        internal const ushort AirlockAuto = 910;
        internal const ushort PlainAuto = 911;
        internal const ushort AirlockInteract = 912;
        internal const ushort NarrowTriggerAuto = 913;

        private static readonly TriggerBox[] Triggers =
        {
            new TriggerBox(0, 0, -20, 16, 32, 4),
            new TriggerBox(0, 0, 12, 16, 32, 36)
        };

        private static readonly TriggerBox[] NarrowTriggers =
        {
            new TriggerBox(0, 0, -20, 16, 32, -4)
        };

        [ModuleInitializer]
        internal static void Seed()
        {
            var field = typeof(BlockCatalog).GetField("_byId", BindingFlags.NonPublic | BindingFlags.Static)!;
            var byId = (Dictionary<ushort, BlockInfo>)field.GetValue(null)!;

            byId[AirlockAuto] = Door(AirlockAuto, "TestAirlockAuto", DoorOpening.Auto, true);
            byId[PlainAuto] = Door(PlainAuto, "TestPlainAuto", DoorOpening.Auto, false);
            byId[AirlockInteract] = Door(AirlockInteract, "TestAirlockInteract", DoorOpening.Interact, true);
            byId[NarrowTriggerAuto] = Door(NarrowTriggerAuto, "TestNarrowTriggerAuto", DoorOpening.Auto, false,
                NarrowTriggers);
        }

        private static BlockInfo Door(ushort id, string name, DoorOpening opening, bool airlock,
            TriggerBox[]? triggers = null)
            => new BlockInfo(id, name, BlockCategory.Door,
                BlockFaceFlags.All, BlockFaceFlags.All, 0,
                new[] { new[] { BlockBox.Full } }, 1, 1, 1,
                new[] { Array.Empty<BlockBox>() }, opening, triggers ?? Triggers, 0.1f, false,
                Array.Empty<AttachSurface>(), airlock);
    }

    /// <summary>Шлюзы: атмос-гейт авто-открытия по перепаду давления + анти-прищем на закрытии.</summary>
    public class AirlockTests : IDisposable
    {
        private const int DoorX = 5, DoorY = 1, DoorZ = 6;
        private const int SideA = DoorZ - 1, SideB = DoorZ + 1;

        private readonly SVars _config;
        private readonly NetManager _dummy;
        private readonly NetPeer _peer;
        private GameServer? _server;

        public AirlockTests()
        {
            _config = new SVars
            {
                Ip = "127.0.0.1",
                Port = 0,
                MaxPlayers = 10,
                TickRate = 30,
                MapPath = "",
                ConnectionKey = "AirlockTest",
                AirlockMaxDeltaKpa = 20f
            };

            _dummy = new NetManager(new EventBasedNetListener());
            _dummy.Start();
            _peer = _dummy.Connect("127.0.0.1", 1, _config.ConnectionKey);
            _dummy.Stop();
        }

        public void Dispose() => _server?.Stop();

        private GameServer Server() => _server ??= new GameServer(_config);

        private static readonly MethodInfo RebuildMethod =
            typeof(GameServer).GetMethod("BuildAutoDoorRegistry", BindingFlags.NonPublic | BindingFlags.Instance)!;
        private static readonly MethodInfo DoorTickMethod =
            typeof(GameServer).GetMethod("ProcessBlockDoors", BindingFlags.NonPublic | BindingFlags.Instance)!;
        private static readonly FieldInfo TickField =
            typeof(GameServer).GetField("_currentTick", BindingFlags.NonPublic | BindingFlags.Instance)!;
        private static readonly FieldInfo ClientsField =
            typeof(GameServer).GetField("_clients", BindingFlags.NonPublic | BindingFlags.Instance)!;

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

        private static GasMix AtKpa(float kpa)
        {
            float moles = kpa / AtmosConstants.KpaPerMole;
            return new GasMix(moles * AtmosConstants.OxygenFraction, moles * AtmosConstants.NitrogenFraction);
        }

        private GameServer WithAirlock(ushort type, float kpaA, float kpaB)
        {
            var s = Server();
            var grid = s.BlockWorld!;

            grid.SetBlock(DoorX, DoorY, SideA, 0);
            grid.SetBlock(DoorX, DoorY, SideB, 0);
            grid.SetBlock(DoorX, DoorY, DoorZ, type);
            grid.SetState(DoorX, DoorY, DoorZ, BlockState.WithOpen(0, false));
            RebuildMethod.Invoke(s, null);

            s.Atmos.SetMix(DoorX, DoorY, SideA, AtKpa(kpaA));
            s.Atmos.SetMix(DoorX, DoorY, SideB, AtKpa(kpaB));
            return s;
        }

        private ClientConnection PlayerInTrigger()
            => Positioned(DoorX + 0.5f, DoorY, SideA + 0.5f);

        private ClientConnection PlayerInDoorway()
            => Positioned(DoorX + 0.5f, DoorY, DoorZ + 0.5f);

        private ClientConnection Positioned(float x, float height, float z)
            => new ClientConnection(_peer, 1)
            {
                PlayerNetId = 1,
                X = x,
                Y = z,
                Z = (int)MathF.Floor(height),
                Mover = new BlockMoverState(x, height, z)
            };

        private static bool IsOpen(GameServer s)
            => BlockState.GetOpen(s.BlockWorld!.GetState(DoorX, DoorY, DoorZ));

        [Fact]
        public void Airlock_Auto_DoesNotOpen_UnderPressureDelta()
        {
            var s = WithAirlock(TestAirlockCatalog.AirlockAuto, AtmosConstants.StandardPressureKpa, 0f);
            Register(s, PlayerInTrigger());

            DoorTicks(s, 30);

            Assert.False(IsOpen(s), "шлюз не должен открываться сам при перепаде давления");
        }

        [Fact]
        public void PlainDoor_Auto_StillOpens_UnderPressureDelta()
        {
            var s = WithAirlock(TestAirlockCatalog.PlainAuto, AtmosConstants.StandardPressureKpa, 0f);
            Register(s, PlayerInTrigger());

            DoorTicks(s, 2);

            Assert.True(IsOpen(s), "обычная дверь ведёт себя как раньше (регресс)");
        }

        [Fact]
        public void Airlock_Auto_Opens_WhenPressureEqual()
        {
            var s = WithAirlock(TestAirlockCatalog.AirlockAuto,
                AtmosConstants.StandardPressureKpa, AtmosConstants.StandardPressureKpa);
            Register(s, PlayerInTrigger());

            DoorTicks(s, 2);

            Assert.True(IsOpen(s), "при равном давлении шлюз открывается нормально");
        }

        [Fact]
        public void Airlock_JustBelowThreshold_Opens()
        {
            var s = WithAirlock(TestAirlockCatalog.AirlockAuto,
                AtmosConstants.StandardPressureKpa, AtmosConstants.StandardPressureKpa - 19f);
            Register(s, PlayerInTrigger());

            DoorTicks(s, 2);

            Assert.True(IsOpen(s), "перепад 19 кПа < порога 20 — открывается");
        }

        [Fact]
        public void Airlock_JustAboveThreshold_DoesNotOpen()
        {
            var s = WithAirlock(TestAirlockCatalog.AirlockAuto,
                AtmosConstants.StandardPressureKpa, AtmosConstants.StandardPressureKpa - 21f);
            Register(s, PlayerInTrigger());

            DoorTicks(s, 30);

            Assert.False(IsOpen(s), "перепад 21 кПа > порога 20 — не открывается");
        }

        [Fact]
        public void Airlock_OpenDoor_StillCloses_WhenDeltaAppears()
        {
            var s = WithAirlock(TestAirlockCatalog.AirlockAuto,
                AtmosConstants.StandardPressureKpa, AtmosConstants.StandardPressureKpa);
            var client = PlayerInTrigger();
            Register(s, client);
            DoorTicks(s, 2);
            Assert.True(IsOpen(s));

            s.Atmos.SetMix(DoorX, DoorY, SideB, GasMix.Vacuum);
            client.Mover = new BlockMoverState(DoorX + 0.5f, DoorY, SideA - 5.5f);

            DoorTicks(s, 30);

            Assert.False(IsOpen(s), "закрытию гейт мешать не должен — иначе шлюз застрянет открытым");
        }

        [Fact]
        public void Airlock_Interact_OpensDespiteDelta()
        {
            var s = WithAirlock(TestAirlockCatalog.AirlockInteract, AtmosConstants.StandardPressureKpa, 0f);
            var client = PlayerInTrigger();

            s.ResolveAndDispatchInteraction(client, new InteractIntent
            {
                TargetKind = (byte)InteractTargetKind.Tile,
                Verb = (byte)InteractVerb.Primary,
                HandIndex = 0,
                TileX = DoorX,
                TileY = DoorZ,
                TileZ = DoorY,
                TargetNetId = -1
            });

            Assert.True(IsOpen(s), "E — осознанное действие игрока, гейт его не трогает");
        }

        [Fact]
        public void AntiCrush_AppliesToAutoDoors_OutsideTrigger()
        {
            var s = WithAirlock(TestAirlockCatalog.NarrowTriggerAuto,
                AtmosConstants.StandardPressureKpa, AtmosConstants.StandardPressureKpa);
            var client = PlayerInTrigger();
            Register(s, client);
            DoorTicks(s, 2);
            Assert.True(IsOpen(s));

            client.Mover = new BlockMoverState(DoorX + 0.5f, DoorY, DoorZ + 0.5f);
            var info = BlockCatalog.Get(TestAirlockCatalog.NarrowTriggerAuto);
            Assert.False(AutoDoorLogic.PlayerInTrigger(
                    client.Mover.X, client.Mover.Y, client.Mover.Z,
                    BlockMovementConfig.HalfWidth, BlockMovementConfig.StandHeight,
                    DoorX, DoorY, DoorZ, info.Triggers, info.SizeX, info.SizeZ, 0),
                "в проёме игрок ВНЕ триггера — иначе дверь держит проксимити и тест ничего не проверяет");

            DoorTicks(s, 60);
            Assert.True(IsOpen(s), "дверь не должна закрываться на игроке в проёме");

            client.Mover = new BlockMoverState(DoorX + 0.5f, DoorY, SideA - 5.5f);
            DoorTicks(s, 60);
            Assert.False(IsOpen(s), "игрок ушёл — дверь закрывается");
        }

        [Fact]
        public void AntiCrush_AppliesToInteractDoors()
        {
            var s = WithAirlock(TestAirlockCatalog.AirlockInteract,
                AtmosConstants.StandardPressureKpa, AtmosConstants.StandardPressureKpa);
            var opener = PlayerInTrigger();
            s.ResolveAndDispatchInteraction(opener, new InteractIntent
            {
                TargetKind = (byte)InteractTargetKind.Tile,
                Verb = (byte)InteractVerb.Primary,
                HandIndex = 0,
                TileX = DoorX,
                TileY = DoorZ,
                TileZ = DoorY,
                TargetNetId = -1
            });
            Assert.True(IsOpen(s));

            var stuck = PlayerInDoorway();
            Register(s, stuck);
            DoorTicks(s, 60);
            Assert.True(IsOpen(s), "Interact-дверь (проксимити выключен) тоже не должна прищемлять");

            stuck.Mover = new BlockMoverState(DoorX + 0.5f, DoorY, SideA - 5.5f);
            DoorTicks(s, 60);
            Assert.False(IsOpen(s));
        }
    }
}
