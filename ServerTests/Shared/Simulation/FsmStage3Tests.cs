using Shared.Messages.Core;
using Shared.Simulation;
using Shared.World;

namespace ServerTests.Shared.Simulation
{
    /// <summary>
    /// Этап 3: серверный State считается по ВВОДУ (FsmLogic.Step), а не по дельте позиции (Этап 2).
    /// Повторяет КОМПОЗИЦИЮ GameServer.ProcessIntents (Step → гейт MovementAllowed → MovementLogic.Apply)
    /// на Shared-типах — серверный ProcessIntents/_clients/client.State из теста недоступны (приватны),
    /// а добавлять рантайм-API только ради теста задание запрещает.
    /// </summary>
    public class FsmStage3Tests
    {
        private static Tile Wall() => new Tile { StructureType = 1, Support = true };

        // Один «тик» как в ProcessIntents: FSM по вводу, затем движение только если MovementAllowed.
        private static PlayerState Tick(GridMap map, ref float x, ref float y, IntentDirection dir, ref StatusTimers timers)
        {
            var state = FsmLogic.Step(PlayerState.Stand, dir, layToggle: false, LayingReason.None, ref timers);
            if (FsmLogic.MovementAllowed(state))
                MovementLogic.Apply(map, 0, ref x, ref y, dir, sprint: false);
            return state;
        }

        [Fact]
        public void InputIntoWall_StateIsMove_PositionUnchanged()
        {
            var map = new GridMap();
            map.SetTile(0, 0, 0, Tile.Floor());
            map.SetTile(0, 1, 0, Wall()); // сплошная стена на севере, прохода нет

            float x = 0.5f, y = 0.5f;
            StatusTimers timers = default;
            var state = Tick(map, ref x, ref y, IntentDirection.North, ref timers);

            Assert.Equal(PlayerState.Move, state); // ввод≠0 → Move даже в стену (отличие от Этапа 2)
            Assert.Equal(0.5f, x);                 // упёрся — позиция не изменилась
            Assert.Equal(0.5f, y);
        }

        [Fact]
        public void EmptyTick_StateIsStand()
        {
            var map = new GridMap();
            map.SetTile(0, 0, 0, Tile.Floor());

            float x = 0.5f, y = 0.5f;
            StatusTimers timers = default;
            var state = Tick(map, ref x, ref y, IntentDirection.None, ref timers);

            Assert.Equal(PlayerState.Stand, state); // нет ввода → Stand
            Assert.Equal(0.5f, x);
            Assert.Equal(0.5f, y);
        }

        [Fact]
        public void FreeInput_StateIsMove_PositionChanged()
        {
            var map = new GridMap();
            map.SetTile(0, 0, 0, Tile.Floor());
            map.SetTile(0, 1, 0, Tile.Floor()); // путь на север свободен

            float x = 0.5f, y = 0.5f;
            StatusTimers timers = default;
            var state = Tick(map, ref x, ref y, IntentDirection.North, ref timers);

            Assert.Equal(PlayerState.Move, state);             // ввод → Move
            Assert.True(y > 0.5f, $"должен сдвинуться, y={y}"); // позиция изменилась
        }
    }
}
