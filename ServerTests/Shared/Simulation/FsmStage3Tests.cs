using Shared.Messages.Core;
using Shared.Simulation;

namespace ServerTests.Shared.Simulation
{
    /// <summary>
    /// Этап 3: серверный State считается по ВВОДУ (FsmLogic.Step), а не по дельте позиции (Этап 2).
    /// Повторяет КОМПОЗИЦИЮ GameServer.ProcessIntents (Step → гейт MovementAllowed → шаг движения)
    /// на Shared-типах — серверный ProcessIntents/_clients/client.State из теста недоступны (приватны),
    /// а добавлять рантайм-API только ради теста задание запрещает.
    /// </summary>
    public class FsmStage3Tests
    {
        // Шаг в свободном пространстве: та же арифметика, что и в движении (оси × шаг × множители).
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

        // Один «тик» как в ProcessIntents: FSM по вводу, затем движение только если MovementAllowed
        // (blocked — упёрся в преграду: шаг не применяется, но состояние считается по ВВОДУ).
        private static PlayerState Tick(bool blocked, ref float x, ref float y, IntentDirection dir, ref StatusTimers timers)
        {
            var state = FsmLogic.Step(PlayerState.Stand, dir, layToggle: false, LayingReason.None, ref timers);
            if (FsmLogic.MovementAllowed(state) && !blocked)
                FreeStep(ref x, ref y, dir);
            return state;
        }

        [Fact]
        public void InputIntoWall_StateIsMove_PositionUnchanged()
        {
            float x = 0.5f, y = 0.5f;
            StatusTimers timers = default;
            var state = Tick(blocked: true, ref x, ref y, IntentDirection.North, ref timers);

            Assert.Equal(PlayerState.Move, state); // ввод≠0 → Move даже в преграду (отличие от Этапа 2)
            Assert.Equal(0.5f, x);                 // упёрся — позиция не изменилась
            Assert.Equal(0.5f, y);
        }

        [Fact]
        public void EmptyTick_StateIsStand()
        {
            float x = 0.5f, y = 0.5f;
            StatusTimers timers = default;
            var state = Tick(blocked: false, ref x, ref y, IntentDirection.None, ref timers);

            Assert.Equal(PlayerState.Stand, state); // нет ввода → Stand
            Assert.Equal(0.5f, x);
            Assert.Equal(0.5f, y);
        }

        [Fact]
        public void FreeInput_StateIsMove_PositionChanged()
        {
            float x = 0.5f, y = 0.5f;
            StatusTimers timers = default;
            var state = Tick(blocked: false, ref x, ref y, IntentDirection.North, ref timers);

            Assert.Equal(PlayerState.Move, state);             // ввод → Move
            Assert.True(y > 0.5f, $"должен сдвинуться, y={y}"); // позиция изменилась
        }
    }
}
