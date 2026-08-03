using Client.Lifts;
using Shared.Messages.Lifts;
using Shared.Simulation.Blocks;
using Shared.World.Blocks;

namespace ServerTests.ClientPure
{
    /// <summary>Створки кабины зеркалят АВТОРИТЕТНЫЙ бит Open шахтной двери. Источник — только бит из грида:
    /// фаза «стоим на этаже» бывает и при закрытой двери (сервер пропускает этаж после MaxOpenAttempts).</summary>
    public class LiftCabinDoorTests
    {
        private const float Eps = LiftCabinDoorState.DefaultEps;

        // ⚠ Шаг этажей НЕРАВНОМЕРНЫЙ, и координаты двери у каждой остановки разные по всем трём осям,
        // с разными знаками и модулями: на равномерной сетке с одинаковыми дверями ни перестановка
        // DoorX/DoorZ, ни сдвиг индекса этажа не наблюдаются.
        private static LiftStopEntry[] Stops() => new[]
        {
            new LiftStopEntry(0, 5.00f, 7, 3, -11),
            new LiftStopEntry(2, 8.50f, -2, 9, 41),
            new LiftStopEntry(5, 14.25f, 23, -6, 4)
        };

        private static LiftStopEntry[] WithDoorlessMiddle() => new[]
        {
            new LiftStopEntry(0, 5.00f, 7, 3, -11),
            LiftStopEntry.WithoutDoor(2, 8.50f),
            new LiftStopEntry(5, 14.25f, 23, -6, 4)
        };

        [Theory]
        [InlineData(5.00f, 0, 7, 3, -11)]
        [InlineData(8.50f, 2, -2, 9, 41)]
        [InlineData(14.25f, 5, 23, -6, 4)]
        public void StopAtY_ReturnsTheDoorOfThatFloor_ComponentByComponent(
            float cabinY, int floor, int doorX, int doorY, int doorZ)
        {
            // Поиск идёт по Y, а возвращается клетка (X,Y,Z) — утверждаем ПОКОМПОНЕНТНО, иначе
            // «нашлась соседняя остановка» пройдёт незамеченным.
            Assert.True(LiftCabinDoorState.TryStopAtY(Stops(), cabinY, Eps, out var stop));

            Assert.Equal(floor, stop.Floor);
            Assert.Equal(doorX, stop.DoorX);
            Assert.Equal(doorY, stop.DoorY);
            Assert.Equal(doorZ, stop.DoorZ);
        }

        [Fact]
        public void DoorCoordinates_DifferOnEveryAxis_SoASwapIsObservable()
        {
            var stops = Stops();
            foreach (var s in stops)
            {
                Assert.NotEqual(s.DoorX, s.DoorZ);
                Assert.NotEqual(s.DoorX, s.DoorY);
                Assert.NotEqual(s.DoorY, s.DoorZ);
            }
        }

        [Theory]
        [InlineData(5.00f - (Eps - 0.001f), 0)]
        [InlineData(5.00f + (Eps - 0.001f), 0)]
        [InlineData(8.50f - (Eps - 0.001f), 2)]
        [InlineData(14.25f + (Eps - 0.001f), 5)]
        public void JustInsideTheTolerance_TheStopIsStillFound(float cabinY, int floor)
        {
            Assert.True(LiftCabinDoorState.TryStopAtY(Stops(), cabinY, Eps, out var stop));
            Assert.Equal(floor, stop.Floor);
        }

        [Theory]
        [InlineData(5.00f - (Eps + 0.001f))]
        [InlineData(5.00f + (Eps + 0.001f))]
        [InlineData(8.50f + (Eps + 0.001f))]
        [InlineData(14.25f - (Eps + 0.001f))]
        public void JustOutsideTheTolerance_NoStopIsFound(float cabinY)
        {
            Assert.False(LiftCabinDoorState.TryStopAtY(Stops(), cabinY, Eps, out _));
        }

        [Theory]
        [InlineData(6.75f)]
        [InlineData(11.375f)]
        [InlineData(0f)]
        [InlineData(100f)]
        public void BetweenFloors_NoStopIsFound(float cabinY)
        {
            Assert.False(LiftCabinDoorState.TryStopAtY(Stops(), cabinY, Eps, out _));
        }

        [Fact]
        public void FloorWithoutADoor_IsNotAStopForTheCabinDoors()
        {
            // Техуровень: кабина там стоит, но створкам нечего зеркалить.
            Assert.False(LiftCabinDoorState.TryStopAtY(WithDoorlessMiddle(), 8.50f, Eps, out _));
            Assert.True(LiftCabinDoorState.TryStopAtY(WithDoorlessMiddle(), 5.00f, Eps, out var low));
            Assert.Equal(0, low.Floor);
        }

        [Fact]
        public void EmptyTable_IsSafe()
        {
            Assert.False(LiftCabinDoorState.TryStopAtY(null, 5f, Eps, out _));
            Assert.False(LiftCabinDoorState.TryStopAtY(new LiftStopEntry[0], 5f, Eps, out _));
        }

        [Theory]
        [InlineData(LiftPhaseKind.Ready)]
        [InlineData(LiftPhaseKind.Dwell)]
        [InlineData(LiftPhaseKind.Closing)]
        public void StoppedAtAnOpenDoor_TheCabinOpens(LiftPhaseKind phase)
        {
            Assert.True(LiftCabinDoorState.ShouldOpen(phase, stopFound: true, doorOpenBit: true));
        }

        [Fact]
        public void InTransit_TheCabinNeverOpens_EvenOverAnOpenDoor()
        {
            // Гейт транзита: без него створки мигнут на пролёте этажа, где дверь открыта для другого лифта
            // или ещё не закрылась.
            Assert.False(LiftCabinDoorState.ShouldOpen(LiftPhaseKind.Travel, stopFound: true, doorOpenBit: true));
        }

        [Theory]
        [InlineData(LiftPhaseKind.Ready)]
        [InlineData(LiftPhaseKind.Dwell)]
        [InlineData(LiftPhaseKind.Closing)]
        public void StoppedAtAClosedDoor_TheCabinStaysShut(LiftPhaseKind phase)
        {
            // Ровно случай пропущенного этажа: фаза говорит «стоим», а дверь закрыта. Фаза источником НЕ является.
            Assert.False(LiftCabinDoorState.ShouldOpen(phase, stopFound: true, doorOpenBit: false));
        }

        [Fact]
        public void WithoutAStop_TheCabinStaysShut_WhateverTheBitSays()
        {
            Assert.False(LiftCabinDoorState.ShouldOpen(LiftPhaseKind.Ready, stopFound: false, doorOpenBit: true));
            Assert.False(LiftCabinDoorState.ShouldOpen(LiftPhaseKind.Dwell, stopFound: false, doorOpenBit: true));
        }

        [Fact]
        public void UnstreamedSection_ReadsAsClosed()
        {
            // Нестримленная секция даёт state = 0 → бит Open false. Безопасный дефолт, отдельной ветки не нужно.
            Assert.False(BlockState.GetOpen(0));
            Assert.False(LiftCabinDoorState.ShouldOpen(LiftPhaseKind.Ready, stopFound: true,
                doorOpenBit: BlockState.GetOpen(0)));
        }
    }
}
