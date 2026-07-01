using Server.Network;
using Shared.Messages.Core;
using Shared.Simulation;
using Shared.World;

namespace ServerTests.Shared.Simulation
{
    /// <summary>
    /// Этап 6: server-only Dead/Unconscious через entry-API (Kill/SetUnconscious) + флаги DisableMovement/
    /// IgnoreCollision. Гейт движения GameServer.ProcessIntents приватен → повторяем его клауз здесь
    /// (FsmLogic.MovementAllowed && !DisableMovement), как в FsmStage3-5Tests. Entry-API/Step — реальные.
    /// </summary>
    public class FsmStage6Tests
    {
        private static GridMap FloorRow()
        {
            var map = new GridMap();
            map.SetTile(0, 0, 0, Tile.Floor());
            map.SetTile(0, 1, 0, Tile.Floor()); // путь на север свободен
            return map;
        }

        // Зеркало гейта движения GameServer.ProcessIntents: один тик, возвращает позицию после (не)движения.
        private static (float x, float y) GatedMove(ClientConnection c, GridMap map, IntentDirection dir)
        {
            float x = 0.5f, y = 0.5f;
            if (FsmLogic.MovementAllowed(c.State) && !c.DisableMovement)
                MovementLogic.Apply(map, 0, ref x, ref y, dir, sprint: false, crawl: c.State == PlayerState.Laying);
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

            var (x, y) = GatedMove(c, FloorRow(), IntentDirection.North);
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

            var (x, y) = GatedMove(c, FloorRow(), IntentDirection.North);
            Assert.Equal(0.5f, x);
            Assert.Equal(0.5f, y);
        }

        [Fact]
        public void DisableMovement_FreezesStandPlayer_ExercisesNewGateClause()
        {
            // Stand → MovementAllowed=true; блокирует ТОЛЬКО клауз !DisableMovement (упражняет новый гейт).
            var c = new ClientConnection(null!, 1) { State = PlayerState.Stand, DisableMovement = true };
            Assert.True(FsmLogic.MovementAllowed(c.State)); // сам по себе Stand двигается

            var (_, yFrozen) = GatedMove(c, FloorRow(), IntentDirection.North);
            Assert.Equal(0.5f, yFrozen); // но DisableMovement замораживает

            c.DisableMovement = false; // контроль: без флага — двигается
            var (_, yMoved) = GatedMove(c, FloorRow(), IntentDirection.North);
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
