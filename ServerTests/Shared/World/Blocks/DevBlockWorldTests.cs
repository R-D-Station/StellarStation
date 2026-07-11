using Shared.Messages.Core;
using Shared.Simulation.Blocks;
using Shared.World.Blocks;

namespace ServerTests.Shared.World.Blocks
{
    public class DevBlockWorldTests
    {
        [Fact]
        public void Build_IsDeterministic()
        {
            var a = new BlockGrid();
            var b = new BlockGrid();
            DevBlockWorld.Build(a);
            DevBlockWorld.Build(b);

            Assert.Equal(a.Sections.Count, b.Sections.Count);
            for (int x = -8; x <= 24; x++)
                for (int z = -8; z <= 8; z++)
                    Assert.Equal(a.GetBlock(x, 0, z), b.GetBlock(x, 0, z));
        }

        [Fact]
        public void Spawn_IsGroundedOnFloor()
        {
            var g = new BlockGrid();
            DevBlockWorld.Build(g);

            Assert.True(BlockMovementLogic.IsGrounded(g, DevBlockWorld.Shapes,
                DevBlockWorld.SpawnX, DevBlockWorld.SpawnY, DevBlockWorld.SpawnZ, vy: 0f));
        }

        [Fact]
        public void Course_WalkAndJumpAcross()
        {
            // Сквозной прогон полигона серверной веткой ввода: лесенка 0.25/0.5 → прыжок на уступ 1.0.
            var g = new BlockGrid();
            DevBlockWorld.Build(g);
            var s = new BlockMoverState(DevBlockWorld.SpawnX, DevBlockWorld.SpawnY, DevBlockWorld.SpawnZ);

            // Фаза 1: бег без прыжка — лесенка берётся автошагом, у стенки уступа (x=10) упор.
            var run = new BlockMoveInput(IntentDirection.East, sprint: true);
            for (int i = 0; i < 60; i++)
                BlockMovementLogic.Step(g, DevBlockWorld.Shapes, ref s, in run);
            Assert.InRange(s.X, 9.35f, 9.65f); // упор у стенки уступа (10.0 − HalfWidth, минус квант шага)
            Assert.Equal(1f, s.Y);

            // Фаза 2: с прыжком — заскакиваем на уступ.
            var jumpRun = new BlockMoveInput(IntentDirection.East, sprint: true, jump: true);
            for (int i = 0; i < 15; i++)
                BlockMovementLogic.Step(g, DevBlockWorld.Shapes, ref s, in jumpRun);
            var settle = new BlockMoveInput(IntentDirection.None);
            for (int i = 0; i < 25; i++)
                BlockMovementLogic.Step(g, DevBlockWorld.Shapes, ref s, in settle);

            Assert.InRange(s.X, 10.4f, 12.6f); // стоим НА уступе x∈[10..13)
            Assert.Equal(2f, s.Y);             // его верх
            Assert.True(s.Grounded);
        }
    }
}
