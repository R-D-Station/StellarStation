using System;
using Client.Lifts;
using Client.Net;
using Shared.Simulation.Blocks;

namespace ServerTests.ClientPure
{
    /// <summary>Поза кабины в момент ОТРИСОВКИ: план детерминирован, поэтому позиция считается точно
    /// для дробного тика, а не приближается сглаживателем (тот держит постоянную ошибку v/k).</summary>
    public class LiftMotionTests
    {
        // Боевые величины: 3 блока/сек при 30 Гц = 0.1 блока за тик, подъём на 5 (этаж) = 50 тиков.
        private const float Bpt = 0.1f;
        private const uint Start = 100u;

        private static LiftSegment Up() => new LiftSegment(5f, 10f, Start, Bpt);

        private static LiftSegment Down() => new LiftSegment(10f, 5f, Start, Bpt);

        [Theory]
        [InlineData(101u)]
        [InlineData(120u)]
        [InlineData(149u)]
        [InlineData(151u)]
        public void OnAWholeTick_ValueMatchesTheDiscreteTrajectory(uint tick)
        {
            // Интерполяция НЕ смещает опорные точки: на целом тике значение то же, что у боевой YAt.
            var up = Up();
            Assert.Equal(LiftTrajectory.YAt(in up, tick), LiftMotion.YAtFraction(in up, tick), 4);

            var down = Down();
            Assert.Equal(LiftTrajectory.YAt(in down, tick), LiftMotion.YAtFraction(in down, tick), 4);
        }

        [Fact]
        public void RenderAnchor_KeepsTodaysSamplePoints()
        {
            // Конвенция часов визуала: renderTick = Tick + alpha, где Tick — последний известный
            // симулированный СЕРВЕРНЫЙ тик. Доля 0 → YAt(tick), доля 1 → YAt(tick+1).
            var s = Up();
            Assert.Equal(LiftTrajectory.YAt(in s, 120u), LiftMotion.VisualY(in s, LiftMotion.RenderTick(120u, 0f)), 4);
            Assert.Equal(LiftTrajectory.YAt(in s, 121u), LiftMotion.VisualY(in s, LiftMotion.RenderTick(120u, 1f)), 4);
        }

        [Fact]
        public void BetweenTicks_IsMonotonic_AndNeverLeavesTheSpan()
        {
            var s = Up();
            float prev = float.NegativeInfinity;
            for (int i = 0; i <= 20; i++)
            {
                float a = i / 20f;
                float y = LiftMotion.VisualY(in s, LiftMotion.RenderTick(120u, a));
                Assert.True(y >= prev, $"движение вверх обязано быть монотонным: {y} < {prev} при доле {a}");
                prev = y;
            }

            Assert.InRange(prev, LiftTrajectory.YAt(in s, 120u), LiftTrajectory.YAt(in s, 121u));
        }

        [Fact]
        public void AcrossManyFrames_TheSequenceIsContinuous_AtTickBoundaries()
        {
            // Конец тика T (доля 1) и начало тика T+1 (доля 0) обязаны совпасть — иначе на каждой границе рывок.
            var s = Up();
            for (uint t = 101u; t < 160u; t++)
                Assert.Equal(LiftMotion.VisualY(in s, LiftMotion.RenderTick(t, 1f)),
                    LiftMotion.VisualY(in s, LiftMotion.RenderTick(t + 1u, 0f)), 5);
        }

        [Fact]
        public void ArrivalIsExact_AndNeverOvershoots()
        {
            // Подъём 5 блоков при 0.1/тик — ровно 50 тиков. После прибытия значение держится, а не проскакивает.
            var s = Up();
            for (int i = 0; i <= 40; i++)
            {
                float y = LiftMotion.YAtFraction(in s, Start + 45f + i * 0.25f);
                Assert.InRange(y, 5f, 10f);
            }
            Assert.Equal(10f, LiftMotion.YAtFraction(in s, Start + 50f), 4);
            Assert.Equal(10f, LiftMotion.YAtFraction(in s, Start + 500f), 4);
        }

        [Fact]
        public void ArrivalHasNoStep_WhenTheCabinStops()
        {
            // Граница Travel→Dwell: соседние доли кадра не должны давать скачок больше хода за тик.
            var s = Up();
            float a = LiftMotion.YAtFraction(in s, Start + 49.9f);
            float b = LiftMotion.YAtFraction(in s, Start + 50.1f);
            Assert.True(b - a <= Bpt, $"скачок на прибытии: {b - a}");
            Assert.Equal(10f, b, 4);
        }

        [Fact]
        public void DescentIsMonotonic_AndClampsAtTheBottom()
        {
            var s = Down();
            float prev = float.PositiveInfinity;
            for (int i = 0; i <= 60; i++)
            {
                float y = LiftMotion.YAtFraction(in s, Start + i);
                Assert.True(y <= prev, "движение вниз обязано быть монотонным");
                prev = y;
            }
            Assert.Equal(5f, prev, 4);
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(-1f)]
        public void NonPositiveSpeed_HoldsTheStart_InsteadOfDividingByIt(float bpt)
        {
            var s = new LiftSegment(5f, 10f, Start, bpt);
            Assert.Equal(5f, LiftMotion.YAtFraction(in s, Start + 100f), 4);
            Assert.Equal(5f, LiftMotion.VisualY(in s, LiftMotion.RenderTick(500u, 0.5f)), 4);
        }

        [Fact]
        public void BeforeTheSegmentStarts_TheCabinHoldsItsOrigin()
        {
            var s = Up();
            Assert.Equal(5f, LiftMotion.YAtFraction(in s, Start - 10f), 4);
            Assert.Equal(5f, LiftMotion.YAtFraction(in s, Start), 4);
            Assert.Equal(5f, LiftMotion.VisualY(in s, LiftMotion.RenderTick(0u, 0.7f)), 4);
        }

        [Theory]
        [InlineData(-0.5f)]
        [InlineData(1.5f)]
        public void OutOfRangeFraction_IsClamped_NotExtrapolated(float alpha)
        {
            var s = Up();
            float y = LiftMotion.VisualY(in s, LiftMotion.RenderTick(120u, alpha));
            Assert.InRange(y, LiftTrajectory.YAt(in s, 120u), LiftTrajectory.YAt(in s, 121u));
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(0.31f)]
        [InlineData(0.5f)]
        [InlineData(0.97f)]
        public void ClockAndMotion_TogetherGiveASmoothRide_AtAnyPhase(float startPhase)
        {
            // СКВОЗНОЙ тест среза: часы визуала + план кабины. Периоды кадра и снапшота взаимно непростые,
            // фаза произвольная — прежняя склейка «PredictedTick + доля» на этом даёт скачок в целый тик.
            const float Interval = 1f / 30f;
            const int FrameStepsPerTick = 3;
            const int SnapshotEveryFrames = 7;

            var s = Up();
            var clock = new RenderClock();
            clock.SyncToServer(Start + 1u);
            clock.Advance(Interval * startPhase, Interval);

            float prev = LiftMotion.VisualY(in s, clock.RenderTick);
            float worstStep = 0f;

            for (int frame = 1; frame <= 400; frame++)
            {
                clock.Advance(Interval / FrameStepsPerTick, Interval);
                if (frame % SnapshotEveryFrames == 0)
                    clock.SyncToServer(Start + 1u + (uint)(frame / FrameStepsPerTick));

                float y = LiftMotion.VisualY(in s, clock.RenderTick);
                float step = y - prev;
                Assert.True(step >= -1e-5f, $"фаза {startPhase}, кадр {frame}: кабина поехала ВНИЗ на подъёме ({step})");
                if (step > worstStep)
                    worstStep = step;
                prev = y;
            }

            Assert.True(worstStep <= Bpt + 1e-4f,
                $"фаза {startPhase}: шаг между кадрами {worstStep} блока — больше хода за тик {Bpt}");
        }

        [Fact]
        public void OneTickOfAnchorWobble_MovesTheCabinByExactlyOneStep()
        {
            // Замер для отчёта: PredictedTick выводится из _serverTickBase + _pending.Count и на Reconcile
            // может дрогнуть на ±1. Дрожь якоря стоит РОВНО ход за тик — при 3 бл/сек это 0.1 блока.
            var s = Up();
            float at120 = LiftMotion.VisualY(in s, LiftMotion.RenderTick(120u, 0.5f));
            float at121 = LiftMotion.VisualY(in s, LiftMotion.RenderTick(121u, 0.5f));

            Assert.Equal(Bpt, at121 - at120, 4);
        }
    }
}
