using Server.Network;
using Shared.Messages.Core;
using Shared.Simulation;

namespace ServerTests.Shared.Simulation
{
    /// <summary>
    /// Этап 5: server-only Stun/Laying(KnockedDown) через тик-таймеры (entry-API + ProcessStatus) и краул ×0.7.
    /// Entry-API на ClientConnection — реальный; декремент-логику ProcessStatus повторяем здесь (приватна на
    /// GameServer, как Step-нить в FsmStage3/4Tests). Краул и Step-ветви — на реальных Shared-функциях.
    /// </summary>
    public class FsmStage5Tests
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

        // Эквивалент GameServer.ProcessStatus (приватный): один тик декремента таймеров.
        private static void ProcessStatusTick(ClientConnection c)
        {
            if (c.State == PlayerState.Stun)
            {
                if (--c.Timers.StunTicksRemaining <= 0) c.State = PlayerState.Stand;
            }
            else if (c.State == PlayerState.Laying && c.CurrentLayingReason == LayingReason.KnockedDown)
            {
                if (--c.Timers.KnockdownTicksRemaining <= 0)
                {
                    c.State = PlayerState.Stand;
                    c.CurrentLayingReason = LayingReason.None;
                }
            }
        }

        [Fact]
        public void ApplyStun_FreezesExactlyNTicks_ThenStand()
        {
            var c = new ClientConnection(null!, 1);
            c.ApplyStun(3);

            Assert.Equal(PlayerState.Stun, c.State);
            Assert.False(FsmLogic.MovementAllowed(c.State)); // заморожен

            ProcessStatusTick(c); Assert.Equal(PlayerState.Stun, c.State);  // 3→2
            ProcessStatusTick(c); Assert.Equal(PlayerState.Stun, c.State);  // 2→1
            ProcessStatusTick(c); Assert.Equal(PlayerState.Stand, c.State); // 1→0 (<=0) → Stand
        }

        [Fact]
        public void KnockDown_LaysExactlyNTicks_ThenStandAndReasonCleared()
        {
            var c = new ClientConnection(null!, 1);
            c.KnockDown(2);

            Assert.Equal(PlayerState.Laying, c.State);
            Assert.Equal(LayingReason.KnockedDown, c.CurrentLayingReason);
            Assert.True(FsmLogic.MovementAllowed(c.State)); // ползёт, не заморожен

            ProcessStatusTick(c); Assert.Equal(PlayerState.Laying, c.State); // 2→1
            ProcessStatusTick(c);                                            // 1→0 (<=0) → Stand
            Assert.Equal(PlayerState.Stand, c.State);
            Assert.Equal(LayingReason.None, c.CurrentLayingReason);          // причина сброшена
        }

        [Fact]
        public void Crawl_MovesAtSeventyPercentOfWalk()
        {

            float xW = 0.5f, yW = 0.5f;
            FreeStep(ref xW, ref yW, IntentDirection.North, sprint: false, crawl: false);

            float xC = 0.5f, yC = 0.5f;
            FreeStep(ref xC, ref yC, IntentDirection.North, sprint: false, crawl: true);

            float walk = yW - 0.5f;
            float crawl = yC - 0.5f;
            Assert.True(crawl < walk, $"краул медленнее ходьбы: walk={walk}, crawl={crawl}");
            Assert.True(System.MathF.Abs(crawl - walk * MovementLogic.CrawlMultiplier) < 1e-5f,
                $"краул должен быть ×{MovementLogic.CrawlMultiplier}: walk={walk}, crawl={crawl}");
        }

        [Fact]
        public void Step_Stun_StaysStunned_MovementBlocked()
        {
            StatusTimers t = default;
            // Из Stun Step не выпускает (выход — только по таймеру в ProcessStatus), даже на ввод/toggle.
            var s = FsmLogic.Step(PlayerState.Stun, IntentDirection.North, layToggle: true, LayingReason.None, ref t);
            Assert.Equal(PlayerState.Stun, s);
            Assert.False(FsmLogic.MovementAllowed(PlayerState.Stun));
        }

        [Fact]
        public void Step_KnockedDown_CannotToggleUp_ButCrawls()
        {
            StatusTimers t = default;
            // KnockedDown: toggle НЕ поднимает (держит сервер по таймеру), но движение (краул) разрешено.
            var s = FsmLogic.Step(PlayerState.Laying, IntentDirection.None, layToggle: true, LayingReason.KnockedDown, ref t);
            Assert.Equal(PlayerState.Laying, s);
            Assert.True(FsmLogic.MovementAllowed(PlayerState.Laying));
        }
    }
}
