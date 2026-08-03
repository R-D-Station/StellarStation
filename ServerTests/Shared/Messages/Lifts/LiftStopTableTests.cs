using Shared.Messages.Lifts;

namespace ServerTests.Shared.Messages.Lifts
{
    /// <summary>Таблица остановок: номер ЭТАЖА != индекс в массиве (обслуживаются не все этажи).</summary>
    public class LiftStopTableTests
    {
        // Фикстура АСИММЕТРИЧНА: этажи 0/2/5 (разрежены и НЕ равны индексам 0/1/2), высоты неравномерны
        // (шаг 6.5 и 9.25), ни одно значение не совпадает с другим. Ровный шаг 0/1/2 спрятал бы подмену
        // «этаж ↔ индекс» — этот трек уже четырежды ловил слепые фикстуры.
        private static readonly LiftStopEntry[] Stops =
        {
            LiftStopEntry.WithoutDoor(0, 1.5f),
            new LiftStopEntry(2, 8f, 11, 3, 47),
            new LiftStopEntry(5, 17.25f, -9, 21, 4)
        };

        [Theory]
        [InlineData(1.5f, 0)]
        [InlineData(0f, 0)]
        [InlineData(4f, 0)]
        [InlineData(6f, 2)]
        [InlineData(8f, 2)]
        [InlineData(12f, 2)]
        [InlineData(17.25f, 5)]
        [InlineData(100f, 5)]
        public void NearestFloor_ReturnsFloorNumber_NotArrayIndex(float y, int expectedFloor)
            => Assert.Equal(expectedFloor, LiftStopTable.NearestFloor(Stops, y));

        [Fact]
        public void NearestIndex_AndNearestFloor_DisagreeByDesign()
        {
            // Ровно та подмена, которую ловит разреженная фикстура: индекс 2 против этажа 5.
            Assert.Equal(2, LiftStopTable.NearestIndex(Stops, 17.25f));
            Assert.Equal(5, LiftStopTable.NearestFloor(Stops, 17.25f));
        }

        [Fact]
        public void IndexOfFloor_MapsFloorToArraySlot()
        {
            Assert.Equal(0, LiftStopTable.IndexOfFloor(Stops, 0));
            Assert.Equal(1, LiftStopTable.IndexOfFloor(Stops, 2));
            Assert.Equal(2, LiftStopTable.IndexOfFloor(Stops, 5));
        }

        [Fact]
        public void UnservedFloors_AreNotFound()
        {
            Assert.Equal(-1, LiftStopTable.IndexOfFloor(Stops, 1));
            Assert.Equal(-1, LiftStopTable.IndexOfFloor(Stops, 3));
            Assert.Equal(-1, LiftStopTable.IndexOfFloor(Stops, 4));
            Assert.False(LiftStopTable.IsServed(Stops, 1));
            Assert.True(LiftStopTable.IsServed(Stops, 2));
        }

        [Fact]
        public void EmptyOrNull_IsSafe()
        {
            Assert.Equal(-1, LiftStopTable.NearestIndex(null, 5f));
            Assert.Equal(-1, LiftStopTable.NearestFloor(null, 5f));
            Assert.Equal(-1, LiftStopTable.NearestIndex(System.Array.Empty<LiftStopEntry>(), 5f));
            Assert.Equal(-1, LiftStopTable.IndexOfFloor(null, 0));
            Assert.False(LiftStopTable.IsServed(null, 0));
        }

        [Fact]
        public void TieBreak_IsDeterministic_FirstWins()
        {
            var pair = new[] { LiftStopEntry.WithoutDoor(3, 0f), LiftStopEntry.WithoutDoor(7, 10f) };
            Assert.Equal(3, LiftStopTable.NearestFloor(pair, 5f));
        }
    }
}
