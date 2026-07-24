using Server.Network;
using Shared.Messages.Core;
using Shared.Simulation;

namespace ServerTests.Server.Network
{
    /// <summary>
    /// Волна 2A [MAJOR]: ProcessIntents дренирует ВСЮ очередь за тик (catch-up), а не дропает голову.
    /// ProcessIntents/_clients приватны → зеркалим дренаж-цикл + ApplyClientIntent-пайплайн на РЕАЛЬНОМ
    /// ClientConnection.IntentQueue (как Step-нить в FsmStage3-6Tests). Проверяем: все применены, ack=последний,
    /// позиция=сумма; пустая очередь=None-тик; потолок флуда дропает хвост (его seq не ack'аются → реплей у клиента).
    /// </summary>
    public class IntentDrainTests
    {
        private static void FreeStep(ref float x, ref float y, IntentDirection dir, bool sprint = false, bool crawl = false,
            float baseStep = MovementLogic.StepPerTick)
        {
            MovementLogic.GetAxes(dir, out int dx, out int dy);
            if (dx == 0 && dy == 0) return;
            float step = baseStep * (sprint ? MovementLogic.SprintMultiplier : 1f);
            if (dx != 0 && dy != 0) step *= MovementLogic.InvSqrt2;
            if (crawl) step *= MovementLogic.CrawlMultiplier;
            x += dx * step;
            y += dy * step;
        }

        private const int MaxPerTick = 32; // == GameServer.ProcessIntents

        // Зеркало GameServer.ApplyClientIntent (приватный) — тот же пайплайн.
        private static void ApplyClientIntent(ClientConnection c, MoveIntent intent, bool hasIntent)
        {
            var dir = hasIntent ? intent.Direction : IntentDirection.None;
            var prev = c.State;
            c.State = FsmLogic.Step(c.State, dir, hasIntent && intent.LayToggle, c.CurrentLayingReason, ref c.Timers);
            if (c.State == PlayerState.Laying && prev != PlayerState.Laying) c.CurrentLayingReason = LayingReason.Voluntary;
            if (!hasIntent) return;
            c.LastProcessedSequence = intent.Sequence;
            if (FsmLogic.MovementAllowed(c.State) && !c.DisableMovement)
            {
                float x = c.X, y = c.Y;
                FreeStep(ref x, ref y, intent.Direction, intent.Sprint, crawl: c.State == PlayerState.Laying);
                c.X = x; c.Y = y;
                c.Facing = MovementLogic.ToFacing(intent.Direction, c.Facing);
            }
        }

        // Зеркало GameServer.ProcessIntents для одного клиента. Возвращает (applied, dropped).
        private static (int applied, int dropped) Drain(ClientConnection c)
        {
            int applied = 0;
            while (applied < MaxPerTick && c.IntentQueue.TryDequeue(out var intent))
            {
                ApplyClientIntent(c, intent, true);
                applied++;
            }

            int dropped = 0;
            if (applied == 0)
                ApplyClientIntent(c, default, false); // None-тик
            else
                while (c.IntentQueue.TryDequeue(out _)) dropped++; // потолок: сброс хвоста
            return (applied, dropped);
        }

        [Fact]
        public void Drain_BurstOfIntents_AllApplied_AckLast_PositionIsSum()
        {
            var c = new ClientConnection(null!, 1) { X = 0.5f, Y = 0.5f };
            const int n = 5;
            for (uint i = 1; i <= n; i++)
                c.IntentQueue.Enqueue(new MoveIntent { Direction = IntentDirection.North, Sprint = false, Sequence = i });

            var (applied, dropped) = Drain(c);

            Assert.Equal(n, applied);
            Assert.Equal(0, dropped);
            Assert.True(c.IntentQueue.IsEmpty);
            Assert.Equal((uint)n, c.LastProcessedSequence);                 // ack = последний реально применённый
            Assert.Equal(PlayerState.Move, c.State);
            Assert.True(System.MathF.Abs(c.Y - (0.5f + n * MovementLogic.StepPerTick)) < 1e-3f, $"y={c.Y}");
        }

        [Fact]
        public void Drain_EmptyQueue_NoneTick_StandsNoMove()
        {
            var c = new ClientConnection(null!, 1) { X = 0.5f, Y = 0.5f, State = PlayerState.Move };

            var (applied, dropped) = Drain(c);

            Assert.Equal(0, applied);                  // None-тик
            Assert.Equal(0, dropped);
            Assert.Equal(PlayerState.Stand, c.State);  // Step(Move,None) → Stand
            Assert.Equal(0.5f, c.Y);                   // без движения
        }

        [Fact]
        public void Drain_Flood_AppliesCapThenDropsTail()
        {
            var c = new ClientConnection(null!, 1) { X = 0.5f, Y = 0.5f };
            const int n = MaxPerTick + 8;
            for (uint i = 1; i <= n; i++)
                c.IntentQueue.Enqueue(new MoveIntent { Direction = IntentDirection.North, Sequence = i });

            var (applied, dropped) = Drain(c);

            Assert.Equal(MaxPerTick, applied);
            Assert.Equal(n - MaxPerTick, dropped);
            Assert.True(c.IntentQueue.IsEmpty);
            Assert.Equal((uint)MaxPerTick, c.LastProcessedSequence); // ack = последний ПРИМЕНЁННЫЙ, не дропнутый
        }
    }
}
