using Shared.Simulation.Blocks;
using Shared.World.Blocks;

namespace ServerTests.Shared.Simulation.Blocks
{
    /// <summary>Пивот кабины сверяется с БОЕВЫМ конвейером (LocalBoxes → TriggerWorldBounds), а не с копией формулы:
    /// вырожденный бокс в object-space точке (moduleX*0.5, ·, moduleZ*0.5) даёт t.Min−px = 0, поворот даёт 0,
    /// значит центр полученного LiftBox И ЕСТЬ корень префаба.</summary>
    public class LiftCabinPivotTests
    {
        // Модуль 6×5 — НЕ квадрат: при квадратном модуле подмена planW/planD ненаблюдаема.
        private const int ModX = 6, ModZ = 5;

        // Точка пивота в object-space, в 1/16 доли клетки: 3.0 и 2.5 попадают в short без округления.
        private static TriggerBox PivotProbe()
            => new TriggerBox((short)(ModX * 8), 0, (short)(ModZ * 8),
                              (short)(ModX * 8), 0, (short)(ModZ * 8));

        // Бокс, смещённый по ОБЕИМ плановым осям и прямоугольный — соседство с зондом не должно на него влиять.
        private static TriggerBox OffsetBox()
            => new TriggerBox(20, 8, 12, 68, 40, 52);

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void PivotFromPlan_MatchesProductionPipeline_EveryFacing(int facing)
        {
            var probe = LiftCabinPlacement.LocalBoxes(ModX, ModZ, facing, new[] { PivotProbe() });
            var box = Assert.Single(probe);

            LiftCabinPlacement.PlanSize(ModX, ModZ, facing, out int planW, out int planD);
            LiftCabinPlacement.PivotFromPlan(planW, planD, out float offX, out float offZ);

            Assert.Equal(offX, box.CenterX, 4);
            Assert.Equal(offZ, box.CenterZ, 4);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void PlanIsRectangular_SoAxisSwapIsObservable(int facing)
        {
            LiftCabinPlacement.PlanSize(ModX, ModZ, facing, out int planW, out int planD);
            Assert.NotEqual(planW, planD);
            LiftCabinPlacement.PivotFromPlan(planW, planD, out float offX, out float offZ);
            Assert.NotEqual(offX, offZ);
        }

        [Fact]
        public void PlanSize_SwapsAxes_OnQuarterTurns()
        {
            LiftCabinPlacement.PlanSize(ModX, ModZ, 0, out int w0, out int d0);
            LiftCabinPlacement.PlanSize(ModX, ModZ, 1, out int w1, out int d1);
            LiftCabinPlacement.PlanSize(ModX, ModZ, 2, out int w2, out int d2);
            LiftCabinPlacement.PlanSize(ModX, ModZ, 3, out int w3, out int d3);

            Assert.Equal((ModX, ModZ), (w0, d0));
            Assert.Equal((ModZ, ModX), (w1, d1));
            Assert.Equal((ModX, ModZ), (w2, d2));
            Assert.Equal((ModZ, ModX), (w3, d3));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void BoxY_IsRelativeToCabinFloor_WithNoHiddenAddend(int facing)
        {
            // Обе величины НЕНУЛЕВЫЕ намеренно: на нулевых бокс и пол совпадают, и тест не упал бы
            // ни от какой правки Y — ни от PivotYOffset, ни от смещения на этаж.
            const float BoxBottom = 0.75f, BoxTop = 3.25f;
            var box = new TriggerBox(20, (short)(BoxBottom * 16f), 12, 68, (short)(BoxTop * 16f), 52);

            var local = LiftCabinPlacement.LocalBoxes(ModX, ModZ, facing, new[] { box });

            Assert.Equal(BoxBottom, local[0].Y, 4);
            Assert.Equal(BoxTop - BoxBottom, local[0].Height, 4);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(7)]
        [InlineData(-3)]
        public void WorldY_IsCabinFloorPlusBoxY_NotHalfTheSize(int cabinFloorY)
        {
            // Пол кабины НЕНУЛЕВОЙ: если бы к Y прибавлялся PivotYOffset (Size.y*0.5) или полмодуля,
            // на этих трёх этажах разница была бы видна.
            const float BoxBottom = 0.75f, BoxTop = 3.25f;
            var box = new TriggerBox(20, (short)(BoxBottom * 16f), 12, 68, (short)(BoxTop * 16f), 52);

            LiftCabinPlacement.WorldBounds(0, cabinFloorY, 0, ModX, ModZ, 0, in box,
                out _, out float minY, out _, out _, out float maxY, out _);

            Assert.Equal(cabinFloorY + BoxBottom, minY, 4);
            Assert.Equal(cabinFloorY + BoxTop, maxY, 4);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void NeighbourBoxes_DoNotShiftThePivot(int facing)
        {
            var alone = LiftCabinPlacement.LocalBoxes(ModX, ModZ, facing, new[] { PivotProbe() });
            var mixed = LiftCabinPlacement.LocalBoxes(ModX, ModZ, facing, new[] { PivotProbe(), OffsetBox() });

            Assert.Equal(2, mixed.Length);
            Assert.Equal(alone[0].CenterX, mixed[0].CenterX, 4);
            Assert.Equal(alone[0].CenterZ, mixed[0].CenterZ, 4);
        }

        [Fact]
        public void OppositeFacings_ShareThePlan_SoFacingMustRideTheWire()
        {
            // 0↔2 и 1↔3 дают ОДИН прямоугольник: по плану поворот не восстановить, а префаб при этом
            // развёрнут на 180°. Отсюда отдельное поле Facing в реестре.
            LiftCabinPlacement.PlanSize(ModX, ModZ, 0, out int w0, out int d0);
            LiftCabinPlacement.PlanSize(ModX, ModZ, 2, out int w2, out int d2);
            LiftCabinPlacement.PlanSize(ModX, ModZ, 1, out int w1, out int d1);
            LiftCabinPlacement.PlanSize(ModX, ModZ, 3, out int w3, out int d3);

            Assert.Equal((w0, d0), (w2, d2));
            Assert.Equal((w1, d1), (w3, d3));
        }

        [Fact]
        public void EmptyPlan_FallsBackToOneCell_NotZero()
        {
            // PlanW == 0 бывает штатно (Build без шахт). Ноль дал бы пивот 0 и вырожденный диапазон колонок.
            LiftCabinPlacement.PivotFromPlan(0, 0, out float offX, out float offZ);
            Assert.Equal(0.5f, offX, 4);
            Assert.Equal(0.5f, offZ, 4);
        }
    }
}
