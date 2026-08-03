using System;
using System.Collections.Generic;
using Server.Lifts;
using Shared.Messages.Core;
using Shared.Simulation;
using Shared.Simulation.Blocks;
using Shared.World.Blocks;

namespace ServerTests.Shared.Simulation.Blocks
{
    public class LiftCarryTests
    {
        private const ushort Full = 1;
        private const ushort Slab = 2;

        private sealed class TestShapes : IBlockShapes
        {
            private static readonly BlockBox[] None = Array.Empty<BlockBox>();
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

        private static BlockGrid Room()
        {
            var g = new BlockGrid();
            for (int x = -4; x <= 12; x++)
                for (int z = -4; z <= 8; z++)
                    g.SetBlock(x, 0, z, Slab);
            return g;
        }

        private static BlockGrid RoomWithCeiling()
        {
            var g = Room();
            for (int x = 3; x <= 7; x++)
                for (int z = -2; z <= 2; z++)
                    g.SetBlock(x, 3, z, Full);
            return g;
        }

        private static LiftRuntime Platform2x2(float fromY, float toY, uint startTick, float speed = 0.05f)
            => new LiftRuntime(5f, 0f, new[] { new LiftBox(0f, 0f, 0f, 1f, 1f, 0.25f) },
                new LiftSegment(fromY, toY, startTick, speed));

        private static readonly BlockMoveInput None = new BlockMoveInput(IntentDirection.None);

        private static (BlockMoverState final, List<(float, float, float, float, bool)> track) Run(
            Func<BlockGrid> makeGrid, Func<LiftRuntime> makeLift, Func<int, BlockMoveInput> inputAt,
            Action<LiftRuntime, uint>? onTick = null, int ticks = 240, float startY = 3f)
        {
            var g = makeGrid();
            var lift = makeLift();
            var set = new DynamicObstacleSet();
            var s = new BlockMoverState(5f, startY, 0f, grounded: false);
            var track = new List<(float, float, float, float, bool)>(ticks);

            for (int k = 0; k < ticks; k++)
            {
                onTick?.Invoke(lift, (uint)k);
                set.Clear();
                lift.Tick((uint)k, set);
                var input = inputAt(k);
                BlockMovementLogic.Step(g, Shapes, ref s, in input, MovementLogic.StepPerTick, set);
                Assert.False(float.IsNaN(s.X) || float.IsNaN(s.Y) || float.IsNaN(s.Z) || float.IsNaN(s.VY),
                    $"NaN на тике {k}");
                track.Add((s.X, s.Y, s.Z, s.VY, s.Grounded));
            }
            return (s, track);
        }

        private static void AssertReplayIdentical(
            Func<BlockGrid> makeGrid, Func<LiftRuntime> makeLift, Func<int, BlockMoveInput> inputAt,
            Action<LiftRuntime, uint>? onTick = null, float startY = 3f)
        {
            var (_, a) = Run(makeGrid, makeLift, inputAt, onTick, startY: startY);
            var (_, b) = Run(makeGrid, makeLift, inputAt, onTick, startY: startY);
            for (int k = 0; k < a.Count; k++)
                Assert.Equal(a[k], b[k]);
        }

        [Fact]
        public void Trajectory_BeforeDuringAfter_AndArrival()
        {
            var s = new LiftSegment(1f, 5f, 100, 0.0625f);

            Assert.Equal(1f, LiftTrajectory.YAt(in s, 0));
            Assert.Equal(1f, LiftTrajectory.YAt(in s, 100));
            Assert.Equal(1.0625f, LiftTrajectory.YAt(in s, 101));
            Assert.Equal(3f, LiftTrajectory.YAt(in s, 132));
            Assert.Equal(164u, LiftTrajectory.ArrivalTick(in s));
            Assert.Equal(5f, LiftTrajectory.YAt(in s, 164));
            Assert.Equal(5f, LiftTrajectory.YAt(in s, 100000));

            var down = new LiftSegment(5f, 1f, 10, 0.0625f);
            Assert.Equal(74u, LiftTrajectory.ArrivalTick(in down));
            Assert.Equal(4.9375f, LiftTrajectory.YAt(in down, 11));
            Assert.Equal(1f, LiftTrajectory.YAt(in down, 74));
        }

        [Fact]
        public void Trajectory_DeltaSum_EqualsFullTravel_Bitwise()
        {
            var s = new LiftSegment(1f, 5f, 100, 0.0625f);
            float sum = 0f;
            for (uint t = 90; t <= 300; t++)
                sum += LiftTrajectory.DeltaAt(in s, t);
            Assert.Equal(4f, sum);

            var down = new LiftSegment(5f, 1f, 0, 0.0625f);
            float sumDown = 0f;
            for (uint t = 0; t <= 200; t++)
                sumDown += LiftTrajectory.DeltaAt(in down, t);
            Assert.Equal(-4f, sumDown);
        }

        [Fact]
        public void Trajectory_DeltaAtTickZero_NoUnderflow()
        {
            var s = new LiftSegment(1f, 5f, 0, 0.0625f);
            Assert.Equal(0f, LiftTrajectory.DeltaAt(in s, 0));
            Assert.Equal(0.0625f, LiftTrajectory.DeltaAt(in s, 1), 6);
        }

        [Fact]
        public void Trajectory_RetargetMidway_ContinuousNoTeleport()
        {
            var lift = Platform2x2(1f, 5f, 0, 0.0625f);
            float yPrev = lift.YAt(31);
            Assert.Equal(2.9375f, yPrev);

            lift.Retarget(1f, 32);
            Assert.Equal(yPrev, lift.Segment.FromY);
            Assert.Equal(1f, lift.Segment.ToY);
            Assert.Equal(2.875f, lift.YAt(32));
            Assert.Equal(0.0625f, Math.Abs(lift.YAt(32) - yPrev), 6);
            Assert.Equal(-0.0625f, LiftTrajectory.DeltaAt(lift.Segment, 32), 6);
            Assert.Equal(1f, lift.YAt(200));

            var same = Platform2x2(1f, 5f, 0, 0.0625f);
            same.Retarget(5f, 32);
            Assert.Equal(3f, same.YAt(32));
        }

        [Fact]
        public void Replay_RideUp_BitwiseIdentical_AndArrives()
        {
            Func<LiftRuntime> lift = () => Platform2x2(1f, 5f, 30);
            AssertReplayIdentical(Room, lift, _ => None);

            var (final, _) = Run(Room, lift, _ => None);
            Assert.Equal(5.25f, final.Y, 3);
            Assert.True(final.Grounded);
            Assert.Equal(0f, final.VY);
        }

        [Fact]
        public void Replay_RideDown_BitwiseIdentical_AndArrives()
        {
            Func<LiftRuntime> lift = () => Platform2x2(5f, 1f, 60);
            Func<BlockGrid> grid = Room;

            var (warm, _) = Run(grid, lift, _ => None, ticks: 59, startY: 6f);
            Assert.Equal(5.25f, warm.Y, 3);

            AssertReplayIdentical(grid, lift, _ => None, startY: 6f);
            var (final, _) = Run(grid, lift, _ => None, startY: 6f);
            Assert.Equal(1.25f, final.Y, 3);
            Assert.True(final.Grounded);
        }

        [Fact]
        public void Replay_RetargetMidway_UpThenDown_Bitwise()
        {
            Func<LiftRuntime> lift = () => Platform2x2(1f, 5f, 30);
            Action<LiftRuntime, uint> onTick = (l, t) => { if (t == 70) l.Retarget(1f, 70); };

            AssertReplayIdentical(Room, lift, _ => None, onTick);

            var (final, track) = Run(Room, lift, _ => None, onTick);
            Assert.Equal(1.25f, final.Y, 3);
            Assert.True(final.Grounded);

            float maxY = float.MinValue;
            foreach (var p in track)
                if (p.Item2 > maxY) maxY = p.Item2;
            Assert.True(maxY > 2f && maxY < 5.25f, $"вершина={maxY}");
        }

        [Fact]
        public void Replay_WalkingOnMovingPlatform_Bitwise_StaysAboard()
        {
            Func<LiftRuntime> lift = () => Platform2x2(1f, 5f, 30);
            static BlockMoveInput InputAt(int k)
            {
                if (k < 30) return None;
                return new BlockMoveInput((k / 4) % 2 == 0 ? IntentDirection.East : IntentDirection.West);
            }

            AssertReplayIdentical(Room, lift, InputAt);

            var (final, _) = Run(Room, lift, InputAt);
            Assert.Equal(5.25f, final.Y, 3);
            Assert.True(final.Grounded);
            Assert.True(final.X > 4f && final.X < 6f, $"X={final.X}");
        }

        [Fact]
        public void Replay_WalkOffMovingPlatform_FallsAndLands()
        {
            Func<LiftRuntime> lift = () => Platform2x2(1f, 5f, 30);
            static BlockMoveInput InputAt(int k)
                => k >= 45 && k < 70 ? new BlockMoveInput(IntentDirection.East) : None;

            AssertReplayIdentical(Room, lift, InputAt);

            var (final, _) = Run(Room, lift, InputAt);
            Assert.Equal(1f, final.Y, 3);
            Assert.True(final.Grounded);
            Assert.True(final.X > 6f && final.X < 12f, $"X={final.X}");
        }

        [Fact]
        public void Ceiling_LiftPushesPlayerUnderBlocks_DeterministicNoClip()
        {
            Func<LiftRuntime> lift = () => Platform2x2(1f, 5f, 30);

            AssertReplayIdentical(RoomWithCeiling, lift, _ => None, startY: 1.5f);

            var (final, track) = Run(RoomWithCeiling, lift, _ => None, startY: 1.5f);

            float maxHead = float.MinValue;
            foreach (var p in track)
            {
                float head = p.Item2 + BlockMovementConfig.StandHeight;
                if (head > maxHead) maxHead = head;
            }
            Assert.True(maxHead <= 3f + BlockMovementConfig.Epsilon, $"голова в потолке: {maxHead}");
            Assert.Equal(1f, final.Y, 3);
            Assert.True(final.Grounded);
        }

        [Fact]
        public void CrateStyleZeroDelta_TakesOldPath()
        {
            var set = new DynamicObstacleSet();
            set.Add(3.5f, 1f, 0.5f, 0.5f, 1f);
            Assert.False(set.HasMovingBoxes);

            set.Add(5.5f, 1f, 0.5f, 0.5f, 1f, 0.05f);
            Assert.True(set.HasMovingBoxes);
            Assert.Equal(0f, set.GetDeltaY(0));
            Assert.Equal(0.05f, set.GetDeltaY(1));

            set.Clear();
            Assert.False(set.HasMovingBoxes);
        }

        // Контентная скорость кабины: SpeedBlocksPerSec = 3 при TickRate 30 → 0.10 блока/тик.
        // Ровно её и не покрывала зашитая в Platform2x2 константа 0.05 — из-за чего 815 зелёных тестов
        // пропустили 100% воспроизводимый провал сквозь платформу.
        // Гарантия даётся ДО контентной скорости включительно. Выше — известный предел, см.
        // KnownLimit_SpeedAboveContentRate_StillPenetrates и docs/plan-lifts.md.
        public static IEnumerable<object[]> CarrySpeeds => new[]
        {
            new object[] { 0.05f },
            new object[] { 0.10f }
        };

        private static float LiftTopAt(LiftRuntime lift, uint tick)
        {
            var set = new DynamicObstacleSet();
            lift.Tick(tick, set);
            set.Get(0, out _, out _, out _, out _, out float maxY, out _);
            return maxY;
        }

        [Theory]
        [MemberData(nameof(CarrySpeeds))]
        public void JumpOnRisingPlatform_NeverFallsThrough(float speed)
        {
            Func<LiftRuntime> lift = () => Platform2x2(1f, 9f, 10, speed);
            var (final, track) = Run(Room, lift,
                k => k == 40 ? new BlockMoveInput(IntentDirection.None, jump: true) : None,
                ticks: 200, startY: 1.25f);

            float liftTop = LiftTopAt(lift(), (uint)(track.Count - 1));
            Assert.True(final.Y >= liftTop - 0.001f,
                $"скорость {speed}: игрок провалился — итог y={final.Y}, верх кабины {liftTop}");
            Assert.True(final.Grounded, $"скорость {speed}: игрок не на опоре в конце");
        }

        [Theory]
        [MemberData(nameof(CarrySpeeds))]
        public void JumpOnRisingPlatform_NeverPenetratesCabin_OnAnyTick(float speed)
        {
            Func<LiftRuntime> lift = () => Platform2x2(1f, 9f, 10, speed);
            var (_, track) = Run(Room, lift,
                k => k == 40 ? new BlockMoveInput(IntentDirection.None, jump: true) : None,
                ticks: 200, startY: 1.25f);

            var probe = lift();
            float worst = 0f;
            int worstTick = -1;
            for (int k = 0; k < track.Count; k++)
            {
                float penetration = LiftTopAt(probe, (uint)k) - track[k].Item2;
                if (penetration > worst)
                {
                    worst = penetration;
                    worstTick = k;
                }
            }

            Assert.True(worst <= 0.001f,
                $"скорость {speed}: ноги ушли внутрь кабины на {worst} блока (тик {worstTick}) — " +
                "проникновение и есть начало провала, конечная позиция его уже не ловит");
        }

        // ХАРАКТЕРИЗАЦИОННЫЙ тест: фиксирует ИЗВЕСТНЫЙ ПРЕДЕЛ, а не желаемое поведение.
        // Выше контентной скорости первое проникновение случается на ВОСХОДЯЩЕЙ половине прыжка, где
        // MoveVertical спрашивает только потолок и пол не проверяет вовсе — точечным фиксом это не лечится,
        // нужен свип dyn-бокса в системе отсчёта игрока (docs/plan-lifts.md, «Известный предел»).
        // Если этот тест ПОКРАСНЕЛ — значит предел сдвинулся: обнови и тест, и plan-lifts.md.
        [Fact]
        public void KnownLimit_SpeedAboveContentRate_StillPenetrates()
        {
            const float Speed = 0.15f;
            Func<LiftRuntime> lift = () => Platform2x2(1f, 9f, 10, Speed);
            var (_, track) = Run(Room, lift,
                k => k == 40 ? new BlockMoveInput(IntentDirection.None, jump: true) : None,
                ticks: 200, startY: 1.25f);

            var probe = lift();
            float worst = 0f;
            for (int k = 0; k < track.Count; k++)
            {
                float penetration = LiftTopAt(probe, (uint)k) - track[k].Item2;
                if (penetration > worst)
                    worst = penetration;
            }

            Assert.True(worst > 0.001f,
                $"скорость {Speed} перестала ронять игрока (макс. проникновение {worst}) — предел сдвинулся, " +
                "обнови docs/plan-lifts.md и этот тест");
        }

        [Fact]
        public void TwoMovingBoxesUnderFeet_HigherTopWins_Deterministic()
        {
            var g = Room();
            var set = new DynamicObstacleSet();
            var s = new BlockMoverState(5f, 1.25f, 0f, grounded: true);

            for (int k = 1; k <= 40; k++)
            {
                set.Clear();
                float y = 1f + k * 0.05f;
                set.Add(4.6f, y - 0.05f + 0.05f, 0f, 0.6f, 0.25f, 0.05f);
                set.Add(5.4f, y, 0f, 0.6f, 0.25f, 0.05f);
                BlockMovementLogic.Step(g, Shapes, ref s, in None, MovementLogic.StepPerTick, set);
            }

            Assert.Equal(1.25f + 40 * 0.05f, s.Y, 3);
            Assert.True(s.Grounded);
        }
    }
}
