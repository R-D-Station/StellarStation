using Shared.Messages.Lifts;
using Shared.Simulation.Blocks;

namespace ServerTests.Shared.Messages.Lifts
{
    /// <summary>Подпись этажа — ПОРЯДКОВЫЙ номер среди обслуживаемых, а не индекс модуля рельса.
    /// Внутренняя нумерация (Request/Calls/двери) остаётся на индексе рельса и здесь не меняется.</summary>
    public class LiftFloorLabelTests
    {
        // Шахта пользователя: рельсы на Y 0/5/10/15 (внутренние этажи 0..3), двери ТОЛЬКО на 0 и 3.
        // ДЫРКА в середине обязательна: на сплошной шахте ordinal совпадает с индексом и подмену не поймать.
        private static LiftStopEntry[] Sparse() => new[]
        {
            new LiftStopEntry(0, 0f, 11, 0, 4),
            new LiftStopEntry(3, 15f, 11, 15, 4)
        };

        private static LiftStopEntry[] Dense() => new[]
        {
            new LiftStopEntry(0, 0f, 11, 0, 4),
            new LiftStopEntry(1, 5f, 11, 5, 4),
            new LiftStopEntry(2, 10f, 11, 10, 4)
        };

        [Fact]
        public void SparseShaft_LabelsAreOrdinals_NotRailIndices()
        {
            var stops = Sparse();

            Assert.Equal(0, LiftStopTable.DisplayIndex(stops, 0));
            Assert.Equal(1, LiftStopTable.DisplayIndex(stops, 3));

            Assert.Equal("1", LiftDisplayText.Floor(LiftStopTable.DisplayIndex(stops, 0)));
            Assert.Equal("2", LiftDisplayText.Floor(LiftStopTable.DisplayIndex(stops, 3)));
        }

        [Fact]
        public void InternalNumbering_IsUntouched_ByLabelling()
        {
            // Подпись «2» у этажа, который внутри остаётся 3: Request/Calls/привязка двери адресуются 3.
            var stops = Sparse();
            Assert.Equal(3, stops[1].Floor);
            Assert.Equal(1, LiftStopTable.DisplayIndex(stops, stops[1].Floor));
            Assert.True(LiftStopTable.IsServed(stops, 3));
            Assert.False(LiftStopTable.IsServed(stops, 1));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void DisplayIndex_OfAServedFloor_IsItsPositionInTheTable(int position)
        {
            var stops = Dense();
            Assert.Equal(position, LiftStopTable.DisplayIndex(stops, stops[position].Floor));
        }

        [Fact]
        public void UnservedFloor_FallsBackToNearestServedBelow()
        {
            // Кабина между остановками (внутренние 1 и 2 — рельс есть, двери нет): порядкового номера
            // у них нет, показываем этаж, с которого кабина уехала.
            var stops = Sparse();
            Assert.Equal(0, LiftStopTable.DisplayIndex(stops, 1));
            Assert.Equal(0, LiftStopTable.DisplayIndex(stops, 2));
        }

        [Fact]
        public void FloorBelowTheLowestServed_ClampsToFirst_NotToMinusOne()
        {
            var stops = new[] { new LiftStopEntry(2, 10f, 1, 10, 1), new LiftStopEntry(5, 25f, 1, 25, 1) };
            Assert.Equal(0, LiftStopTable.DisplayIndex(stops, 0));
            Assert.Equal("1", LiftDisplayText.Floor(LiftStopTable.DisplayIndex(stops, 0)));
        }

        [Fact]
        public void EmptyTable_GivesNoLabel()
        {
            Assert.Equal(-1, LiftStopTable.DisplayIndex(null, 3));
            Assert.Equal(-1, LiftStopTable.DisplayIndex(new LiftStopEntry[0], 3));
            Assert.Equal(string.Empty, LiftDisplayText.Floor(-1));
        }

        [Theory]
        [InlineData(0f, 0)]
        [InlineData(4.9f, 0)]
        [InlineData(10f, 0)]
        [InlineData(14.9f, 0)]
        [InlineData(15f, 3)]
        [InlineData(20f, 3)]
        public void FloorAtOrBelow_NeverReportsAFloorNotReachedYet(float cabinY, int expected)
        {
            // NearestFloor снапится по РАССТОЯНИЮ: на Y=10 он дал бы этаж 3 (ближе), то есть подпись «2»
            // на середине пути вверх. Подписи нужен этаж, с которого кабина уехала.
            Assert.Equal(expected, LiftStopTable.FloorAtOrBelow(Sparse(), cabinY));
        }

        [Fact]
        public void FloorAtOrBelow_DiffersFromNearestFloor_Midway()
        {
            var stops = Sparse();
            Assert.Equal(3, LiftStopTable.NearestFloor(stops, 10f));
            Assert.Equal(0, LiftStopTable.FloorAtOrBelow(stops, 10f));
        }

        [Fact]
        public void FloorAtOrBelow_BelowTheShaft_ClampsToLowest()
        {
            var stops = new[] { new LiftStopEntry(2, 10f, 1, 10, 1), new LiftStopEntry(5, 25f, 1, 25, 1) };
            Assert.Equal(2, LiftStopTable.FloorAtOrBelow(stops, 0f));
        }

        [Fact]
        public void CabinBetweenStops_ShowsTheFloorItLeft()
        {
            var stops = Sparse();
            var state = LiftDoorDisplayState.ForLift(stops, 10f, LiftPhaseKind.Travel,
                new LiftSegment(0f, 15f, 0u, 0.5f), hasPlan: true);

            Assert.Equal(0, state.CabinLabelIndex);
            Assert.Equal("1", LiftDisplayText.Floor(state.CabinLabelIndex));
            Assert.Equal(1, state.Direction);
        }

        [Fact]
        public void CabinAtTopStop_ShowsSecond_NotFourth()
        {
            var stops = Sparse();
            var state = LiftDoorDisplayState.ForLift(stops, 15f, LiftPhaseKind.Ready,
                new LiftSegment(15f, 15f, 0u, 0.5f), hasPlan: true);

            Assert.Equal(1, state.CabinLabelIndex);
            Assert.Equal("2", LiftDisplayText.Floor(state.CabinLabelIndex));
        }
    }
}
