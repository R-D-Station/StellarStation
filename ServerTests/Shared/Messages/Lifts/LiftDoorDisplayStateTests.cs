using Shared.Messages.Lifts;
using Shared.Simulation.Blocks;

namespace ServerTests.Shared.Messages.Lifts
{
    /// <summary>Состояние дисплея шахтной двери: панели показывают ОБЩЕЕ состояние лифта,
    /// а кнопка — вызов СВОЕГО этажа. Спутать эти два этажа — самая вероятная ошибка среза.</summary>
    public class LiftDoorDisplayStateTests
    {
        // Асимметрия по всем осям: этажи 0/2/5 разрежены, шаги высот 7 и 12 разные,
        // номер этажа нигде не совпадает ни с индексом в массиве, ни с высотой.
        private static LiftStopEntry[] Stops() => new[]
        {
            new LiftStopEntry(0, 0f, 11, 0, 4),
            new LiftStopEntry(2, 7f, 11, 7, 4),
            new LiftStopEntry(5, 19f, 11, 19, 4)
        };

        private static LiftSegment Up() => new LiftSegment(0f, 19f, 100u, 0.5f);

        private static LiftSegment Down() => new LiftSegment(19f, 0f, 100u, 0.5f);

        [Fact]
        public void CabinLabel_IsOrdinal_NotRailIndex()
        {
            // Этажи 0/2/5: верхний внутри 5, а подписан «3» — третий обслуживаемый.
            var s = LiftDoorDisplayState.ForLift(Stops(), 19f, LiftPhaseKind.Ready, Down(), hasPlan: true);
            Assert.True(s.Known);
            Assert.Equal(2, s.CabinLabelIndex);
            Assert.Equal("3", LiftDisplayText.Floor(s.CabinLabelIndex));
        }

        [Theory]
        [InlineData(0f, 0)]
        [InlineData(6.9f, 0)]
        [InlineData(7f, 1)]
        [InlineData(18.9f, 1)]
        [InlineData(19f, 2)]
        [InlineData(25f, 2)]
        public void CabinLabel_HoldsTheFloorLeft_UntilTheNextIsReached(float cabinY, int expected)
        {
            var s = LiftDoorDisplayState.ForLift(Stops(), cabinY, LiftPhaseKind.Ready, Down(), hasPlan: true);
            Assert.Equal(expected, s.CabinLabelIndex);
        }

        [Fact]
        public void Direction_ShowsOnlyWhileTravelling()
        {
            Assert.Equal(1, LiftDoorDisplayState.ForLift(Stops(), 0f, LiftPhaseKind.Travel, Up(), true).Direction);
            Assert.Equal(-1, LiftDoorDisplayState.ForLift(Stops(), 19f, LiftPhaseKind.Travel, Down(), true).Direction);
        }

        [Theory]
        [InlineData(LiftPhaseKind.Ready)]
        [InlineData(LiftPhaseKind.Dwell)]
        [InlineData(LiftPhaseKind.Closing)]
        public void Direction_IsZero_WhenCabinStands_EvenWithStaleSegment(LiftPhaseKind phase)
        {
            // Сегмент остаётся «вверх» и после прибытия — висящая стрелка врала бы.
            Assert.Equal(0, LiftDoorDisplayState.ForLift(Stops(), 19f, phase, Up(), true).Direction);
        }

        [Fact]
        public void Called_IsBitOfDoorFloor_NotCabinFloor()
        {
            // Кабина на ВНУТРЕННЕМ 5-м (подпись «3»), вызов висит на внутреннем 0-м: дверь 0-го горит,
            // дверь 5-го — нет. Бит вызова адресуется ВНУТРЕННИМ индексом, подпись его не задевает.
            var lift = LiftDoorDisplayState.ForLift(Stops(), 19f, LiftPhaseKind.Ready, Down(), true);
            Assert.Equal(2, lift.CabinLabelIndex);

            uint calls = LiftScanQueue.Add(0u, 0);
            Assert.True(lift.WithCall(calls, 0).Called);
            Assert.False(lift.WithCall(calls, 5).Called);
            Assert.False(lift.WithCall(calls, 2).Called);
        }

        [Fact]
        public void Called_DoesNotDisturbTheSharedPart()
        {
            var lift = LiftDoorDisplayState.ForLift(Stops(), 0f, LiftPhaseKind.Travel, Up(), true);
            var door = lift.WithCall(LiftScanQueue.Add(0u, 2), 2);

            Assert.Equal(lift.CabinLabelIndex, door.CabinLabelIndex);
            Assert.Equal(lift.Direction, door.Direction);
            Assert.True(door.Known);
            Assert.True(door.Called);
        }

        [Fact]
        public void WithoutPlan_StateIsUnknown_AndNeverLightsUp()
        {
            var s = LiftDoorDisplayState.ForLift(Stops(), 0f, LiftPhaseKind.Travel, Up(), hasPlan: false);
            Assert.False(s.Known);
            Assert.Equal(0, s.Direction);
            // Пока плана нет, кабина не локализована — гореть кнопке не от чего.
            Assert.False(s.WithCall(LiftScanQueue.Add(0u, 0), 0).Called);
        }

        [Fact]
        public void EmptyStopTable_IsUnknown()
        {
            Assert.False(LiftDoorDisplayState.ForLift(null, 0f, LiftPhaseKind.Ready, Up(), true).Known);
            Assert.False(LiftDoorDisplayState.ForLift(new LiftStopEntry[0], 0f, LiftPhaseKind.Ready, Up(), true).Known);
        }

        [Fact]
        public void Equality_SeesEveryField()
        {
            var a = new LiftDoorDisplayState(5, 1, true, true);
            Assert.True(a.Equals(new LiftDoorDisplayState(5, 1, true, true)));
            Assert.False(a.Equals(new LiftDoorDisplayState(4, 1, true, true)));
            Assert.False(a.Equals(new LiftDoorDisplayState(5, -1, true, true)));
            Assert.False(a.Equals(new LiftDoorDisplayState(5, 1, false, true)));
            Assert.False(a.Equals(new LiftDoorDisplayState(5, 1, true, false)));
        }

        [Fact]
        public void FloorText_IsOneBased_AndPrecomputed()
        {
            Assert.Equal("1", LiftDisplayText.Floor(0));
            Assert.Equal("6", LiftDisplayText.Floor(5));
            Assert.Equal("32", LiftDisplayText.Floor(31));
            Assert.Same(LiftDisplayText.Floor(4), LiftDisplayText.Floor(4));
        }

        [Fact]
        public void FloorText_OutOfRange_IsEmpty_NotCrash()
        {
            Assert.Equal(string.Empty, LiftDisplayText.Floor(-1));
            Assert.Equal(string.Empty, LiftDisplayText.Floor(LiftDisplayText.MaxFloors));
            Assert.Equal(string.Empty, LiftDisplayText.Floor(9999));
        }
    }
}
