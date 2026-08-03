using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Client.Lifts;
using Shared.World.Blocks;

namespace ServerTests.ClientPure
{
    // Диапазон id 980..981 — свободный (см. guard-тест ниже): порядок [ModuleInitializer] не определён,
    // и столкновение с чужим тестовым каталогом проигрывается МОЛЧА.
    internal static class GateCatalog
    {
        internal const ushort Wall = 980;
        internal const ushort Marker = 981;

        [ModuleInitializer]
        internal static void Seed()
        {
            var field = typeof(BlockCatalog).GetField("_byId", BindingFlags.NonPublic | BindingFlags.Static)!;
            var byId = (Dictionary<ushort, BlockInfo>)field.GetValue(null)!;

            byId[Wall] = new BlockInfo(Wall, "GateWall", BlockCategory.Wall,
                BlockFaceFlags.All, BlockFaceFlags.All, 0, new[] { BlockBox.Full });
            byId[Marker] = new BlockInfo(Marker, "GateMarker", BlockCategory.Marker,
                BlockFaceFlags.None, BlockFaceFlags.None, 0, Array.Empty<BlockBox>());
        }
    }

    /// <summary>Гейт видимости кабины целиком: футпринт колонок, диапазон рядов по ВИЗУАЛЬНОЙ высоте
    /// и свёртка MAX с пропуском клеток, мешающих видеть. Живёт в файле без UnityEngine именно ради этого.</summary>
    public class LiftVisibilityGateTests
    {
        // Боевые величины: модуль кабины 5, шаг этажа 5, бокс кабины — плита пола толщиной 0.5.
        private const int ModuleY = 5;
        private const float PlateTop = 1.0f;

        [Fact]
        public void TestCatalogIds_AreNotStolenByAnotherTestCatalog()
        {
            Assert.Equal("GateWall", BlockCatalog.Get(GateCatalog.Wall).Name);
            Assert.Equal("GateMarker", BlockCatalog.Get(GateCatalog.Marker).Name);
        }

        private sealed class Cells : ICellAlphaSource
        {
            public readonly Dictionary<(int, int, int), float> Alpha = new();
            public int Queries;

            public float AlphaAt(int x, int y, int z)
            {
                Queries++;
                return Alpha.TryGetValue((x, y, z), out float a) ? a : 0f;
            }
        }

        private static BlockGrid EmptyGrid() => new BlockGrid();

        [Fact]
        public void ColumnRange_CoversWholeFootprint_AsymmetricPlan()
        {
            LiftVisibilityGate.ColumnRange(10f, 20f, 6, 5, out int x0, out int x1, out int z0, out int z1);

            Assert.Equal(10, x0);
            Assert.Equal(15, x1);
            Assert.Equal(20, z0);
            Assert.Equal(24, z1);
        }

        [Fact]
        public void ColumnRange_DoesNotSwapAxes()
        {
            LiftVisibilityGate.ColumnRange(10f, 20f, 6, 5, out int x0, out int x1, out int z0, out int z1);
            Assert.Equal(5, x1 - x0);
            Assert.Equal(4, z1 - z0);
        }

        [Fact]
        public void ZeroPlan_FallsBackToSingleColumn_NotInvertedRange()
        {
            LiftVisibilityGate.ColumnRange(3f, -4f, 0, 0, out int x0, out int x1, out int z0, out int z1);

            Assert.Equal(3, x0);
            Assert.Equal(3, x1);
            Assert.Equal(-4, z0);
            Assert.Equal(-4, z1);
        }

        [Fact]
        public void ColumnRange_FloorsNegativeAnchor_TowardsMinusInfinity()
        {
            LiftVisibilityGate.ColumnRange(-0.5f, -10.25f, 2, 3, out int x0, out int x1, out int z0, out int z1);

            Assert.Equal(-1, x0);
            Assert.Equal(0, x1);
            Assert.Equal(-11, z0);
            Assert.Equal(-9, z1);
        }

        [Fact]
        public void RowRange_CoversWholeCabin_ByModuleHeight()
        {
            LiftVisibilityGate.RowRange(5f, ModuleY, ModuleY, out int lo, out int hi);

            Assert.Equal(5, lo);
            Assert.Equal(9, hi);
            Assert.Equal(ModuleY, hi - lo + 1);
        }

        [Fact]
        public void RowRange_ByCollisionPlate_MissesThePassenger_WhichIsTheBugBeingFixed()
        {
            // Плита пола 0.5..1.0: по ней диапазон схлопывается в ОДИН ряд — ряд самой плиты.
            // Клетка ног пассажира (ряд+1) в опрос не попадает. Ровно поэтому высота берётся из модуля.
            LiftVisibilityGate.RowRange(5f, PlateTop, ModuleY, out int lo, out int hi);
            Assert.Equal(5, lo);
            Assert.Equal(5, hi);

            LiftVisibilityGate.RowRange(5f, ModuleY, ModuleY, out int mLo, out int mHi);
            Assert.True(mHi > hi, "модульная высота обязана накрывать больше рядов, чем коллизионная плита");
            Assert.InRange(6, mLo, mHi);
        }

        [Fact]
        public void RowRange_MidTravel_CoversBothSidesOfTheSlab()
        {
            // Подъём 5→10: на y = 7.4 кабина ПЕРЕСЕКАЕТ перекрытие, ряды по разные стороны от него.
            LiftVisibilityGate.RowRange(7.4f, ModuleY, ModuleY, out int lo, out int hi);
            Assert.Equal(7, lo);
            Assert.Equal(12, hi);
        }

        [Fact]
        public void RowRange_TopIsExclusive_SoAFlushCabinDoesNotGrabTheCeilingRow()
        {
            // Кабина 5 высотой на y=5 занимает 5..10 без верхней границы: ряд 10 — уже перекрытие.
            LiftVisibilityGate.RowRange(5f, 5f, 16, out int lo, out int hi);
            Assert.Equal(9, hi);
        }

        [Fact]
        public void RowRange_IsCapped_SoThePerFrameLoopCannotRunAway()
        {
            LiftVisibilityGate.RowRange(0f, 1000f, ModuleY, out int lo, out int hi);
            Assert.Equal(0, lo);
            Assert.Equal(ModuleY, hi);
        }

        [Fact]
        public void RowCap_LeavesRoomForTheExtraRow_AFractionalYNeeds()
        {
            // Кабина высотой H на ДРОБНОМ y занимает H+1 рядов. Кламп в H рядов срезал бы верхний —
            // ровно тот, что оказывается по другую сторону перекрытия на подъёме.
            LiftVisibilityGate.RowRange(7.4f, ModuleY, ModuleY, out int lo, out int hi);
            Assert.Equal(ModuleY + 1, hi - lo + 1);

            LiftVisibilityGate.RowRange(7f, ModuleY, ModuleY, out int iLo, out int iHi);
            Assert.Equal(ModuleY, iHi - iLo + 1);
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(-1f)]
        public void RowRange_NonPositiveHeight_GivesTheFloorRowOnly(float height)
        {
            LiftVisibilityGate.RowRange(4.25f, height, ModuleY, out int lo, out int hi);
            Assert.Equal(4, lo);
            Assert.Equal(4, hi);
        }

        [Fact]
        public void RowRange_NegativeY_FloorsTowardsMinusInfinity()
        {
            LiftVisibilityGate.RowRange(-3.5f, ModuleY, ModuleY, out int lo, out int hi);
            Assert.Equal(-4, lo);
            Assert.Equal(1, hi);
        }

        [Fact]
        public void Markers_DoNotBlockSight_ButRealBlocksDo()
        {
            // Якорь РЕЛЬСА — маркер. Отбрасывать по «не ноль» значило бы тихо выкинуть колонку рельса.
            Assert.False(LiftVisibilityGate.BlocksSight(0));
            Assert.False(LiftVisibilityGate.BlocksSight(GateCatalog.Marker));
            Assert.True(LiftVisibilityGate.BlocksSight(GateCatalog.Wall));
        }

        [Fact]
        public void Combine_TakesMaxAcrossRows_NotTheFloorRow()
        {
            // Ряды по разные стороны перекрытия дают 0 и 1 — результат обязан быть 1.
            var grid = EmptyGrid();
            var cells = new Cells();
            cells.Alpha[(0, 5, 0)] = 0f;
            cells.Alpha[(0, 6, 0)] = 0f;
            cells.Alpha[(0, 7, 0)] = 1f;

            float a = LiftVisibilityGate.Combine(grid, cells, true, 0, 0, 0, 0, 5, 7);

            Assert.Equal(1f, a);
        }

        [Fact]
        public void Combine_SkipsCellsThatBlockSight_ButNotMarkers()
        {
            var grid = EmptyGrid();
            grid.SetBlock(0, 5, 0, GateCatalog.Wall);
            grid.SetBlock(1, 5, 0, GateCatalog.Marker);

            var cells = new Cells();
            cells.Alpha[(0, 5, 0)] = 1f;
            cells.Alpha[(1, 5, 0)] = 0f;

            float a = LiftVisibilityGate.Combine(grid, cells, true, 0, 1, 0, 0, 5, 5);

            Assert.Equal(0f, a);
            Assert.Equal(1, cells.Queries);
        }

        [Fact]
        public void Combine_EverythingBlocked_MeansVisible_NotHidden()
        {
            var grid = EmptyGrid();
            grid.SetBlock(0, 5, 0, GateCatalog.Wall);

            var cells = new Cells();
            cells.Alpha[(0, 5, 0)] = 0f;

            Assert.Equal(1f, LiftVisibilityGate.Combine(grid, cells, true, 0, 0, 0, 0, 5, 5));
            Assert.Equal(0, cells.Queries);
        }

        [Fact]
        public void Combine_WithoutValidSnapshot_IsVisible_AndAsksNothing()
        {
            var cells = new Cells();
            Assert.Equal(1f, LiftVisibilityGate.Combine(EmptyGrid(), cells, false, 0, 5, 0, 5, 0, 5));
            Assert.Equal(0, cells.Queries);
        }

        [Fact]
        public void Combine_VisitsEveryCellOfTheFootprint()
        {
            var cells = new Cells();
            LiftVisibilityGate.Combine(EmptyGrid(), cells, true, 0, 5, 0, 4, 0, 4);
            Assert.Equal(6 * 5 * 5, cells.Queries);
        }

        [Fact]
        public void Accumulator_TakesMaximum_NotFirstOrLast()
        {
            var acc = new LiftAlphaAccumulator();
            acc.Add(0.25f);
            acc.Add(1f);
            acc.Add(0f);

            Assert.Equal(1f, acc.Result);
        }

        [Fact]
        public void Accumulator_SingleZero_IsNotConfusedWithEmpty()
        {
            var empty = new LiftAlphaAccumulator();
            var hidden = new LiftAlphaAccumulator();
            hidden.Add(0f);

            Assert.False(empty.Any);
            Assert.Equal(1f, empty.Result);
            Assert.Equal(0f, hidden.Result);
        }
    }
}
