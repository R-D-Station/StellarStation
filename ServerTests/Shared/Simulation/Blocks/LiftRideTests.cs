using Shared.Messages.Core;
using Shared.Simulation;
using Shared.Simulation.Blocks;
using Shared.World.Blocks;

namespace ServerTests.Shared.Simulation.Blocks
{
    /// <summary>Предикат «игрок в кабине»: главный инвариант — carry ⊆ panel (кого везёт, тот и жмёт кнопку).</summary>
    public class LiftRideTests
    {
        // ⚠ ЧЕТВЁРТЫЙ СЛУЧАЙ СЛЕПОЙ ФИКСТУРЫ ЗА ТРЕК: при HalfX == HalfZ и CX == CZ подмена осей невидима.
        // Кабина ПРЯМОУГОЛЬНАЯ в плане (реальный случай: LiftCabinPlacement даёт разные полуширины любой
        // некубической кабине) и центр смещён по обеим осям по-разному.
        private const float CX = 11f, CZ = 20.5f;
        private const float HalfX = 1.5f, HalfZ = 0.5f;
        private const float Top = 1.25f;

        private sealed class TestShapes : IBlockShapes
        {
            private static readonly BlockBox[] None = System.Array.Empty<BlockBox>();
            private static readonly BlockBox[] SlabBox = { BlockBox.SlabTop };
            public BlockBox[] GetBoxes(ushort type, byte state) => type == 2 ? SlabBox : None;
        }

        private static BlockGrid Room()
        {
            var g = new BlockGrid();
            for (int x = 2; x <= 20; x++)
                for (int z = 12; z <= 30; z++)
                    g.SetBlock(x, 0, z, 2);
            return g;
        }

        [Fact]
        public void StandingOnStoppedCabin_IsPassenger()
        {
            // Регресс на отвергнутое хранимое поле: carry работает ТОЛЬКО при delta != 0, поэтому пассажир
            // СТОЯЩЕЙ кабины выпал бы из множества ровно тогда, когда ему надо нажать кнопку.
            Assert.True(LiftRide.StandsOn(CX, Top, CZ, CX, CZ, HalfX, HalfZ, Top));
        }

        [Fact]
        public void CarrySubsetPanel_OnRisingCabin_EveryTick()
        {
            var grid = Room();
            var shapes = new TestShapes();
            var input = new BlockMoveInput(IntentDirection.None);
            // НЕ в центре: у длинной стены, иначе разница полуширин снова невидима.
            float px = CX + HalfX - BlockMovementConfig.HalfWidth - 0.05f;
            var s = new BlockMoverState(px, Top, CZ, grounded: true);

            const float Delta = 0.1f;
            float top = Top;
            int carriedTicks = 0;
            for (int k = 0; k < 120; k++)
            {
                float before = s.Y;
                top += Delta;
                var set = new DynamicObstacleSet();
                set.Add(CX, top - 0.25f, CZ, HalfX, HalfZ, 0.25f, Delta);
                BlockMovementLogic.Step(grid, shapes, ref s, in input, MovementLogic.StepPerTick, set);

                if (s.Y <= before + 1e-4f)
                    continue;
                carriedTicks++;
                Assert.True(LiftRide.StandsOn(s.X, s.Y, s.Z, CX, CZ, HalfX, HalfZ, top),
                    $"тик {k}: carry везёт (y {before}→{s.Y}), а панель не признаёт — инвариант carry ⊆ panel порван; " +
                    $"верх кабины {top}");
            }

            // Без этих двух ассертов тест был бы вакуумным: при carriedTicks == 0 внутренний assert не выполнится ни разу.
            Assert.True(carriedTicks > 100, $"кабина почти не везла игрока ({carriedTicks} тиков) — тест ничего не проверил");
            Assert.Equal(top, s.Y, 3);
        }

        [Fact]
        public void FeetOneTickBehindRisingRoof_IsStillPassenger()
        {
            // Точный случай, где наивная граница top - Epsilon соврала бы: carry ловит ноги на ПРЕДЫДУЩЕЙ крыше.
            const float Roof = 4f, Delta = 0.1f;
            Assert.True(LiftRide.StandsOn(CX, Roof - Delta, CZ, CX, CZ, HalfX, HalfZ, Roof));
        }

        [Fact]
        public void JumpInsideCabin_IsStillPassenger()
        {
            Assert.True(LiftRide.StandsOn(CX, Top + 1f, CZ, CX, CZ, HalfX, HalfZ, Top));
            Assert.True(LiftRide.StandsOn(CX, Top + BlockMovementConfig.StandHeight, CZ, CX, CZ, HalfX, HalfZ, Top));
        }

        [Fact]
        public void StandingOnFloorWhileCabinIsBelow_IsNotPassenger()
        {
            // Планы КАСАЮТСЯ краем (игрок на этаже вплотную к шахте), кабина этажом ниже: py - top = 1.0,
            // это больше AutoStep, а центр игрока вне плана бокса → пассажиром не считается.
            float edgeX = CX + HalfX + BlockMovementConfig.HalfWidth - 0.01f;
            Assert.False(LiftRide.StandsOn(edgeX, Top + 1f, CZ, CX, CZ, HalfX, HalfZ, Top));
        }

        [Fact]
        public void OutsideAlongX_InsideAlongZ_IsNotPassenger()
        {
            // Меняет знак РОВНО при свопе halfX/halfZ: по X снаружи (нужна полуширина 1.5), по Z внутри.
            float outsideX = CX + HalfX + BlockMovementConfig.HalfWidth + 0.05f;
            Assert.False(LiftRide.StandsOn(outsideX, Top, CZ, CX, CZ, HalfX, HalfZ, Top));
        }

        [Fact]
        public void OutsideAlongZ_InsideAlongX_IsNotPassenger()
        {
            // Зеркальный: по Z снаружи (полуширина всего 0.5), по X внутри. Со свопом половин станет true.
            float outsideZ = CZ + HalfZ + BlockMovementConfig.HalfWidth + 0.05f;
            Assert.False(LiftRide.StandsOn(CX, Top, outsideZ, CX, CZ, HalfX, HalfZ, Top));
        }

        [Fact]
        public void InsideNearLongWall_IsPassenger()
        {
            // Внутри у ДЛИННОЙ стены: |px - CX| = 1.3 > HalfZ, поэтому со свопом половин станет false.
            Assert.True(LiftRide.StandsOn(CX + 1.3f, Top, CZ, CX, CZ, HalfX, HalfZ, Top));
        }

        [Fact]
        public void CentersAreNotInterchangeable()
        {
            // Позиция валидна только при верном сопоставлении центров: со свопом CenterX/CenterZ станет false.
            Assert.True(LiftRide.StandsOn(CX + 1.3f, Top, CZ + 0.3f, CX, CZ, HalfX, HalfZ, Top));
            Assert.False(LiftRide.StandsOn(CZ, Top, CX, CX, CZ, HalfX, HalfZ, Top));
        }

        [Fact]
        public void FarBelowOrFarAbove_IsNotPassenger()
        {
            Assert.False(LiftRide.StandsOn(CX, 0.5f, CZ, CX, CZ, HalfX, HalfZ, 4f));
            Assert.False(LiftRide.StandsOn(CX, 4f + BlockMovementConfig.StandHeight + 0.01f, CZ,
                CX, CZ, HalfX, HalfZ, 4f));
        }
    }
}
