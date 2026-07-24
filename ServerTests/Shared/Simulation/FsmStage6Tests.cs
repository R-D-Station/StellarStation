using Server.Network;
using Shared.Messages.Core;
using Shared.Simulation;

namespace ServerTests.Shared.Simulation
{
    /// <summary>
    /// Этап 6: server-only Dead/Unconscious через entry-API (Kill/SetUnconscious) + флаги DisableMovement/
    /// IgnoreCollision. Гейт движения GameServer.ProcessIntents приватен → повторяем его клауз здесь
    /// (FsmLogic.MovementAllowed && !DisableMovement), как в FsmStage3-5Tests. Entry-API/Step — реальные.
    /// </summary>
    public class FsmStage6Tests
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

        // Зеркало гейта движения GameServer.ProcessIntents: один тик, возвращает позицию после (не)движения.
        private static (float x, float y) GatedMove(ClientConnection c, IntentDirection dir)
        {
            float x = 0.5f, y = 0.5f;
            if (FsmLogic.MovementAllowed(c.State) && !c.DisableMovement)
                FreeStep(ref x, ref y, dir, sprint: false, crawl: c.State == PlayerState.Laying);
            return (x, y);
        }

        [Fact]
        public void Kill_SetsDeadFlags_AndFreezesMovement()
        {
            var c = new ClientConnection(null!, 1);
            c.Kill();

            Assert.Equal(PlayerState.Dead, c.State);
            Assert.True(c.IgnoreCollision);
            Assert.True(c.DisableMovement);
            Assert.False(FsmLogic.MovementAllowed(c.State)); // Dead не пускает

            var (x, y) = GatedMove(c, IntentDirection.North);
            Assert.Equal(0.5f, x);
            Assert.Equal(0.5f, y); // заморожен — позиция не изменилась
        }

        [Fact]
        public void SetUnconscious_SetsFlags_AndFreezesMovement()
        {
            var c = new ClientConnection(null!, 1);
            c.SetUnconscious();

            Assert.Equal(PlayerState.Unconscious, c.State);
            Assert.True(c.DisableMovement);
            Assert.False(FsmLogic.MovementAllowed(c.State));

            var (x, y) = GatedMove(c, IntentDirection.North);
            Assert.Equal(0.5f, x);
            Assert.Equal(0.5f, y);
        }

        [Fact]
        public void DisableMovement_FreezesStandPlayer_ExercisesNewGateClause()
        {
            // Stand → MovementAllowed=true; блокирует ТОЛЬКО клауз !DisableMovement (упражняет новый гейт).
            var c = new ClientConnection(null!, 1) { State = PlayerState.Stand, DisableMovement = true };
            Assert.True(FsmLogic.MovementAllowed(c.State)); // сам по себе Stand двигается

            var (_, yFrozen) = GatedMove(c, IntentDirection.North);
            Assert.Equal(0.5f, yFrozen); // но DisableMovement замораживает

            c.DisableMovement = false; // контроль: без флага — двигается
            var (_, yMoved) = GatedMove(c, IntentDirection.North);
            Assert.True(yMoved > 0.5f, $"без DisableMovement должен двигаться, y={yMoved}");
        }

        [Fact]
        public void Step_HoldsDeadAndUnconscious_ExitExternalOnly()
        {
            StatusTimers t = default;
            // Dead/Unconscious: Step возвращает cur (выход только внешний/future) даже на ввод/toggle.
            Assert.Equal(PlayerState.Dead,
                FsmLogic.Step(PlayerState.Dead, IntentDirection.North, layToggle: true, LayingReason.None, ref t));
            Assert.Equal(PlayerState.Unconscious,
                FsmLogic.Step(PlayerState.Unconscious, IntentDirection.South, layToggle: true, LayingReason.None, ref t));
            Assert.False(FsmLogic.MovementAllowed(PlayerState.Dead));
            Assert.False(FsmLogic.MovementAllowed(PlayerState.Unconscious));
        }
    }
}
