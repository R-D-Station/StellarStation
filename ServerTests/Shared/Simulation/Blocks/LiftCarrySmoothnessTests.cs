using System;
using System.Collections.Generic;
using System.Globalization;
using Server.Lifts;
using Shared.Messages.Core;
using Shared.Simulation;
using Shared.Simulation.Blocks;
using Shared.World.Blocks;

namespace ServerTests.Shared.Simulation.Blocks
{
    /// <summary>Вертикаль пассажира на едущем носителе: подпись «пилы» — Y то растёт, то падает.
    /// Мерим СЕРВЕРНЫЙ Mover.Y по тикам, а не смотрим на картинку.</summary>
    public class LiftCarrySmoothnessTests
    {
        private const ushort Slab = 2;

        // Бокс кабины АСИММЕТРИЧЕН по обеим плановым осям и смещён от якоря: симметричный не различает
        // ни подмену осей, ни подмену «центр ↔ угол».
        private const float AnchorX = 5f, AnchorZ = 0f;
        private const float BoxCX = 0.5f, BoxCZ = -0.25f;
        private const float BoxHX = 1.5f, BoxHZ = 0.75f;
        private const float BoxY = 0f, BoxH = 0.25f;
        // Пассажир стоит НЕ по центру бокса, а у той кромки, где перестановка полуширин или центров
        // выводит его ЗА план: в центре бокса подмена осей ненаблюдаема.
        // Полуширина игрока 0.4; бокс по X [4.0, 7.0], по Z [-1.0, 0.5].
        private const float PlayerX = 4.3f, PlayerZ = -0.8f;

        private const uint Depart = 20u;

        private sealed class TestShapes : IBlockShapes
        {
            private static readonly BlockBox[] None = Array.Empty<BlockBox>();
            private static readonly BlockBox[] SlabBox = { BlockBox.SlabTop };

            public BlockBox[] GetBoxes(ushort type, byte state) => type == Slab ? SlabBox : None;
        }

        private static readonly TestShapes Shapes = new();
        private static readonly BlockMoveInput Idle = new BlockMoveInput(IntentDirection.None);

        public static IEnumerable<object[]> Speeds => new[]
        {
            new object[] { 0.05f },
            new object[] { 0.08f },
            new object[] { 0.10f },
            new object[] { 0.12f },
            new object[] { 0.15f }
        };

        private static BlockGrid Floor()
        {
            var g = new BlockGrid();
            for (int x = 0; x <= 12; x++)
                for (int z = -6; z <= 6; z++)
                    g.SetBlock(x, 0, z, Slab);
            return g;
        }

        private static LiftRuntime Cabin(float fromY, float toY, float speed)
            => new LiftRuntime(AnchorX, AnchorZ,
                new[] { new LiftBox(BoxCX, BoxY, BoxCZ, BoxHX, BoxHZ, BoxH) },
                new LiftSegment(fromY, toY, Depart, speed));

        private readonly struct Sample
        {
            public readonly uint Tick;
            public readonly float Y, Top, VY;
            public readonly bool Grounded;

            public Sample(uint tick, float y, float top, float vy, bool grounded)
            {
                Tick = tick; Y = y; Top = top; VY = vy; Grounded = grounded;
            }

            public float Gap => Top - Y;
        }

        private static List<Sample> Ride(float fromY, float toY, float speed, int ticks,
            Func<int, BlockMoveInput> inputAt = null, Func<int, int> obstacleTickAt = null)
        {
            var grid = Floor();
            var lift = Cabin(fromY, toY, speed);
            var set = new DynamicObstacleSet();
            var s = new BlockMoverState(PlayerX, fromY + BoxY + BoxH, PlayerZ, grounded: true);
            var track = new List<Sample>(ticks);

            for (int k = 0; k < ticks; k++)
            {
                int obstacleTick = obstacleTickAt != null ? obstacleTickAt(k) : k;
                set.Clear();
                lift.Tick((uint)Math.Max(0, obstacleTick), set);
                var input = inputAt != null ? inputAt(k) : Idle;
                BlockMovementLogic.Step(grid, Shapes, ref s, in input, MovementLogic.StepPerTick, set);
                float top = lift.YAt((uint)k) + BoxY + BoxH;
                track.Add(new Sample((uint)k, s.Y, top, s.VY, s.Grounded));
            }
            return track;
        }

        private static string Saw(List<Sample> track, bool rising)
        {
            float worst = 0f;
            uint at = 0u;
            int reversals = 0;
            for (int i = 1; i < track.Count; i++)
            {
                float d = track[i].Y - track[i - 1].Y;
                float bad = rising ? -d : d;
                if (bad > 1e-4f)
                {
                    reversals++;
                    if (bad > worst) { worst = bad; at = track[i].Tick; }
                }
            }
            return worst <= 0f
                ? "пилы нет"
                : string.Format(CultureInfo.InvariantCulture,
                    "ПИЛА: амплитуда {0:0.####} блока, разворотов {1} за {2} тиков (период ~{3:0.#} тика), худший на тике {4}",
                    worst, reversals, track.Count, reversals > 0 ? track.Count / (float)reversals : 0f, at);
        }

        [Theory]
        [MemberData(nameof(Speeds))]
        public void RisingCabin_PassengerHeightNeverDrops(float speed)
        {
            var track = Ride(1f, 6f, speed, 120);
            string saw = Saw(track, rising: true);
            Assert.True(saw == "пилы нет", $"скорость {speed}: {saw}");
        }

        [Theory]
        [MemberData(nameof(Speeds))]
        public void DescendingCabin_PassengerHeightNeverRises(float speed)
        {
            var track = Ride(6f, 1f, speed, 120);
            string saw = Saw(track, rising: false);
            Assert.True(saw == "пилы нет", $"скорость {speed}: {saw}");
        }

        [Theory]
        [MemberData(nameof(Speeds))]
        public void GapFeetToRoof_StaysConstant_ThroughDepartureAndArrival(float speed)
        {
            var track = Ride(1f, 6f, speed, 120);
            float worst = 0f;
            uint at = 0u;
            foreach (var sample in track)
                if (Math.Abs(sample.Gap) > worst)
                {
                    worst = Math.Abs(sample.Gap);
                    at = sample.Tick;
                }

            Assert.True(worst <= BlockMovementConfig.Epsilon,
                string.Format(CultureInfo.InvariantCulture,
                    "скорость {0}: ноги отрываются от крыши на {1:0.####} блока (тик {2})", speed, worst, at));
        }

        [Theory]
        [MemberData(nameof(Speeds))]
        public void PassengerStaysGrounded_WholeRide(float speed)
        {
            var track = Ride(1f, 6f, speed, 120);
            int airborne = 0;
            uint first = 0u;
            foreach (var sample in track)
                if (!sample.Grounded)
                {
                    if (airborne == 0)
                        first = sample.Tick;
                    airborne++;
                }

            Assert.True(airborne == 0,
                $"скорость {speed}: пассажир теряет опору на {airborne} тиках, первый — {first}");
        }

        // Порог из памяти проекта: прыжок на кабине ломается при ≳4 блока/сек (0.133 бл/тик при 30 Гц).
        // Контентная скорость 3 бл/сек (0.10) — внутри рабочей зоны.
        private const float JumpSafeSpeed = 0.12f;

        [Theory]
        [InlineData(0.05f)]
        [InlineData(0.08f)]
        [InlineData(0.10f)]
        [InlineData(JumpSafeSpeed)]
        public void JumpOnRisingCabin_LandsBackOnIt_UpToTheKnownSpeedLimit(float speed)
        {
            var track = Ride(1f, 8f, speed, 160,
                k => k == 40 ? new BlockMoveInput(IntentDirection.None, jump: true) : Idle);

            var last = track[track.Count - 1];
            Assert.True(last.Y >= last.Top - BlockMovementConfig.Epsilon,
                string.Format(CultureInfo.InvariantCulture,
                    "скорость {0}: после прыжка пассажир провалился — Y {1:0.####}, крыша {2:0.####}",
                    speed, last.Y, last.Top));
        }

        [Fact]
        public void JumpOnCabin_StillBreaksAboveTheKnownLimit_PinnedNotForgotten()
        {
            // Известное ограничение, а не регресс: выше порога прыжок роняет сквозь платформу.
            // Тест держит границу видимой — если её починят, он упадёт и заставит обновить документацию.
            var track = Ride(1f, 8f, 0.15f, 160,
                k => k == 40 ? new BlockMoveInput(IntentDirection.None, jump: true) : Idle);

            var last = track[track.Count - 1];
            Assert.True(last.Y < last.Top - BlockMovementConfig.Epsilon,
                "прыжок на 0.15 бл/тик больше не роняет — порог сдвинулся, обнови документацию и этот тест");
        }

        [Theory]
        [MemberData(nameof(Speeds))]
        public void ObstacleTickOffByOne_ShiftsPassengerByExactlyOneStep(float speed)
        {
            // Замер клиент↔сервер: предиктор гоняет ТОТ ЖЕ Step, но строит набор препятствий по СВОЕМУ тику.
            // Если он разойдётся с серверным на 1, вся дальнейшая траектория пассажира уедет на ход за тик,
            // и реконсиляция вернёт её скачком — это и есть «подскакивает / резко падает».
            var straight = Ride(1f, 6f, speed, 60);
            var offset = Ride(1f, 6f, speed, 60, obstacleTickAt: k => k + 1);

            var a = straight[45];
            var b = offset[45];
            Assert.Equal(speed, b.Y - a.Y, 4);
        }
    }
}
