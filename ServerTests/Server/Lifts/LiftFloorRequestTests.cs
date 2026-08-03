using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using LiteNetLib;
using Server.Doors;
using Server.Lifts;
using Server.Network;
using Shared.Configs;
using Shared.Messages.Lifts;
using Shared.Simulation.Blocks;
using Shared.World.Atmos;
using Shared.World.Blocks;

namespace ServerTests.Server.Lifts
{
    /// <summary>L4-S: приём выбора этажа (авторитет по LiftRide) и точки взвода _dirty → ровно один LiftSync.</summary>
    public class LiftFloorRequestTests
    {
        private sealed class Doors : IDoorCommands
        {
            public readonly HashSet<long> Open = new();
            public bool TryGetAnchorKey(int x, int y, int z, out long key) { key = 0L; return false; }
            public bool Exists(long key) => true;
            public bool IsOpen(long key) => Open.Contains(key);
            public DoorCommandResult TrySetOpen(long key, bool open, bool force = false)
            {
                if (open) return Open.Add(key) ? DoorCommandResult.Applied : DoorCommandResult.AlreadyInState;
                return Open.Remove(key) ? DoorCommandResult.Applied : DoorCommandResult.AlreadyInState;
            }
        }

        // ⚠ Ни одна величина не совпадает с другой: якорь, смещение бокса, полуширины и высоты попарно различны.
        // Квадратный бокс в центре якоря (как было) не ловит НИ подмену halfX/halfZ, НИ подмену CenterX/CenterZ,
        // НИ потерю box.Y в формуле верха — это четвёртый случай слепой фикстуры за трек.
        private const float AnchorX = 5f, AnchorZ = 20f;
        private const float BoxCX = 6f, BoxCZ = 0.5f;      // мировой центр кабины: (11, 20.5)
        private const float BoxHX = 1.5f, BoxHZ = 0.5f;    // кабина 3×1 в плане
        private const float BoxY = 0.75f, BoxH = 0.5f;     // верх = segY + 0.75 + 0.5
        private const float WorldCX = AnchorX + BoxCX, WorldCZ = AnchorZ + BoxCZ;
        private const float SegY = 1f;
        private const float CabinTop = SegY + BoxY + BoxH;

        private static LiftController Make(Doors doors, uint dwell = 4u, uint lead = 0u)
        {
            var runtime = new LiftRuntime(AnchorX, AnchorZ,
                new[] { new LiftBox(BoxCX, BoxY, BoxCZ, BoxHX, BoxHZ, BoxH) },
                new LiftSegment(SegY, SegY, 0u, 0.1f), liftId: 7);
            return new LiftController(7, runtime, new[] { SegY, 4f }, new[] { 100L, 200L },
                new[] { true, true }, dwell, lead, 1u, 0.1f, doors);
        }

        private static ClientConnection Rider(float y = CabinTop, float x = WorldCX, float z = WorldCZ)
            => new ClientConnection(null!, 1) { PlayerNetId = 1, Mover = new BlockMoverState(x, y, z) };

        [Fact]
        public void RiderIsRecognised_StoppedCabin()
            => Assert.True(LiftFloorSystem.IsRiding(Rider(), Make(new Doors()), 0u));

        [Fact]
        public void PlayerOutsideCabin_IsRejected()
            => Assert.False(LiftFloorSystem.IsRiding(Rider(x: WorldCX + 5f), Make(new Doors()), 0u));

        [Fact]
        public void CabinTopFormula_UsesBoxYAndHeight()
        {
            // Закрепляет y + box.Y + box.Height: ноги ровно на верху — пассажир; на верху БЕЗ box.Y — уже нет
            // (разрыв 0.75 больше AutoStep 0.5). Потеря слагаемого или замена Height здесь падает.
            Assert.True(LiftFloorSystem.IsRiding(Rider(y: CabinTop), Make(new Doors()), 0u));
            Assert.False(LiftFloorSystem.IsRiding(Rider(y: SegY + BoxH - 0.01f), Make(new Doors()), 0u),
                "верх кабины обязан считаться как segY + box.Y + box.Height");
        }

        [Fact]
        public void IsRiding_DistinguishesPlanAxes()
        {
            // Кабина 3×1: снаружи по короткой оси Z, внутри по длинной X. При свопе половин/центров станет true.
            float outsideZ = WorldCZ + BoxHZ + 0.4f + 0.05f;
            Assert.False(LiftFloorSystem.IsRiding(Rider(z: outsideZ), Make(new Doors()), 0u));
            Assert.True(LiftFloorSystem.IsRiding(Rider(x: WorldCX + 1.0f), Make(new Doors()), 0u),
                "внутри у длинной стены — пассажир; со свопом половин станет false");
        }

        [Fact]
        public void IsRiding_FollowsCabin_WhileMoving()
        {
            var doors = new Doors();
            var c = Make(doors);
            Assert.True(c.Request(1));
            for (uint t = 0; t < 8; t++)
                c.Decide(t);

            uint now = 8u;
            float y = c.Runtime.YAt(now);
            Assert.True(y > SegY, "предусловие: кабина уже поехала");

            var rider = Rider(y: y + BoxY + BoxH);
            Assert.True(LiftFloorSystem.IsRiding(rider, c, now),
                "на ходу верх кабины берётся по ТЕКУЩЕМУ тику, а не по StartTick сегмента");
        }

        [Fact]
        public void UnspawnedClient_IsRejected()
        {
            var client = Rider();
            client.PlayerNetId = 0;
            Assert.False(LiftFloorSystem.IsRiding(client, Make(new Doors()), 0u));
        }

        [Fact]
        public void Request_FromRider_ArmsDirty_AndOneSync()
        {
            var c = Make(new Doors());
            Assert.True(c.Request(1));
            Assert.True(c.Decide(0u), "заказ этажа обязан дать пакет: Calls не выводится ничем");
            Assert.False(c.Decide(1u), "второй пакет без новых событий — флуд");
        }

        [Fact]
        public void RepeatedRequest_SameFloor_DoesNotResync()
        {
            var c = Make(new Doors());
            Assert.True(c.Request(1));
            Assert.True(c.Decide(0u));
            Assert.True(c.Request(1));
            Assert.False(c.Decide(1u), "повторное нажатие того же этажа ничего не меняет — пакета быть не должно");
        }

        [Fact]
        public void Departure_And_DoorOpen_EachGiveExactlyOneSync()
        {
            var doors = new Doors();
            var c = Make(doors);
            Assert.True(c.Request(1));

            int syncs = 0;
            for (uint t = 0; t < 200; t++)
                if (c.Decide(t))
                    syncs++;

            // заказ + отправление + снятие Calls на открытии двери
            Assert.Equal(3, syncs);
            Assert.Equal(1, c.CurrentFloor);
            Assert.Equal(0u, c.Calls);
        }

        [Fact]
        public void RequestOnCurrentFloor_ProlongsDwell_AndSyncs()
        {
            var doors = new Doors();
            var c = Make(doors);
            for (uint t = 0; t < 50; t++)
                c.Decide(t);
            Assert.Equal(LiftPhaseKind.Ready, c.PhaseAt(50u));

            Assert.True(c.Request(0));
            uint dwellBefore = c.Plan.DwellUntilTick;
            Assert.True(c.Decide(50u), "нажатие этажа, где кабина уже стоит, обязано дать пакет");
            Assert.True(c.Plan.DwellUntilTick > dwellBefore, "стоянка обязана продлиться");
        }

        [Fact]
        public void FloorWithoutDoor_RequestRejected_NoSync()
        {
            var doors = new Doors();
            var runtime = new LiftRuntime(AnchorX, AnchorZ,
                new[] { new LiftBox(0f, 0f, 0f, 1f, 1f, 0.25f) },
                new LiftSegment(1f, 1f, 0u, 0.1f), liftId: 7);
            var c = new LiftController(7, runtime, new[] { 1f, 4f }, new[] { 100L, 0L },
                new[] { true, false }, 4u, 0u, 1u, 0.1f, doors);

            Assert.False(c.Request(1));
            Assert.False(c.Decide(0u));
        }

        private sealed class PeerRefComparer : IEqualityComparer<NetPeer>
        {
            public static readonly PeerRefComparer Instance = new();
            public bool Equals(NetPeer? a, NetPeer? b) => ReferenceEquals(a, b);
            public int GetHashCode(NetPeer p) => RuntimeHelpers.GetHashCode(p);
        }

        private static NetPeer FakePeer() => (NetPeer)RuntimeHelpers.GetUninitializedObject(typeof(NetPeer));

        private sealed class Rig
        {
            public LiftSystem Lifts = null!;
            public LiftFloorSystem Floors = null!;
            public LiftController Controller = null!;
            public ClientConnection Client = null!;
        }

        // БОЕВАЯ цепь: настоящий скан → LiftSystem → LiftFloorSystem. Кабина берётся из TestLiftCatalog.CabinWide
        // (модуль 4×2 в плане ⇒ HalfX != HalfZ) — та же асимметрия, что и в LiftRideTests.
        private static Rig Build(int netId = 1, float dx = 0f, float dz = 0f, float dy = 0f)
        {
            const int RX = 10, RZ = 10;
            var grid = new BlockGrid();
            for (int k = 0; k < 2; k++)
            {
                grid.SetBlock(RX, k * TestLiftCatalog.WideStep, RZ, TestLiftCatalog.RailWide);
                grid.SetState(RX, k * TestLiftCatalog.WideStep, RZ, BlockState.WithFacing(0, 0));
            }
            grid.SetBlock(RX + 1, 0, RZ, TestLiftCatalog.CabinWide);
            grid.SetBlock(RX + 1, 0, RZ - 1, TestLiftCatalog.ShaftDoor);
            grid.SetBlock(RX + 1, TestLiftCatalog.WideStep, RZ - 1, TestLiftCatalog.ShaftDoor);

            var clients = new Dictionary<NetPeer, ClientConnection>(PeerRefComparer.Instance);
            var doors = new DoorSystem(grid, new AtmosGrid(), new SVars { TickRate = 30 }, clients);
            doors.Build();
            var lifts = new LiftSystem(grid, doors, new SVars { TickRate = 30 });
            lifts.Build();

            var controller = Assert.Single(lifts.Controllers);
            var box = controller.Runtime.CabinBoxes[0];
            float top = controller.Runtime.YAt(0u) + box.Y + box.Height;

            var client = new ClientConnection(FakePeer(), 1)
            {
                PlayerNetId = netId,
                Mover = new BlockMoverState(
                    controller.Runtime.AnchorX + box.CenterX + dx,
                    top + dy,
                    controller.Runtime.AnchorZ + box.CenterZ + dz)
            };
            clients[client.Peer] = client;

            return new Rig
            {
                Lifts = lifts,
                Floors = new LiftFloorSystem(lifts, clients),
                Controller = controller,
                Client = client
            };
        }

        [Fact]
        public void Process_RiderRequest_ReachesController()
        {
            var rig = Build();
            rig.Client.LiftFloorQueue.Enqueue(new LiftFloorRequest { LiftId = rig.Controller.LiftId, Floor = 1 });

            rig.Floors.Process(0u);

            Assert.Equal(1, rig.Floors.Applied);
            Assert.Equal(0, rig.Floors.RejectedNotRiding);
            Assert.True(rig.Controller.HasCall(1));
        }

        [Fact]
        public void Process_NonRider_IsRejectedSilently()
        {
            var rig = Build(dx: 40f);
            rig.Client.LiftFloorQueue.Enqueue(new LiftFloorRequest { LiftId = rig.Controller.LiftId, Floor = 1 });

            var ex = Record.Exception(() => rig.Floors.Process(0u));

            Assert.Null(ex);
            Assert.Equal(0, rig.Floors.Applied);
            Assert.Equal(1, rig.Floors.RejectedNotRiding);
            Assert.False(rig.Controller.HasCall(1));
        }

        [Fact]
        public void Process_UnknownLiftId_IsIgnored()
        {
            var rig = Build();
            rig.Client.LiftFloorQueue.Enqueue(new LiftFloorRequest { LiftId = 4242, Floor = 1 });

            rig.Floors.Process(0u);

            Assert.Equal(0, rig.Floors.Applied);
            Assert.Equal(0, rig.Floors.RejectedNotRiding);
            Assert.False(rig.Controller.HasCall(1));
        }

        [Fact]
        public void Process_FloorOutOfRange_IsIgnored()
        {
            var rig = Build();
            foreach (byte floor in new byte[] { 255, 200, 9 })
                rig.Client.LiftFloorQueue.Enqueue(new LiftFloorRequest { LiftId = rig.Controller.LiftId, Floor = floor });

            var ex = Record.Exception(() => rig.Floors.Process(0u));

            Assert.Null(ex);
            Assert.Equal(0, rig.Floors.Applied);
            Assert.Equal(0u, rig.Controller.Calls);
        }

        [Fact]
        public void Process_UnspawnedClient_IsRejected()
        {
            var rig = Build(netId: 0);
            rig.Client.LiftFloorQueue.Enqueue(new LiftFloorRequest { LiftId = rig.Controller.LiftId, Floor = 1 });

            rig.Floors.Process(0u);

            Assert.Equal(0, rig.Floors.Applied);
            Assert.Equal(1, rig.Floors.RejectedNotRiding);
        }

        [Fact]
        public void Process_RiderWhoSteppedOut_IsRejected_NotAnError()
        {
            var rig = Build();
            rig.Client.LiftFloorQueue.Enqueue(new LiftFloorRequest { LiftId = rig.Controller.LiftId, Floor = 1 });
            rig.Client.Mover = new BlockMoverState(rig.Client.Mover.X + 40f, rig.Client.Mover.Y, rig.Client.Mover.Z);

            rig.Floors.Process(0u);

            Assert.Equal(0, rig.Floors.Applied);
            Assert.Equal(1, rig.Floors.RejectedNotRiding);
        }

        [Fact]
        public void Process_Flood_DoesNotGrowQueue_AndServerSurvives()
        {
            var rig = Build();
            for (int i = 0; i < 10_000; i++)
                rig.Client.LiftFloorQueue.Enqueue(new LiftFloorRequest { LiftId = rig.Controller.LiftId, Floor = 1 });

            var ex = Record.Exception(() => rig.Floors.Process(0u));

            Assert.Null(ex);
            Assert.True(rig.Client.LiftFloorQueue.IsEmpty,
                "очередь обязана сливаться целиком: предохранитель ограничивает ОБРАБОТКУ, а не НАКОПЛЕНИЕ");
            Assert.Equal(10_000 - LiftFloorSystem.MaxRequestsPerTick, rig.Floors.Dropped);
        }

        [Fact]
        public void Disconnect_ClearsQueue()
        {
            var rig = Build();
            for (int i = 0; i < 5; i++)
                rig.Client.LiftFloorQueue.Enqueue(new LiftFloorRequest { LiftId = rig.Controller.LiftId, Floor = 1 });

            LiftFloorSystem.OnClientDisconnect(rig.Client);

            Assert.True(rig.Client.LiftFloorQueue.IsEmpty);
        }

        [Theory]
        [InlineData(0u)]
        [InlineData(1u)]
        public void DegenerateDwell_ClearsCall_AndPacketsAreFinite(uint dwell)
        {
            var doors = new Doors();
            var c = Make(doors, dwell: dwell);
            Assert.True(c.Request(1));

            int syncs = 0;
            for (uint t = 0; t < 2000; t++)
                if (c.Decide(t))
                    syncs++;

            Assert.Equal(0u, c.Calls);
            Assert.Equal(1, c.CurrentFloor);
            Assert.True(syncs < 20,
                $"dwell={dwell}: {syncs} пакетов — окно Dwell пусто, заказ не гаснет и Ready продлевает стоянку " +
                "каждый тик, взводя _dirty: кабина висит вечно и штормит LiftSync по ReliableOrdered");
        }

        [Theory]
        [InlineData(0u)]
        [InlineData(1u)]
        public void DegenerateDwell_WithLongDoorLead_StillTerminates(uint dwell)
        {
            var doors = new Doors();
            var c = Make(doors, dwell: dwell, lead: 50u);
            Assert.True(c.Request(1));

            int syncs = 0;
            for (uint t = 0; t < 4000; t++)
                if (c.Decide(t))
                    syncs++;

            Assert.Equal(0u, c.Calls);
            Assert.True(syncs < 20, $"dwell={dwell}, lead=50: {syncs} пакетов");
        }

        [Fact]
        public void RoundTrip_LiftFloorRequest_AsymmetricFields()
        {
            var src = new LiftFloorRequest { LiftId = 0x0A0B0C0D, Floor = 23 };
            var dst = new LiftFloorRequest();
            dst.Deserialize(src.Serialize());

            Assert.Equal(src.LiftId, dst.LiftId);
            Assert.Equal(src.Floor, dst.Floor);
            Assert.Equal(5, src.Serialize().Length);
        }

        [Fact]
        public void LiftFloorRequest_RejectsBadPayload()
        {
            Assert.Throws<ArgumentNullException>(() => new LiftFloorRequest().Deserialize(null!));
            Assert.Throws<ArgumentException>(() => new LiftFloorRequest().Deserialize(new byte[4]));
            Assert.Throws<ArgumentException>(() => new LiftFloorRequest().Deserialize(new byte[6]));
        }

        [Fact]
        public void RoundTrip_LiftSync_CarriesDwellAndCalls()
        {
            var src = new LiftSync
            {
                LiftId = 9, FromY = 1.5f, ToY = -7.25f, StartTick = 123u,
                BlocksPerTick = 0.0625f, DwellUntilTick = 4567u, Calls = 0xA5A50001u
            };
            var dst = new LiftSync();
            dst.Deserialize(src.Serialize());

            Assert.Equal(src.LiftId, dst.LiftId);
            Assert.Equal(src.FromY, dst.FromY);
            Assert.Equal(src.ToY, dst.ToY);
            Assert.Equal(src.StartTick, dst.StartTick);
            Assert.Equal(src.BlocksPerTick, dst.BlocksPerTick);
            Assert.Equal(src.DwellUntilTick, dst.DwellUntilTick);
            Assert.Equal(src.Calls, dst.Calls);
        }
    }
}
