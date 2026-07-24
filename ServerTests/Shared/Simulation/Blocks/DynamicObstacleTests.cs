using System.Collections.Generic;
using Shared.Messages.Core;
using Shared.Simulation;
using Shared.Simulation.Blocks;
using Shared.World.Blocks;

namespace ServerTests.Shared.Simulation.Blocks
{
    public class DynamicObstacleTests
    {
        private const ushort Full = 1;
        private const ushort Slab = 2;

        private sealed class TestShapes : IBlockShapes
        {
            private static readonly BlockBox[] None = System.Array.Empty<BlockBox>();
            private static readonly BlockBox[] FullBox = { BlockBox.Full };
            private static readonly BlockBox[] SlabBox = { BlockBox.SlabTop };

            public BlockBox[] GetBoxes(ushort type, byte state) => type switch
            {
                Full => FullBox,
                Slab => SlabBox,
                _ => None
            };
        }

        private static readonly TestShapes Shapes = new();

        private static void Floor(BlockGrid g, int x0, int x1, int z0, int z1, int y)
        {
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                    g.SetBlock(x, y, z, Slab);
        }

        private static BlockGrid Room()
        {
            var g = new BlockGrid();
            Floor(g, -4, 12, -4, 8, 0);
            return g;
        }

        [Fact]
        public void Set_AddGetRoundTrip_Formula()
        {
            var set = new DynamicObstacleSet();
            set.Add(3.5f, 1f, 0.5f, 0.5f, 1f);
            Assert.Equal(1, set.Count);

            set.Get(0, out float minX, out float minY, out float minZ, out float maxX, out float maxY, out float maxZ);
            Assert.Equal(3.0f, minX, 5);
            Assert.Equal(4.0f, maxX, 5);
            Assert.Equal(1.0f, minY, 5);
            Assert.Equal(2.0f, maxY, 5);
            Assert.Equal(0.0f, minZ, 5);
            Assert.Equal(1.0f, maxZ, 5);

            set.Clear();
            Assert.Equal(0, set.Count);
        }

        [Fact]
        public void WalkEast_StopsAtDynamicAabb()
        {
            var g = Room();
            var dyn = new DynamicObstacleSet();
            dyn.Add(3.5f, 1f, 0.5f, 0.5f, 1f);
            var s = new BlockMoverState(0.5f, 1f, 0.5f);
            var east = new BlockMoveInput(IntentDirection.East);

            for (int i = 0; i < 100; i++)
                BlockMovementLogic.Step(g, Shapes, ref s, in east, MovementLogic.StepPerTick, dyn);

            Assert.True(s.X > 2.5f && s.X <= 3f - BlockMovementConfig.HalfWidth + BlockMovementConfig.Epsilon, $"X={s.X}");
            Assert.Equal(1f, s.Y, 5);
        }

        [Fact]
        public void FallOntoDynamicBox_LandsOnTopGrounded()
        {
            var g = Room();
            var dyn = new DynamicObstacleSet();
            dyn.Add(2.5f, 1f, 0.5f, 0.5f, 0.5f);
            var s = new BlockMoverState(2.5f, 3f, 0.5f);
            var none = new BlockMoveInput(IntentDirection.None);

            for (int i = 0; i < 100; i++)
                BlockMovementLogic.Step(g, Shapes, ref s, in none, MovementLogic.StepPerTick, dyn);

            Assert.Equal(1.5f, s.Y, 5);
            Assert.True(s.Grounded);
        }

        [Fact]
        public void Jump_HeadBumpsDynamicBoxAbove()
        {
            var g = Room();
            var dyn = new DynamicObstacleSet();
            dyn.Add(0.5f, 3f, 0.5f, 0.5f, 1f);

            float ApexWith(IDynamicObstacles obs)
            {
                var s = new BlockMoverState(0.5f, 1f, 0.5f);
                var none = new BlockMoveInput(IntentDirection.None);
                BlockMovementLogic.Step(g, Shapes, ref s, in none, MovementLogic.StepPerTick, obs);
                var jump = new BlockMoveInput(IntentDirection.None, jump: true);
                BlockMovementLogic.Step(g, Shapes, ref s, in jump, MovementLogic.StepPerTick, obs);
                float apex = s.Y;
                for (int i = 0; i < 60; i++)
                {
                    BlockMovementLogic.Step(g, Shapes, ref s, in none, MovementLogic.StepPerTick, obs);
                    if (s.Y > apex) apex = s.Y;
                }
                return apex;
            }

            float apexBlocked = ApexWith(dyn);
            float apexFree = ApexWith(null);

            Assert.True(apexBlocked <= 3f - BlockMovementConfig.StandHeight + BlockMovementConfig.Epsilon, $"blocked={apexBlocked}");
            Assert.True(apexFree > apexBlocked, $"free={apexFree}, blocked={apexBlocked}");
        }

        [Fact]
        public void EmptyDyn_SameAsNull_Bitwise()
        {
            var g1 = Room();
            var g2 = Room();
            var s1 = new BlockMoverState(0.5f, 1f, 0.5f);
            var s2 = new BlockMoverState(0.5f, 1f, 0.5f);
            var empty = new DynamicObstacleSet();
            var east = new BlockMoveInput(IntentDirection.East);

            for (int i = 0; i < 40; i++)
            {
                BlockMovementLogic.Step(g1, Shapes, ref s1, in east, MovementLogic.StepPerTick);
                BlockMovementLogic.Step(g2, Shapes, ref s2, in east, MovementLogic.StepPerTick, empty);
            }

            Assert.Equal((s1.X, s1.Y, s1.Z, s1.VY, s1.Grounded), (s2.X, s2.Y, s2.Z, s2.VY, s2.Grounded));
        }

        [Fact]
        public void BlockAndDynamic_BothStopIndependently_Coexist()
        {
            float StopX(BlockGrid g, IDynamicObstacles dyn)
            {
                var s = new BlockMoverState(0.5f, 1f, 0.5f);
                var east = new BlockMoveInput(IntentDirection.East);
                for (int i = 0; i < 100; i++)
                    BlockMovementLogic.Step(g, Shapes, ref s, in east, MovementLogic.StepPerTick, dyn);
                return s.X;
            }

            var farDyn = new DynamicObstacleSet();
            farDyn.Add(3.5f, 1f, 6.5f, 0.5f, 1f);

            var gBlock = Room();
            gBlock.SetBlock(3, 1, 0, Full);
            float xBlock = StopX(gBlock, farDyn);

            var nearDyn = new DynamicObstacleSet();
            nearDyn.Add(3.5f, 1f, 0.5f, 0.5f, 1f);
            float xDyn = StopX(Room(), nearDyn);

            float edge = 3f - BlockMovementConfig.HalfWidth + BlockMovementConfig.Epsilon;
            Assert.True(xBlock > 2.5f && xBlock <= edge, $"block={xBlock}");
            Assert.True(xDyn > 2.5f && xDyn <= edge, $"dyn={xDyn}");
        }

        [Fact]
        public void Replay_WithDynamicObstacles_IsBitwiseDeterministic()
        {
            static BlockGrid Build()
            {
                var g = Room();
                g.SetBlock(6, 1, 0, Full);
                return g;
            }

            static DynamicObstacleSet Dyn()
            {
                var d = new DynamicObstacleSet();
                d.Add(3.5f, 1f, 0.5f, 0.5f, 1f);
                d.Add(2.5f, 1f, -0.5f, 0.5f, 0.5f);
                d.Add(4.5f, 1f, 1.5f, 0.5f, 0.25f);
                return d;
            }

            static BlockMoveInput InputAt(int k) => new BlockMoveInput(
                (k / 7) % 2 == 0 ? IntentDirection.East : IntentDirection.NorthWest,
                sprint: k % 3 == 0, jump: k % 11 == 0, crawl: (k / 29) % 2 == 1);

            var g1 = Build();
            var g2 = Build();
            var d1 = Dyn();
            var d2 = Dyn();
            var s1 = new BlockMoverState(0.5f, 1f, 0.5f);
            var s2 = new BlockMoverState(0.5f, 1f, 0.5f);

            var track = new List<(float, float, float, float, bool)>();
            for (int k = 0; k < 240; k++)
            {
                var input = InputAt(k);
                BlockMovementLogic.Step(g1, Shapes, ref s1, in input, MovementLogic.StepPerTick, d1);
                track.Add((s1.X, s1.Y, s1.Z, s1.VY, s1.Grounded));
            }
            for (int k = 0; k < 240; k++)
            {
                var input = InputAt(k);
                BlockMovementLogic.Step(g2, Shapes, ref s2, in input, MovementLogic.StepPerTick, d2);
                Assert.Equal(track[k], (s2.X, s2.Y, s2.Z, s2.VY, s2.Grounded));
            }
        }
    }
}
