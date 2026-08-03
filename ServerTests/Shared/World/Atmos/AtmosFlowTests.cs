using Shared.World.Atmos;
using Shared.World.Blocks;

namespace ServerTests.Shared.World.AtmosCore
{
    public class AtmosFlowTests
    {
        private const ushort Wall = 3;
        private const ushort Door = 6;

        private static BlockGrid Corridor(int length)
        {
            var grid = new BlockGrid();
            for (int x = 0; x <= length + 1; x++)
                for (int y = 0; y <= 2; y++)
                    for (int z = 0; z <= 2; z++)
                    {
                        bool interior = y == 1 && z == 1 && x >= 1 && x <= length;
                        if (!interior) grid.SetBlock(x, y, z, Wall);
                    }
            return grid;
        }

        private static float SumMoles(AtmosGrid atmos)
        {
            float sum = 0f;
            foreach (var kv in atmos.Sections)
                for (int i = 0; i < GasSection.CellCount; i++)
                    sum += kv.Value.Get(i).TotalMoles;
            return sum;
        }

        private static int RunToRest(AtmosFlow flow, BlockGrid grid, AtmosGrid atmos, int maxCells, int maxSteps = 20000)
        {
            int steps = 0;
            while (flow.ActiveCount > 0 && steps < maxSteps)
            {
                flow.Step(grid, atmos, maxCells);
                steps++;
            }
            return steps;
        }

        [Fact]
        public void OpenDoor_BetweenFullAndVacuum_Equalizes_MassConserved()
        {
            var grid = Corridor(5);
            grid.SetBlock(3, 1, 1, Door);
            grid.SetState(3, 1, 1, BlockState.WithOpen(0, false));

            var atmos = new AtmosGrid();
            AtmosInit.Classify(grid, atmos);

            atmos.SetMix(4, 1, 1, GasMix.Vacuum);
            atmos.SetMix(5, 1, 1, GasMix.Vacuum);
            float before = SumMoles(atmos);

            grid.SetState(3, 1, 1, BlockState.WithOpen(0, true));

            var flow = new AtmosFlow();
            flow.WakeAll(grid, atmos);
            RunToRest(flow, grid, atmos, 0);

            Assert.Equal(0, flow.ActiveCount);
            Assert.Equal(before, SumMoles(atmos), 3);

            float left = atmos.GetMix(1, 1, 1).PressureKpa;
            float right = atmos.GetMix(5, 1, 1).PressureKpa;
            Assert.True(right > 0f, $"правая комната должна наполниться, p={right}");
            Assert.True(System.MathF.Abs(left - right) < 0.5f, $"давления должны сойтись: {left} vs {right}");
        }

        [Fact]
        public void ClosedDoor_BlocksFlow_ActiveSetSleeps()
        {
            var grid = Corridor(5);
            grid.SetBlock(3, 1, 1, Door);
            grid.SetState(3, 1, 1, BlockState.WithOpen(0, false));

            var atmos = new AtmosGrid();
            AtmosInit.Classify(grid, atmos);
            atmos.SetMix(4, 1, 1, GasMix.Vacuum);
            atmos.SetMix(5, 1, 1, GasMix.Vacuum);

            var flow = new AtmosFlow();
            flow.WakeAll(grid, atmos);
            RunToRest(flow, grid, atmos, 0);

            Assert.Equal(0, flow.ActiveCount);
            Assert.True(atmos.GetMix(4, 1, 1).IsVacuum());
            Assert.True(atmos.GetMix(5, 1, 1).IsVacuum());
            Assert.Equal(AtmosConstants.StandardPressureKpa, atmos.GetMix(1, 1, 1).PressureKpa, 3);
        }

        [Fact]
        public void OpeningDoorToSpace_DrainsRoom_MassMonotonicallyDecreases()
        {
            var grid = Corridor(4);
            grid.SetBlock(5, 1, 1, Door);
            grid.SetState(5, 1, 1, BlockState.WithOpen(0, false));

            var atmos = new AtmosGrid();
            AtmosInit.Classify(grid, atmos);
            float start = SumMoles(atmos);
            Assert.True(start > 0f);

            grid.SetState(5, 1, 1, BlockState.WithOpen(0, true));

            var flow = new AtmosFlow();
            flow.WakeAround(grid, atmos, 5, 1, 1);

            float prev = start;
            int steps = 0;
            while (flow.ActiveCount > 0 && steps < 20000)
            {
                flow.Step(grid, atmos, 0);
                float now = SumMoles(atmos);
                Assert.True(now <= prev + 1e-4f, $"масса не должна расти: {prev} -> {now}");
                prev = now;
                steps++;
            }

            Assert.True(steps < 20000, "дренаж должен закончиться за конечное число Step");
            Assert.True(prev < start * 0.01f, $"комната должна осушиться: {prev} из {start}");
        }

        [Fact]
        public void ClosingDoorAfterPartialDrain_StopsLeak_RemainderEqualizes()
        {
            var grid = Corridor(4);
            grid.SetBlock(5, 1, 1, Door);
            grid.SetState(5, 1, 1, BlockState.WithOpen(0, false));

            var atmos = new AtmosGrid();
            AtmosInit.Classify(grid, atmos);
            float start = SumMoles(atmos);

            grid.SetState(5, 1, 1, BlockState.WithOpen(0, true));
            var flow = new AtmosFlow();
            flow.WakeAround(grid, atmos, 5, 1, 1);
            for (int i = 0; i < 3; i++) flow.Step(grid, atmos, 0);

            float partial = SumMoles(atmos);
            Assert.True(partial < start && partial > 0f, $"частичный дрейн: {partial} из {start}");

            grid.SetState(5, 1, 1, BlockState.WithOpen(0, false));
            flow.WakeAround(grid, atmos, 5, 1, 1);
            RunToRest(flow, grid, atmos, 0);

            float sealedMass = SumMoles(atmos);
            Assert.True(sealedMass > 0f, "утечка должна встать");
            Assert.True(sealedMass <= partial + 1e-4f);

            float a = atmos.GetMix(1, 1, 1).PressureKpa;
            float d = atmos.GetMix(4, 1, 1).PressureKpa;
            Assert.True(System.MathF.Abs(a - d) < 0.5f, $"остаток должен выровняться: {a} vs {d}");
        }

        [Fact]
        public void SealedSystem_ConservesMass_AcrossManySteps()
        {
            var grid = Corridor(6);
            var atmos = new AtmosGrid();
            AtmosInit.Classify(grid, atmos);

            atmos.SetMix(1, 1, 1, new GasMix(4f, 6f));
            atmos.SetMix(6, 1, 1, GasMix.Vacuum);
            float before = SumMoles(atmos);

            var flow = new AtmosFlow();
            flow.WakeAll(grid, atmos);
            RunToRest(flow, grid, atmos, 0);

            Assert.Equal(before, SumMoles(atmos), 3);
        }

        [Fact]
        public void MolesNeverNegative_AfterDrain()
        {
            var grid = Corridor(4);
            grid.SetBlock(5, 1, 1, Door);
            grid.SetState(5, 1, 1, BlockState.WithOpen(0, true));

            var atmos = new AtmosGrid();
            AtmosInit.Classify(grid, atmos);
            atmos.SetMix(1, 1, 1, GasMix.Standard);
            atmos.SetMix(2, 1, 1, GasMix.Standard);

            var flow = new AtmosFlow();
            flow.WakeAll(grid, atmos);
            RunToRest(flow, grid, atmos, 0);

            for (int x = 1; x <= 4; x++)
            {
                var mix = atmos.GetMix(x, 1, 1);
                Assert.True(mix.O2Moles >= 0f, $"O2 отрицательный в {x}: {mix.O2Moles}");
                Assert.True(mix.N2Moles >= 0f, $"N2 отрицательный в {x}: {mix.N2Moles}");
            }
        }

        [Fact]
        public void Flow_IsDeterministic_AcrossRuns()
        {
            float[] Run()
            {
                var grid = Corridor(6);
                grid.SetBlock(3, 1, 1, Door);
                grid.SetState(3, 1, 1, BlockState.WithOpen(0, true));

                var atmos = new AtmosGrid();
                AtmosInit.Classify(grid, atmos);
                atmos.SetMix(1, 1, 1, new GasMix(2f, 3f));
                atmos.SetMix(6, 1, 1, GasMix.Vacuum);

                var flow = new AtmosFlow();
                flow.WakeAll(grid, atmos);
                for (int i = 0; i < 25; i++) flow.Step(grid, atmos, 3);

                var result = new float[12];
                for (int x = 1; x <= 6; x++)
                {
                    var mix = atmos.GetMix(x, 1, 1);
                    result[(x - 1) * 2] = mix.O2Moles;
                    result[(x - 1) * 2 + 1] = mix.N2Moles;
                }
                return result;
            }

            var a = Run();
            var b = Run();
            for (int i = 0; i < a.Length; i++)
                Assert.Equal(a[i], b[i]);
        }

        [Fact]
        public void Amortization_SmallBudget_ReachesSameEquilibrium()
        {
            float[] Run(int maxCells)
            {
                var grid = Corridor(5);
                var atmos = new AtmosGrid();
                AtmosInit.Classify(grid, atmos);
                atmos.SetMix(1, 1, 1, new GasMix(5f, 5f));
                atmos.SetMix(5, 1, 1, GasMix.Vacuum);

                var flow = new AtmosFlow();
                flow.WakeAll(grid, atmos);
                RunToRest(flow, grid, atmos, maxCells);

                var result = new float[5];
                for (int x = 1; x <= 5; x++) result[x - 1] = atmos.GetMix(x, 1, 1).PressureKpa;
                return result;
            }

            var full = Run(0);
            var throttled = Run(1);

            for (int i = 0; i < full.Length; i++)
                Assert.True(System.MathF.Abs(full[i] - throttled[i]) < 0.5f,
                    $"клетка {i}: полный бюджет {full[i]} vs амортизированный {throttled[i]}");
        }
    }
}
