using System.Collections.Generic;
using Shared.Messages.Core;
using Shared.Simulation;
using Shared.Simulation.Blocks;
using Shared.World.Blocks;

namespace ServerTests.Shared.Simulation.Blocks
{
    // Оси как в Unity: X/Z — план, Y — высота.
    public class BlockMovementLogicTests
    {
        // Тестовые типы: 1 = полный куб, 2 = слаб пола (по верху блока), 3 = полублок-ступень, 4 = четверть-ступень.
        private const ushort Full = 1;
        private const ushort Slab = 2;
        private const ushort HalfStep = 3;
        private const ushort QuarterStep = 4;

        private sealed class TestShapes : IBlockShapes
        {
            private static readonly BlockBox[] None = System.Array.Empty<BlockBox>();
            private static readonly BlockBox[] FullBox = { BlockBox.Full };
            private static readonly BlockBox[] SlabBox = { BlockBox.SlabTop };
            private static readonly BlockBox[] HalfBox = { new BlockBox(0, 0, 0, 16, 8, 16) };
            private static readonly BlockBox[] QuarterBox = { new BlockBox(0, 0, 0, 16, 4, 16) };

            public BlockBox[] GetBoxes(ushort type, byte state) => type switch
            {
                Full => FullBox,
                Slab => SlabBox,
                HalfStep => HalfBox,
                QuarterStep => QuarterBox,
                _ => None
            };
        }

        private static readonly TestShapes Shapes = new();

        // Слаб-пол на прямоугольнике плана (x0..x1, z0..z1) на высоте y (поверхность ходьбы = y+1).
        private static void Floor(BlockGrid g, int x0, int x1, int z0, int z1, int y)
        {
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                    g.SetBlock(x, y, z, Slab);
        }

        private static BlockGrid Room()
        {
            var g = new BlockGrid();
            Floor(g, -4, 12, -4, 4, 0); // поверхность y=1
            return g;
        }

        private static void Run(BlockGrid g, ref BlockMoverState s, in BlockMoveInput input, int ticks)
        {
            for (int i = 0; i < ticks; i++)
                BlockMovementLogic.Step(g, Shapes, ref s, in input, MovementLogic.StepPerTick);
        }

        // Одиночный прыжок на месте (Jump один тик, дальше None): возвращает апекс ног.
        private static float OneJumpApex(BlockGrid g, ref BlockMoverState s, int ticks)
        {
            float apex = s.Y;
            var jump = new BlockMoveInput(IntentDirection.None, jump: true);
            BlockMovementLogic.Step(g, Shapes, ref s, in jump, MovementLogic.StepPerTick);
            var none = new BlockMoveInput(IntentDirection.None);
            for (int i = 1; i < ticks; i++)
            {
                if (s.Y > apex) apex = s.Y;
                BlockMovementLogic.Step(g, Shapes, ref s, in none, MovementLogic.StepPerTick);
            }
            return apex > s.Y ? apex : s.Y;
        }

        [Fact]
        public void NoInput_StaysPut()
        {
            var g = Room();
            var s = new BlockMoverState(0.5f, 1f, 0.5f);
            Run(g, ref s, new BlockMoveInput(IntentDirection.None), 30);

            Assert.Equal((0.5f, 1f, 0.5f, 0f, true), (s.X, s.Y, s.Z, s.VY, s.Grounded));
        }

        [Fact]
        public void Walk_StopsAtWall()
        {
            var g = Room();
            g.SetBlock(3, 1, 0, Full); // стена 2 блока над полом на x∈[3,4)
            g.SetBlock(3, 2, 0, Full);

            var s = new BlockMoverState(2.0f, 1f, 0.5f);
            Run(g, ref s, new BlockMoveInput(IntentDirection.East), 20);

            Assert.InRange(s.X, 2.59f, 2.61f); // упор: 3.0 − HalfWidth
            Assert.Equal(1f, s.Y);
            Assert.True(s.Grounded);
        }

        [Fact]
        public void AutoStep_QuarterAndHalf()
        {
            var g = Room();
            g.SetBlock(3, 1, 0, QuarterStep); // +0.25
            g.SetBlock(5, 1, 0, HalfStep);    // ещё +0.25 (верх 1.5)

            var s = new BlockMoverState(2.0f, 1f, 0.5f);
            Run(g, ref s, new BlockMoveInput(IntentDirection.East), 45);

            Assert.True(s.X > 5.2f);
            Assert.Equal(1.5f, s.Y); // забрались на обе ступени ногами
            Assert.True(s.Grounded);
        }

        [Fact]
        public void FullBlockRise_NotAutoStepped()
        {
            var g = Room();
            g.SetBlock(3, 1, 0, Full); // подъём 1.0 — только прыжком

            var s = new BlockMoverState(2.0f, 1f, 0.5f);
            Run(g, ref s, new BlockMoveInput(IntentDirection.East), 20);

            Assert.InRange(s.X, 2.59f, 2.61f);
            Assert.Equal(1f, s.Y);
        }

        [Fact]
        public void Jump_ApexNearOneTenth()
        {
            var g = Room();
            var s = new BlockMoverState(0.5f, 1f, 0.5f);

            float apex = OneJumpApex(g, ref s, 40);

            Assert.InRange(apex - 1f, 1.05f, 1.15f); // апекс ≈ 1.10: 1 блок берётся, 1.25 — нет
            Assert.Equal(1f, s.Y);                   // вернулись на пол
            Assert.True(s.Grounded);
        }

        [Fact]
        public void Jump_MountsOneBlockLedge()
        {
            var g = Room();
            for (int x = 5; x <= 8; x++)
                g.SetBlock(x, 1, 0, Full); // уступ высотой 1 (верх 2.0)

            var s = new BlockMoverState(4.0f, 1f, 0.5f);
            Run(g, ref s, new BlockMoveInput(IntentDirection.East, sprint: true, jump: true), 25);
            Run(g, ref s, new BlockMoveInput(IntentDirection.None), 30); // отпустили — устаканились

            Assert.Equal(2f, s.Y);
            Assert.True(s.Grounded);
            Assert.True(s.X > 5f);
        }

        [Fact]
        public void Jump_CannotMountTwoBlocks()
        {
            var g = Room();
            g.SetBlock(5, 1, 0, Full);
            g.SetBlock(5, 2, 0, Full); // стена 2 (верх 3.0)

            var s = new BlockMoverState(4.0f, 1f, 0.5f);
            Run(g, ref s, new BlockMoveInput(IntentDirection.East, sprint: true, jump: true), 40);
            Run(g, ref s, new BlockMoveInput(IntentDirection.None), 30);

            Assert.Equal(1f, s.Y);
            Assert.True(s.X < 4.7f); // упёрся: 5.0 − HalfWidth
        }

        [Fact]
        public void Jump_BumpsCeiling()
        {
            var g = Room();
            for (int x = -1; x <= 2; x++)
                for (int z = -1; z <= 2; z++)
                    g.SetBlock(x, 3, z, Full); // потолок: низ 3.0, хедрум ног 1.6

            var s = new BlockMoverState(0.5f, 1f, 0.5f);
            float apex = OneJumpApex(g, ref s, 40);

            Assert.Equal(1.6f, apex); // 3.0 − рост 1.4
            Assert.Equal(1f, s.Y);
            Assert.True(s.Grounded);
        }

        [Fact]
        public void WalkOffEdge_FallsAndLands()
        {
            var g = new BlockGrid();
            Floor(g, 0, 3, 0, 1, 0);   // верхняя площадка (поверхность 1)
            Floor(g, 0, 12, 0, 1, -4); // дно (поверхность −3)

            var s = new BlockMoverState(3.0f, 1f, 0.5f);
            Run(g, ref s, new BlockMoveInput(IntentDirection.East), 90);

            Assert.Equal(-3f, s.Y);
            Assert.True(s.Grounded);
            Assert.Equal(0f, s.VY);
        }

        [Fact]
        public void LongFall_ClampedByTerminalVelocity()
        {
            var g = new BlockGrid();
            Floor(g, 0, 1, 0, 1, 0);

            var s = new BlockMoverState(0.5f, 120f, 0.5f, grounded: false);
            float minVy = 0f;
            var none = new BlockMoveInput(IntentDirection.None);
            for (int i = 0; i < 400 && !s.Grounded; i++)
            {
                BlockMovementLogic.Step(g, Shapes, ref s, in none, MovementLogic.StepPerTick);
                if (s.VY < minVy) minVy = s.VY;
            }

            Assert.Equal(-BlockMovementConfig.TerminalVelocity, minVy);
            Assert.True(s.Grounded);
            Assert.Equal(1f, s.Y);
        }

        [Fact]
        public void Crawl_PassesUnderQuarterGap()
        {
            var g = Room();
            for (int x = 3; x <= 5; x++)
                g.SetBlock(x, 1, 0, Slab); // навес: низ 1.75 — зазор 0.75 над полом

            var stand = new BlockMoverState(2.0f, 1f, 0.5f);
            Run(g, ref stand, new BlockMoveInput(IntentDirection.East), 30);
            Assert.True(stand.X < 2.61f); // стоя (1.4) не пролезть

            var crawl = new BlockMoverState(2.0f, 1f, 0.5f);
            Run(g, ref crawl, new BlockMoveInput(IntentDirection.East, crawl: true), 60);
            Assert.True(crawl.X > 4.5f); // ползком (0.45) — проходим
            Assert.Equal(1f, crawl.Y);

            Assert.False(BlockMovementLogic.CanStand(g, Shapes, 4.0f, 1f, 0.5f)); // под навесом не встать
        }

        [Fact]
        public void EdgeSupport_AnyOverlapHolds()
        {
            var g = new BlockGrid();
            Floor(g, 0, 3, 0, 1, 0); // край пола: x < 4

            var held = new BlockMoverState(4.35f, 1f, 0.5f); // навес 0.05 футпринта над полом
            Run(g, ref held, new BlockMoveInput(IntentDirection.None), 10);
            Assert.True(held.Grounded);
            Assert.Equal(1f, held.Y);

            var falls = new BlockMoverState(4.45f, 1f, 0.5f); // футпринт целиком над пустотой
            Run(g, ref falls, new BlockMoveInput(IntentDirection.None), 10);
            Assert.False(falls.Grounded);
            Assert.True(falls.Y < 1f);
        }

        [Fact]
        public void HalfBaseStep_HalvesHorizontalDelta()
        {
            var g1 = Room();
            var g2 = Room();
            var s1 = new BlockMoverState(0.5f, 1f, 0.5f);
            var s2 = new BlockMoverState(0.5f, 1f, 0.5f);
            var east = new BlockMoveInput(IntentDirection.East);

            for (int i = 0; i < 20; i++)
            {
                BlockMovementLogic.Step(g1, Shapes, ref s1, in east, MovementLogic.StepPerTick);
                BlockMovementLogic.Step(g2, Shapes, ref s2, in east, 0.5f * MovementLogic.StepPerTick);
            }

            float fullDelta = s1.X - 0.5f;
            float halfDelta = s2.X - 0.5f;
            Assert.True(fullDelta > 0f);
            Assert.True(System.MathF.Abs(halfDelta - 0.5f * fullDelta) < 1e-5f, $"full={fullDelta}, half={halfDelta}");
            Assert.Equal(0.5f, s1.Z, 5);
            Assert.Equal(0.5f, s2.Z, 5);
        }

        [Fact]
        public void Replay_IsBitwiseDeterministic()
        {
            static BlockGrid Build()
            {
                var g = Room();
                g.SetBlock(3, 1, 0, HalfStep);
                g.SetBlock(6, 1, 0, Full);
                for (int x = -1; x <= 2; x++) g.SetBlock(x, 3, 1, Full);
                return g;
            }

            static BlockMoveInput InputAt(int k) => new BlockMoveInput(
                (k / 7) % 2 == 0 ? IntentDirection.East : IntentDirection.NorthWest,
                sprint: k % 3 == 0, jump: k % 11 == 0, crawl: (k / 29) % 2 == 1);

            var g1 = Build();
            var g2 = Build();
            var s1 = new BlockMoverState(0.5f, 1f, 0.5f);
            var s2 = new BlockMoverState(0.5f, 1f, 0.5f);

            var track = new List<(float, float, float, float, bool)>();
            for (int k = 0; k < 240; k++)
            {
                var input = InputAt(k);
                BlockMovementLogic.Step(g1, Shapes, ref s1, in input, MovementLogic.StepPerTick);
                track.Add((s1.X, s1.Y, s1.Z, s1.VY, s1.Grounded));
            }
            for (int k = 0; k < 240; k++)
            {
                var input = InputAt(k);
                BlockMovementLogic.Step(g2, Shapes, ref s2, in input, MovementLogic.StepPerTick);
                Assert.Equal(track[k], (s2.X, s2.Y, s2.Z, s2.VY, s2.Grounded)); // побитово, без допусков
            }
        }
    }
}
