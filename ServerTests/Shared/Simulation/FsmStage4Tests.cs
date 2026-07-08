using Shared.Messages.Core;
using Shared.Simulation;
using Shared.World;

namespace ServerTests.Shared.Simulation
{
    /// <summary>
    /// Этап 4: добровольное лежание — input-triggered бит LayToggle, предсказывается клиентом.
    /// Повторяет running-state нить (FsmLogic.Step с reason=Voluntary), которой идентично гоняют
    /// GameServer.ProcessIntents, PlayerPredictor.ApplyLocal и Reconcile — детерминизм обе стороны.
    /// </summary>
    public class FsmStage4Tests
    {
        private static PlayerState Step(PlayerState s, IntentDirection dir, bool layToggle, ref StatusTimers t)
            => FsmLogic.Step(s, dir, layToggle, LayingReason.Voluntary, ref t);

        [Fact]
        public void LayToggle_StandToLayingToStand()
        {
            StatusTimers t = default;
            var s = PlayerState.Stand;

            s = Step(s, IntentDirection.None, layToggle: true, ref t);  // toggle: лечь
            Assert.Equal(PlayerState.Laying, s);

            s = Step(s, IntentDirection.None, layToggle: false, ref t); // держим без toggle — остаёмся лежать
            Assert.Equal(PlayerState.Laying, s);

            s = Step(s, IntentDirection.None, layToggle: true, ref t);  // toggle: встать
            Assert.Equal(PlayerState.Stand, s);
        }

        [Fact]
        public void ToggleWhileMoving_GoesToLaying()
        {
            StatusTimers t = default;
            var s = Step(PlayerState.Stand, IntentDirection.North, layToggle: false, ref t);
            Assert.Equal(PlayerState.Move, s);

            // На ходу toggle приоритетен над движением → лечь.
            s = Step(s, IntentDirection.North, layToggle: true, ref t);
            Assert.Equal(PlayerState.Laying, s);
        }

        [Fact]
        public void Laying_MovementAllowed_StillMoves()
        {
            // Этап 4: лежащий двигается (полная скорость; краул ×0.7 — Этап 5).
            Assert.True(FsmLogic.MovementAllowed(PlayerState.Laying));

            var map = new GridMap();
            map.SetTile(0, 0, 0, Tile.Floor());
            map.SetTile(0, 1, 0, Tile.Floor()); // путь на север свободен

            float x = 0.5f, y = 0.5f;
            MovementLogic.Apply(map, 0, ref x, ref y, IntentDirection.North, sprint: false);
            Assert.True(y > 0.5f, $"лежащий должен двигаться, y={y}");
        }
    }
}
