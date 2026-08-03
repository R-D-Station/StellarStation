using System.Collections.Generic;
using System.Diagnostics;
using Shared.World.Atmos;
using Shared.World.Blocks;
using Xunit.Abstractions;

namespace ServerTests.Shared.World.AtmosCore
{
    public class AtmosBenchTests
    {
        private const ushort Wall = 3;
        private const ushort Slab = 2;
        private const ushort Door = 6;

        private const int Plan = 32;
        private const int RoomPitch = 9;
        private static readonly int[] FloorY = { 1, 3, 5 };
        private const int StepCap = 400;

        private readonly ITestOutputHelper _out;

        public AtmosBenchTests(ITestOutputHelper output) => _out = output;

        private sealed class Station
        {
            public BlockGrid Grid = null!;
            public AtmosGrid Atmos = null!;
            public List<long> ExternalDoors = new List<long>();
            public List<long> InternalDoors = new List<long>();
        }

        private static bool IsWallLine(int v) => v % RoomPitch == 0;

        private static Station Build(bool hangar)
        {
            var s = new Station { Grid = new BlockGrid(), Atmos = new AtmosGrid() };
            var grid = s.Grid;

            for (int x = 0; x < Plan; x++)
                for (int z = 0; z < Plan; z++)
                {
                    for (int f = 0; f < FloorY.Length; f++)
                    {
                        grid.SetBlock(x, FloorY[f] - 1, z, Slab);
                        grid.SetBlock(x, FloorY[f] + 1, z, Slab);
                    }

                    bool perimeter = x == 0 || z == 0 || x == Plan - 1 || z == Plan - 1;
                    bool inner = IsWallLine(x) || IsWallLine(z);
                    if (!perimeter && !inner) continue;

                    for (int f = 0; f < FloorY.Length; f++)
                        grid.SetBlock(x, FloorY[f], z, Wall);
                }

            if (hangar)
                for (int x = 1; x <= 16; x++)
                    for (int z = 1; z <= 16; z++)
                        grid.SetBlock(x, FloorY[0], z, 0);

            for (int f = 0; f < FloorY.Length; f++)
            {
                int y = FloorY[f];

                for (int z = 2; z < Plan - 1; z += RoomPitch)
                {
                    if (hangar && f == 0 && z <= 24) continue;
                    grid.SetBlock(0, y, z, Door);
                    grid.SetState(0, y, z, BlockState.WithOpen(0, false));
                    s.ExternalDoors.Add(AtmosCell.Pack(0, y, z));
                }

                for (int x = RoomPitch; x < Plan - 1; x += RoomPitch)
                    for (int z = 2; z < Plan - 1; z += RoomPitch)
                    {
                        if (hangar && f == 0 && x <= 16 && z <= 16) continue;
                        grid.SetBlock(x, y, z, Door);
                        grid.SetState(x, y, z, BlockState.WithOpen(0, false));
                        s.InternalDoors.Add(AtmosCell.Pack(x, y, z));
                    }
            }

            if (hangar)
            {
                grid.SetBlock(0, FloorY[0], 8, Door);
                grid.SetState(0, FloorY[0], 8, BlockState.WithOpen(0, false));
                s.ExternalDoors.Add(AtmosCell.Pack(0, FloorY[0], 8));
            }

            AtmosInit.Classify(grid, s.Atmos);
            return s;
        }

        private static float SumMoles(AtmosGrid atmos)
        {
            float sum = 0f;
            foreach (var kv in atmos.Sections)
                for (int i = 0; i < GasSection.CellCount; i++)
                    sum += kv.Value.Get(i).TotalMoles;
            return sum;
        }

        private static void OpenDoor(Station s, long packed, AtmosFlow flow)
        {
            AtmosCell.Unpack(packed, out int x, out int y, out int z);
            s.Grid.SetState(x, y, z, BlockState.WithOpen(0, true));
            flow.WakeAround(s.Grid, s.Atmos, x, y, z);
        }

        private (double avgMs, double peakMs, int peakActive, int steps, float massBefore, float massAfter)
            RunScenario(Station s, AtmosFlow flow, int maxCells, int maxSteps)
        {
            float before = SumMoles(s.Atmos);
            double peak = 0d, total = 0d;
            int peakActive = 0, steps = 0;
            var sw = new Stopwatch();

            while (flow.ActiveCount > 0 && steps < maxSteps)
            {
                if (flow.ActiveCount > peakActive) peakActive = flow.ActiveCount;
                sw.Restart();
                flow.Step(s.Grid, s.Atmos, maxCells);
                sw.Stop();
                double ms = sw.Elapsed.TotalMilliseconds;
                total += ms;
                if (ms > peak) peak = ms;
                steps++;
            }

            return (steps == 0 ? 0d : total / steps, peak, peakActive, steps, before, SumMoles(s.Atmos));
        }

        [Fact]
        public void Bench_AtmosFlow_WorstCaseNumbers()
        {
            const int maxCells = 8192;
            _out.WriteLine($"станция: план {Plan}x{Plan}, этажей {FloorY.Length}, комнаты 8x8, бюджет {maxCells} клеток/суб-тик");

            var settled = Build(hangar: false);
            var flowSettled = new AtmosFlow();
            flowSettled.WakeAll(settled.Grid, settled.Atmos);
            var warm = RunScenario(settled, flowSettled, maxCells, StepCap);
            _out.WriteLine($"[прогрев] полный обход: Step'ов {warm.steps}, средн {warm.avgMs:F3} мс, пик {warm.peakMs:F3} мс, актив-пик {warm.peakActive}");

            var swIdle = Stopwatch.StartNew();
            for (int i = 0; i < 100; i++) flowSettled.Step(settled.Grid, settled.Atmos, maxCells);
            swIdle.Stop();
            _out.WriteLine($"[1. равновесие] 100 Step на устаканенной станции: {swIdle.Elapsed.TotalMilliseconds / 100d:F4} мс/Step, актив {flowSettled.ActiveCount}");
            Assert.Equal(0, flowSettled.ActiveCount);

            var small = Build(hangar: false);
            var flowSmall = new AtmosFlow();
            OpenDoor(small, small.ExternalDoors[0], flowSmall);
            var r2 = RunScenario(small, flowSmall, maxCells, StepCap);
            _out.WriteLine($"[2. пробоина малой комнаты] Step'ов {r2.steps}, средн {r2.avgMs:F3} мс, пик {r2.peakMs:F3} мс, актив-пик {r2.peakActive}, масса {r2.massBefore:F1} -> {r2.massAfter:F1}");
            Assert.True(r2.massAfter < r2.massBefore);

            var hangar = Build(hangar: true);
            var flowHangar = new AtmosFlow();
            OpenDoor(hangar, hangar.ExternalDoors[hangar.ExternalDoors.Count - 1], flowHangar);
            var r3 = RunScenario(hangar, flowHangar, maxCells, StepCap);
            _out.WriteLine($"[3. пробоина ангара 16x16] Step'ов {r3.steps}, средн {r3.avgMs:F3} мс, пик {r3.peakMs:F3} мс, актив-пик {r3.peakActive}, масса {r3.massBefore:F1} -> {r3.massAfter:F1}");
            Assert.True(r3.massAfter < r3.massBefore);

            var full = Build(hangar: true);
            var flowFull = new AtmosFlow();
            for (int i = 0; i < full.ExternalDoors.Count; i++) OpenDoor(full, full.ExternalDoors[i], flowFull);
            for (int i = 0; i < full.InternalDoors.Count; i++) OpenDoor(full, full.InternalDoors[i], flowFull);
            var swFull = Stopwatch.StartNew();
            var r4 = RunScenario(full, flowFull, maxCells, StepCap);
            swFull.Stop();
            _out.WriteLine($"[4. полный дрейн] Step'ов {r4.steps}, средн {r4.avgMs:F3} мс, пик {r4.peakMs:F3} мс, актив-пик {r4.peakActive}, масса {r4.massBefore:F1} -> {r4.massAfter:F1}, суммарно {swFull.Elapsed.TotalMilliseconds:F0} мс");
            Assert.True(r4.massAfter < r4.massBefore);

            foreach (int budget in new[] { 4096, 2048, 1024, 512 })
            {
                var t = Build(hangar: true);
                var flowT = new AtmosFlow();
                for (int i = 0; i < t.ExternalDoors.Count; i++) OpenDoor(t, t.ExternalDoors[i], flowT);
                for (int i = 0; i < t.InternalDoors.Count; i++) OpenDoor(t, t.InternalDoors[i], flowT);
                var rt = RunScenario(t, flowT, budget, StepCap);
                _out.WriteLine($"[5. полный дрейн, бюджет {budget}] средн {rt.avgMs:F3} мс, пик {rt.peakMs:F3} мс, актив-пик {rt.peakActive}, масса {rt.massBefore:F1} -> {rt.massAfter:F1}");
            }
        }
    }
}
