using System.Collections.Generic;
using System.Runtime.CompilerServices;
using LiteNetLib;
using Server.Doors;
using Server.Lifts;
using Server.Network;
using Shared.Configs;
using Shared.Simulation.Blocks;
using Shared.World.Atmos;
using Shared.World.Blocks;

namespace ServerTests.Server.Lifts
{
    /// <summary>Дедуп LiftSync на ДЛИННОЙ поездке: GameServer шлёт по пакету на каждый элемент
    /// LiftSystem.Changed, поэтому «ровно один пакет на смену состояния» проверяется здесь без сокета.</summary>
    public class LiftSyncDedupTests
    {
        private sealed class PeerRefComparer : IEqualityComparer<NetPeer>
        {
            public static readonly PeerRefComparer Instance = new();
            public bool Equals(NetPeer? a, NetPeer? b) => ReferenceEquals(a, b);
            public int GetHashCode(NetPeer p) => RuntimeHelpers.GetHashCode(p);
        }

        private static LiftSystem SyntheticShaft(int pauseTicks)
        {
            var config = new SVars
            {
                TickRate = 30, DebugLiftEnabled = true,
                DebugLiftX = 5.5f, DebugLiftZ = 5.5f, DebugLiftFromY = 1f, DebugLiftToY = 2f,
                DebugLiftSpeed = 0.5f, DebugLiftHalfW = 1f, DebugLiftHeight = 0.25f,
                DebugLiftPauseTicks = pauseTicks
            };
            var grid = new BlockGrid();
            var clients = new Dictionary<NetPeer, ClientConnection>(PeerRefComparer.Instance);
            var doors = new DoorSystem(grid, new AtmosGrid(), config, clients);
            doors.Build();
            var lifts = new LiftSystem(grid, doors, config);
            lifts.Build();
            return lifts;
        }

        // Зеркало GameServer.LiftSyncOf: те же поля, что реально уходят в LiftSync.
        private static (float, float, uint, uint, uint) SyncOf(LiftController c)
            => (c.Runtime.Segment.FromY, c.Runtime.Segment.ToY, c.Runtime.Segment.StartTick,
                c.Plan.DwellUntilTick, c.Calls);

        [Fact]
        public void PanelRide_EmitsEveryStateChangeExactlyOnce_BothDirections()
        {
            var lifts = SyntheticShaft(pauseTicks: 10);
            var controller = Assert.Single(lifts.Controllers);
            var packets = new List<(float, float, uint, uint, uint)>();

            int next = 1;
            for (uint t = 0; t < 400u; t++)
            {
                if (controller.PhaseAt(t) == LiftPhaseKind.Ready && controller.Calls == 0u)
                {
                    Assert.True(controller.Request(next), $"этаж {next} обязан быть обслуживаемым");
                    next = next == 1 ? 0 : 1;
                }
                lifts.Tick(t);
                for (int i = 0; i < lifts.Changed.Count; i++)
                    packets.Add(SyncOf(lifts.Changed[i]));
            }

            Assert.True(packets.Count >= 6,
                $"поездка по заказам с панели не состоялась: пакетов {packets.Count}");

            var seen = new HashSet<(float, float, uint, uint, uint)>();
            foreach (var p in packets)
                Assert.True(seen.Add(p), $"дубликат пакета: сегмент {p.Item1}->{p.Item2}@{p.Item3}, " +
                                         $"dwell={p.Item4}, calls={p.Item5}");

            bool up = false, down = false;
            foreach (var p in packets)
            {
                if (p.Item2 > p.Item1) up = true;
                if (p.Item2 < p.Item1) down = true;
            }
            Assert.True(up && down, "поездка обязана дать сегменты в обе стороны");
        }

        [Fact]
        public void ParkedLift_WithoutRequests_EmitsNothing()
        {
            var lifts = SyntheticShaft(pauseTicks: 10);
            Assert.Single(lifts.Controllers);

            int packets = 0;
            for (uint t = 0; t < 400u; t++)
            {
                lifts.Tick(t);
                packets += lifts.Changed.Count;
            }

            Assert.Equal(0, packets);
        }
    }
}
