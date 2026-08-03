using Shared.Simulation.Blocks;

namespace ServerTests.Shared.Simulation.Blocks
{
    /// <summary>Фаза лифта как ЧИСТАЯ функция тика (ни одного скрытого таймера) + SCAN-очередь на битмаске.</summary>
    public class LiftPlanTests
    {
        private static LiftPlan Plan(float fromY, float toY, uint start, float speed, uint dwell, uint lead)
        {
            var segment = new LiftSegment(fromY, toY, start, speed);
            return new LiftPlan
            {
                Segment = segment,
                DwellUntilTick = LiftPhase.DwellUntil(in segment, dwell, lead)
            };
        }

        [Fact]
        public void Phases_FollowTravelDwellClosingReady()
        {
            var p = Plan(0f, 4f, 0u, 1f, dwell: 10u, lead: 3u);
            uint arrival = LiftTrajectory.ArrivalTick(in p.Segment);
            Assert.Equal(4u, arrival);

            Assert.Equal(LiftPhaseKind.Travel, LiftPhase.At(in p, 0u, 3u));
            Assert.Equal(LiftPhaseKind.Travel, LiftPhase.At(in p, arrival - 1, 3u));
            Assert.Equal(LiftPhaseKind.Dwell, LiftPhase.At(in p, arrival, 3u));
            Assert.Equal(LiftPhaseKind.Dwell, LiftPhase.At(in p, p.DwellUntilTick - 4u, 3u));
            Assert.Equal(LiftPhaseKind.Closing, LiftPhase.At(in p, p.DwellUntilTick - 3u, 3u));
            Assert.Equal(LiftPhaseKind.Closing, LiftPhase.At(in p, p.DwellUntilTick - 1u, 3u));
            Assert.Equal(LiftPhaseKind.Ready, LiftPhase.At(in p, p.DwellUntilTick, 3u));
            Assert.Equal(LiftPhaseKind.Ready, LiftPhase.At(in p, p.DwellUntilTick + 500u, 3u));
        }

        [Fact]
        public void Phase_IsPureFunctionOfTick_NoHiddenState()
        {
            var p = Plan(0f, 4f, 0u, 1f, 10u, 3u);
            for (uint t = 0; t < 40; t++)
                Assert.Equal(LiftPhase.At(in p, t, 3u), LiftPhase.At(in p, t, 3u));

            for (uint t = 39; t > 0; t--)
                Assert.Equal(LiftPhase.At(in p, t, 3u), LiftPhase.At(in p, t, 3u));
        }

        [Fact]
        public void DwellAndClosingWindows_HaveExactlyTheConfiguredLength()
        {
            const uint Dwell = 7u, Lead = 11u;
            var p = Plan(0f, 1f, 0u, 1f, Dwell, Lead);
            uint arrival = LiftTrajectory.ArrivalTick(in p.Segment);

            int dwellTicks = 0, closingTicks = 0;
            for (uint t = arrival; t < p.DwellUntilTick; t++)
            {
                var phase = LiftPhase.At(in p, t, Lead);
                if (phase == LiftPhaseKind.Dwell) dwellTicks++;
                else if (phase == LiftPhaseKind.Closing) closingTicks++;
                else Assert.Fail($"между прибытием и Ready не может быть {phase} (тик {t})");
            }

            Assert.Equal((int)Dwell, dwellTicks);
            Assert.Equal((int)Lead, closingTicks);
        }

        [Fact]
        public void Direction_MatchesSegment()
        {
            Assert.Equal(1, LiftPhase.Direction(new LiftSegment(0f, 5f, 0u, 1f)));
            Assert.Equal(-1, LiftPhase.Direction(new LiftSegment(5f, 0f, 0u, 1f)));
            Assert.Equal(0, LiftPhase.Direction(new LiftSegment(2f, 2f, 0u, 1f)));
        }

        [Fact]
        public void Queue_BitsRoundTrip_AndIgnoreOutOfRange()
        {
            uint calls = 0u;
            calls = LiftScanQueue.Add(calls, 0);
            calls = LiftScanQueue.Add(calls, 31);
            Assert.True(LiftScanQueue.Has(calls, 0));
            Assert.True(LiftScanQueue.Has(calls, 31));

            Assert.Equal(calls, LiftScanQueue.Add(calls, 32));
            Assert.Equal(calls, LiftScanQueue.Add(calls, -1));
            Assert.False(LiftScanQueue.Has(calls, 32));
            Assert.False(LiftScanQueue.Has(calls, -1));

            calls = LiftScanQueue.Clear(calls, 0);
            Assert.False(LiftScanQueue.Has(calls, 0));
            Assert.True(LiftScanQueue.Has(calls, 31));
        }

        [Fact]
        public void Next_NoCalls_ReturnsMinusOne()
            => Assert.Equal(-1, LiftScanQueue.Next(0u, 0, 1, 5));

        [Fact]
        public void Next_CallOnCurrentFloor_WinsOverEverything()
        {
            uint calls = LiftScanQueue.Add(LiftScanQueue.Add(0u, 2), 4);
            Assert.Equal(2, LiftScanQueue.Next(calls, 2, 1, 5));
        }

        [Fact]
        public void Next_PrefersSameDirection_ThenReverses()
        {
            uint calls = LiftScanQueue.Add(LiftScanQueue.Add(0u, 0), 4);

            Assert.Equal(4, LiftScanQueue.Next(calls, 2, 1, 5));
            Assert.Equal(0, LiftScanQueue.Next(calls, 2, -1, 5));

            uint onlyBelow = LiftScanQueue.Add(0u, 0);
            Assert.Equal(0, LiftScanQueue.Next(onlyBelow, 2, 1, 5));
        }

        [Fact]
        public void Next_IgnoresFloorsBeyondStopCount()
        {
            uint calls = LiftScanQueue.Add(0u, 7);
            Assert.Equal(-1, LiftScanQueue.Next(calls, 0, 1, 5));
        }

        [Fact]
        public void CanStillStop_RejectsFloorThatWouldBeOvershotThisTick()
        {
            Assert.True(LiftScanQueue.CanStillStop(0f, 5f, 1, 1f));
            Assert.True(LiftScanQueue.CanStillStop(4f, 5f, 1, 1f));
            Assert.False(LiftScanQueue.CanStillStop(4.5f, 5f, 1, 1f), "за тик проедет мимо — тормозить поздно");
            Assert.False(LiftScanQueue.CanStillStop(5f, 5f, 1, 1f));

            Assert.True(LiftScanQueue.CanStillStop(5f, 0f, -1, 1f));
            Assert.False(LiftScanQueue.CanStillStop(0.5f, 0f, -1, 1f));
            Assert.False(LiftScanQueue.CanStillStop(0f, 5f, 1, 0f), "нулевая скорость — остановиться нельзя");
        }

        [Fact]
        public void NextEnRoute_PicksNearestCallOnTheWay()
        {
            var stopY = new[] { 0f, 3f, 6f, 9f };
            uint calls = LiftScanQueue.Add(LiftScanQueue.Add(0u, 1), 2);

            Assert.Equal(1, LiftScanQueue.NextEnRoute(calls, 0f, stopY, 1, 3, 0.5f));
        }

        [Fact]
        public void NextEnRoute_SkipsFloorsBehindAndBeyondTarget()
        {
            var stopY = new[] { 0f, 3f, 6f, 9f };

            uint behind = LiftScanQueue.Add(0u, 0);
            Assert.Equal(-1, LiftScanQueue.NextEnRoute(behind, 4f, stopY, 1, 3, 0.5f));

            uint beyond = LiftScanQueue.Add(0u, 3);
            Assert.Equal(-1, LiftScanQueue.NextEnRoute(beyond, 0f, stopY, 1, 2, 0.5f));
        }

        [Fact]
        public void NextEnRoute_DownwardPicksHighestOnTheWay()
        {
            var stopY = new[] { 0f, 3f, 6f, 9f };
            uint calls = LiftScanQueue.Add(LiftScanQueue.Add(0u, 1), 2);

            Assert.Equal(2, LiftScanQueue.NextEnRoute(calls, 9f, stopY, -1, 0, 0.5f));
        }

        [Fact]
        public void NextEnRoute_NoDirection_ReturnsMinusOne()
        {
            var stopY = new[] { 0f, 3f };
            Assert.Equal(-1, LiftScanQueue.NextEnRoute(LiftScanQueue.Add(0u, 1), 0f, stopY, 0, 1, 0.5f));
        }
    }
}
