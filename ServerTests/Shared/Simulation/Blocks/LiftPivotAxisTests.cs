using Client.Lifts;
using Shared.World.Blocks;
using Xunit;

namespace ServerTests.Shared.Simulation.Blocks
{
    public class LiftPivotAxisTests
    {
        private const int ModuleX = 6;
        private const int ModuleZ = 5;
        private const int RailX = 100;
        private const int RailY = 7;
        private const int RailZ = 200;

        private static void RailPose(int moduleX, int moduleZ, int facing, out float x, out float y, out float z)
        {
            bool ok = LiftPlanSource.TryPose(LiftPartKind.Rail, RailX, RailY, RailZ,
                moduleX, moduleZ, facing, in NoShaft, out x, out y, out z, out _);
            Assert.True(ok);
        }

        private static readonly LiftShaftPlan NoShaft = default;

        [Theory]
        [InlineData(0, 103.0f, 202.5f)]
        [InlineData(1, 102.5f, 198.0f)]
        [InlineData(2, 98.0f, 198.5f)]
        [InlineData(3, 98.5f, 203.0f)]
        public void Rail_NonSquareModule_PoseMatchesExpectedPerFacing(int facing, float expectedX, float expectedZ)
        {
            RailPose(ModuleX, ModuleZ, facing, out float x, out _, out float z);

            Assert.Equal(expectedX, x, 4);
            Assert.Equal(expectedZ, z, 4);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void Rail_PoseIsPlanCentre_NotMarkerCell(int facing)
        {
            RailPose(ModuleX, ModuleZ, facing, out float x, out _, out float z);

            Assert.False(x == RailX + 0.5f && z == RailZ + 0.5f);
        }

        [Fact]
        public void Rail_FloorY_PassedThroughUnchanged()
        {
            RailPose(ModuleX, ModuleZ, 0, out _, out float y, out _);

            Assert.Equal(RailY, y, 4);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void Rail_ModuleAxis_MovesExactlyOneWorldAxis_PerFacing(int facing)
        {
            bool moduleXDrivesWorldX = (facing & 1) == 0;

            RailPose(ModuleX, ModuleZ, facing, out float baseX, out _, out float baseZ);

            RailPose(ModuleX + 2, ModuleZ, facing, out float wideX, out _, out float wideZ);
            if (moduleXDrivesWorldX)
            {
                Assert.NotEqual(baseX, wideX, 4);
                Assert.Equal(baseZ, wideZ, 4);
            }
            else
            {
                Assert.Equal(baseX, wideX, 4);
                Assert.NotEqual(baseZ, wideZ, 4);
            }

            RailPose(ModuleX, ModuleZ + 2, facing, out float deepX, out _, out float deepZ);
            if (moduleXDrivesWorldX)
            {
                Assert.Equal(baseX, deepX, 4);
                Assert.NotEqual(baseZ, deepZ, 4);
            }
            else
            {
                Assert.NotEqual(baseX, deepX, 4);
                Assert.Equal(baseZ, deepZ, 4);
            }
        }

        [Fact]
        public void Rail_SwappedModuleAxes_ChangePosePerAxis()
        {
            RailPose(ModuleX, ModuleZ, 0, out float x, out _, out float z);
            RailPose(ModuleZ, ModuleX, 0, out float swappedX, out _, out float swappedZ);

            Assert.Equal(103.0f, x, 4);
            Assert.Equal(202.5f, z, 4);
            Assert.Equal(102.5f, swappedX, 4);
            Assert.Equal(203.0f, swappedZ, 4);
        }

        [Fact]
        public void Cabin_UsesOwningShaftPlan_IgnoringOwnCell()
        {
            var shaft = new LiftShaftPlan(RailX, RailZ, ModuleX, ModuleZ, 0);

            bool ok = LiftPlanSource.TryPose(LiftPartKind.Cabin, RailX + 4, RailY, RailZ + 3,
                1, 1, 2, in shaft, out float x, out float y, out float z, out int facing);

            Assert.True(ok);
            Assert.Equal(103.0f, x, 4);
            Assert.Equal(202.5f, z, 4);
            Assert.Equal(RailY, y, 4);
            Assert.Equal(0, facing);
        }

        [Fact]
        public void Cabin_WithoutShaft_Fails()
        {
            bool ok = LiftPlanSource.TryPose(LiftPartKind.Cabin, 10, 2, 20,
                ModuleX, ModuleZ, 0, in NoShaft, out _, out _, out _, out _);

            Assert.False(ok);
        }

        [Fact]
        public void Rail_WithoutShaft_StillResolves()
        {
            bool ok = LiftPlanSource.TryPose(LiftPartKind.Rail, RailX, RailY, RailZ,
                ModuleX, ModuleZ, 0, in NoShaft, out float x, out _, out float z, out _);

            Assert.True(ok);
            Assert.Equal(103.0f, x, 4);
            Assert.Equal(202.5f, z, 4);
        }

        [Fact]
        public void ZeroModule_Fails()
        {
            bool ok = LiftPlanSource.TryPose(LiftPartKind.Rail, RailX, RailY, RailZ,
                0, 0, 0, in NoShaft, out _, out _, out _, out _);

            Assert.False(ok);
        }
    }
}
