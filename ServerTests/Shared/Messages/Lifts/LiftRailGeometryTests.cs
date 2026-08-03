using System.Collections.Generic;
using Server.Lifts;
using Shared.Messages.Lifts;
using Shared.Simulation.Blocks;
using Shared.World.Blocks;

namespace ServerTests.Shared.Messages.Lifts
{
    /// <summary>Модули шахты рисуются из реестра и обязаны стоять в ОДНОЙ плоскости с кабиной:
    /// прямоугольник плана у них общий, значит и пивот общий. Вторая конвенция здесь — гарантированный шов.</summary>
    public class LiftRailGeometryTests
    {
        private const float AnchorX = 10f, AnchorZ = 20f;
        private const byte PlanW = 6, PlanD = 5;

        // Шахта пользователя: рельсы на 0/5/10/15, дверей только две. Асимметрия плана обязательна.
        private static LiftRegistryEntry Shaft(byte facing = 0) => new LiftRegistryEntry
        {
            LiftId = 1,
            AnchorX = AnchorX, AnchorZ = AnchorZ,
            PlanW = PlanW, PlanD = PlanD, Facing = facing,
            RailDefId = 4242, CabinDefId = 4243,
            BaseY = 0, FloorStep = 5, FloorCount = 4,
            Boxes = System.Array.Empty<LiftCabinBox>(),
            Stops = new[] { new LiftStopEntry(0, 0f, 11, 0, 19), new LiftStopEntry(3, 15f, 11, 15, 19) }
        };

        [Fact]
        public void FloorYAt_MatchesTheServerFormula_OnEveryFloor()
        {
            var spec = new LiftShaftSpec { BaseY = 0, FloorStep = 5, FloorCount = 4 };
            var wire = Shaft();

            for (int k = 0; k < wire.FloorCount; k++)
                Assert.Equal(spec.FloorYAt(k), wire.FloorYAt(k), 4);
        }

        [Fact]
        public void RailFloors_CoverUnservedOnes_WhichStopsCannotDescribe()
        {
            // Двери только на 0 и 3 — по таблице остановок этажи 1 и 2 не существуют вовсе.
            // Ради них FloorCount/BaseY/FloorStep и едут в проводе.
            var wire = Shaft();

            Assert.Equal(4, wire.FloorCount);
            Assert.Equal(2, wire.Stops.Length);
            Assert.Equal(5f, wire.FloorYAt(1), 4);
            Assert.Equal(10f, wire.FloorYAt(2), 4);
            Assert.DoesNotContain(wire.Stops, s => s.Floor == 1 || s.Floor == 2);
        }

        [Fact]
        public void ParkedCabinHeight_EqualsRailHeight_OnTheSameFloor()
        {
            // Кабина паркуется на Y из таблицы остановок, рельс стоит на FloorYAt того же индекса.
            // Величины едут в проводе РАЗНЫМИ путями (Stops[i].Y от контроллера, BaseY/FloorStep от шахты) —
            // если они разойдутся, кабина и модули шахты окажутся на разной высоте.
            var wire = Shaft();

            foreach (var stop in wire.Stops)
                Assert.Equal(wire.FloorYAt(stop.Floor), stop.Y, 4);
        }

        [Fact]
        public void WorldPivot_AgreesWithTheProductionPipeline()
        {
            // Тот же оракул, что у пивота кабины: вырожденный бокс в object-space центре модуля.
            const int ModX = 6, ModZ = 5;
            var probe = new TriggerBox((short)(ModX * 8), 0, (short)(ModZ * 8),
                                       (short)(ModX * 8), 0, (short)(ModZ * 8));

            for (int facing = 0; facing < 4; facing++)
            {
                var local = LiftCabinPlacement.LocalBoxes(ModX, ModZ, facing, new[] { probe });
                LiftCabinPlacement.PlanSize(ModX, ModZ, facing, out int planW, out int planD);
                LiftCabinPlacement.WorldPivot(AnchorX, AnchorZ, planW, planD, 7f,
                    out float px, out float py, out float pz);

                Assert.Equal(AnchorX + local[0].CenterX, px, 4);
                Assert.Equal(AnchorZ + local[0].CenterZ, pz, 4);
                Assert.Equal(7f, py, 4);
            }
        }

        [Fact]
        public void WorldPivot_PlanWidthMovesX_AndOnlyX()
        {
            // Меняем ТОЛЬКО planW: сдвинуться обязан X и не обязан Z. Проверка asymmetry-вообще
            // («x != z») переживает перестановку осей, эта — нет.
            LiftCabinPlacement.WorldPivot(0f, 0f, 6, 5, 0f, out float x1, out _, out float z1);
            LiftCabinPlacement.WorldPivot(0f, 0f, 8, 5, 0f, out float x2, out _, out float z2);

            Assert.NotEqual(x1, x2);
            Assert.Equal(z1, z2, 4);
        }

        [Fact]
        public void WorldPivot_PlanDepthMovesZ_AndOnlyZ()
        {
            LiftCabinPlacement.WorldPivot(0f, 0f, 6, 5, 0f, out float x1, out _, out float z1);
            LiftCabinPlacement.WorldPivot(0f, 0f, 6, 9, 0f, out float x2, out _, out float z2);

            Assert.NotEqual(z1, z2);
            Assert.Equal(x1, x2, 4);
        }

        [Fact]
        public void RailDefIdAndFloors_SurviveTheWire()
        {
            var src = new LiftRegistry { Lifts = new[] { Shaft(2) } };
            var dst = new LiftRegistry();
            dst.Deserialize(src.Serialize());

            var e = Assert.Single(dst.Lifts);
            Assert.Equal(4242, e.RailDefId);
            Assert.Equal(0, e.BaseY);
            Assert.Equal(5, e.FloorStep);
            Assert.Equal(4, e.FloorCount);
            Assert.Equal(2, e.Facing);
            Assert.NotEqual(e.RailDefId, e.CabinDefId);
        }

        [Fact]
        public void NegativeBaseY_SurvivesTheWire()
        {
            var entry = Shaft();
            entry.BaseY = -37;
            var src = new LiftRegistry { Lifts = new[] { entry } };
            var dst = new LiftRegistry();
            dst.Deserialize(src.Serialize());

            Assert.Equal(-37, dst.Lifts[0].BaseY);
            Assert.Equal(-37f, dst.Lifts[0].FloorYAt(0), 4);
            Assert.Equal(-22f, dst.Lifts[0].FloorYAt(3), 4);
        }

        [Fact]
        public void Builder_CarriesRailTypeAndFloorGeometry_FromTheShaft()
        {
            var lift = new LiftRuntime(AnchorX, AnchorZ, new[] { new LiftBox(0f, 0f, 0f, 1f, 1f, 1f) },
                new LiftSegment(0f, 1f, 0, 0.1f), liftId: 9);
            var shaft = new LiftShaftSpec
            {
                LiftId = 9, RailType = 777, CabinType = 778, Facing = 1,
                BaseY = -4, FloorStep = 5, FloorCount = 4,
                PlanX0 = 10, PlanX1 = 14, PlanZ0 = 20, PlanZ1 = 25
            };

            var reg = LiftRegistryBuilder.Build(new List<LiftRuntime> { lift }, null,
                new List<LiftShaftSpec> { shaft });

            Assert.Equal(777, reg.Lifts[0].RailDefId);
            Assert.Equal(-4, reg.Lifts[0].BaseY);
            Assert.Equal(5, reg.Lifts[0].FloorStep);
            Assert.Equal(4, reg.Lifts[0].FloorCount);
            Assert.NotEqual(reg.Lifts[0].RailDefId, reg.Lifts[0].CabinDefId);
        }

        [Fact]
        public void Builder_WithoutShaft_EmitsNoRail()
        {
            var lift = new LiftRuntime(1f, 2f, new[] { new LiftBox(0f, 0f, 0f, 1f, 1f, 1f) },
                new LiftSegment(0f, 1f, 0, 0.1f), liftId: 5);

            var reg = LiftRegistryBuilder.Build(new List<LiftRuntime> { lift });

            Assert.Equal(0, reg.Lifts[0].RailDefId);
            Assert.Equal(0, reg.Lifts[0].FloorCount);
        }
    }
}
