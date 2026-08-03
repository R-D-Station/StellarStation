using System;
using System.Collections.Generic;
using Client.Net;

namespace ServerTests.ClientPure
{
    /// <summary>Часы ВИЗУАЛА: единственный источник времени для отрисовки. Прежняя склейка «целая часть
    /// из предиктора + доля из локального аккумулятора» давала скачок на целый тик, потому что целая часть
    /// двигалась на снапшоте, а доля — на локальном тике.</summary>
    public class RenderClockTests
    {
        private const float Interval = 1f / 30f;

        // ⚠ Периоды ВЗАИМНО НЕПРОСТЫЕ: на сетке «снапшот = локальный тик» дефект не наблюдается,
        // потому что оба источника двигаются вместе. Шаг кадра 1/3 тика, снапшот раз в 7 шагов.
        private const int FrameStepsPerTick = 3;
        private const int SnapshotEveryFrames = 7;

        private static RenderClock Seeded(uint serverTick = 100u)
        {
            var c = new RenderClock();
            c.SyncToServer(serverTick);
            return c;
        }

        [Fact]
        public void FirstSnapshot_SeedsToServerTickMinusOne()
        {
            var c = Seeded(100u);

            Assert.True(c.Seeded);
            Assert.Equal(99u, c.Tick);
            Assert.Equal(0f, c.Alpha, 5);
            Assert.Equal(99f, c.RenderTick, 5);
        }

        [Fact]
        public void SnapshotWithoutLocalTick_DoesNotJumpAWholeTick()
        {
            // (а) Пришёл снапшот, локального тика не было. Старая схема двигала ЦЕЛУЮ часть — рывок на 0.1 блока.
            var c = Seeded(100u);
            c.Advance(Interval * 0.4f, Interval);
            float before = c.RenderTick;

            c.SyncToServer(101u);
            c.SyncToServer(102u);

            Assert.Equal(before, c.RenderTick, 5);
        }

        [Fact]
        public void LocalTickWithoutSnapshot_GrowsByExactlyOne()
        {
            // (б) Прошёл локальный тик без снапшота — часы обязаны вырасти ровно на 1.0.
            var c = Seeded(100u);
            float before = c.RenderTick;

            c.Advance(Interval, Interval);

            Assert.Equal(before + 1f, c.RenderTick, 4);
        }

        [Fact]
        public void ClockRuns_EvenWhenNoInputIsSent()
        {
            // Ровно баг: игрок едет СТОЯ, _pending пуст, предиктор молчит — часы всё равно обязаны идти.
            var c = Seeded(100u);
            for (int i = 0; i < 90; i++)
                c.Advance(Interval, Interval);

            Assert.Equal(99f + 90f, c.RenderTick, 3);
        }

        [Theory]
        [InlineData(1e-3f)]
        [InlineData(1f - 1e-3f)]
        public void AtTheTickEdge_SnapshotDoesNotTearTheClock(float alpha)
        {
            // ⚠ Проба У КРОМКИ: разрыв старой схемы жил ровно на границе тика, в середине маскировался.
            var c = Seeded(100u);
            c.Advance(Interval * alpha, Interval);
            float before = c.RenderTick;

            c.SyncToServer(101u);

            Assert.True(Math.Abs(c.RenderTick - before) < 1e-4f,
                $"на доле {alpha} снапшот сдвинул часы на {c.RenderTick - before}");
        }

        [Fact]
        public void CoprimeSnapshotAndFrameRates_KeepTheClockMonotonic()
        {
            // Серверный тик выводится ИЗ ЧИСЛА КАДРОВ (frame / FrameStepsPerTick), а не приращением на
            // целое за снапшот: 7/3 не делится нацело, и приращение уводило бы серверные часы медленнее
            // локальных — это был бы дефект фикстуры, а не часов.
            var c = Seeded(100u);
            var track = new List<float>();

            for (int frame = 1; frame <= 600; frame++)
            {
                c.Advance(Interval / FrameStepsPerTick, Interval);
                if (frame % SnapshotEveryFrames == 0)
                    c.SyncToServer(100u + (uint)(frame / FrameStepsPerTick));
                track.Add(c.RenderTick);
            }

            for (int i = 1; i < track.Count; i++)
                Assert.True(track[i] >= track[i - 1],
                    $"кадр {i}: часы пошли назад ({track[i - 1]} → {track[i]})");
        }

        [Fact]
        public void CoprimeRates_NeverStepMoreThanOneTickPerFrame()
        {
            var c = Seeded(100u);
            float prev = c.RenderTick;
            float worst = 0f;

            for (int frame = 1; frame <= 600; frame++)
            {
                c.Advance(Interval / FrameStepsPerTick, Interval);
                if (frame % SnapshotEveryFrames == 0)
                    c.SyncToServer(100u + (uint)(frame / FrameStepsPerTick));
                float step = c.RenderTick - prev;
                if (step > worst)
                    worst = step;
                prev = c.RenderTick;
            }

            Assert.True(worst <= 1f, $"худший шаг за кадр {worst} тика — коррекция дала скачок больше тика");
        }

        [Fact]
        public void SmallDrift_IsAbsorbedGradually_NotInOneStep()
        {
            // Сид 100 → Tick 99 (serverTick−1). Снапшот 102 целится в 101, то есть расхождение 2 тика.
            var c = Seeded(100u);
            c.SyncToServer(102u);

            Assert.Equal(2f, c.PendingCorrection, 4);

            float prev = c.RenderTick;
            for (int i = 0; i < 3; i++)
            {
                c.Advance(Interval, Interval);
                float step = c.RenderTick - prev;
                Assert.True(step <= 1f + RenderClock.MaxCorrectionRate + 1e-4f,
                    $"коррекция дала шаг {step} тика за один тик");
                prev = c.RenderTick;
            }

            Assert.True(c.PendingCorrection < 2f, "коррекция не расходуется");
            Assert.True(c.PendingCorrection > 0f, "коррекция съедена за раз — это и есть скачок");
        }

        [Fact]
        public void LargeDrift_Reseeds_InsteadOfCrawling()
        {
            var c = Seeded(100u);
            c.SyncToServer(100u + (uint)RenderClock.MaxDriftTicks + 10u);

            Assert.Equal(100u + (uint)RenderClock.MaxDriftTicks + 9u, c.Tick);
            Assert.Equal(0f, c.PendingCorrection, 5);
        }

        [Fact]
        public void NegativeCorrection_SlowsDown_ButNeverRewinds()
        {
            var c = Seeded(200u);
            c.SyncToServer(198u);

            float prev = c.RenderTick;
            for (int i = 0; i < 120; i++)
            {
                c.Advance(Interval, Interval);
                Assert.True(c.RenderTick >= prev, "отрицательная коррекция обязана тормозить, а не откатывать");
                prev = c.RenderTick;
            }
        }

        [Fact]
        public void ZeroOrNegativeDelta_IsIgnored()
        {
            var c = Seeded(100u);
            float before = c.RenderTick;

            c.Advance(0f, Interval);
            c.Advance(-1f, Interval);
            c.Advance(Interval, 0f);

            Assert.Equal(before, c.RenderTick, 5);
        }

        [Fact]
        public void AlphaStaysInsideTheTick()
        {
            var c = Seeded(100u);
            for (int i = 0; i < 500; i++)
            {
                c.Advance(Interval * 0.37f, Interval);
                Assert.InRange(c.Alpha, 0f, 1f);
            }
        }
    }
}
