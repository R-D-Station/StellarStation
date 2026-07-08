using Shared.World;

namespace ServerTests.Shared.World
{
    public class WallConnectionTests
    {
        [Theory]
        // count 0
        [InlineData(false, false, false, false, WallShape.Single, 0)]
        // count 1 — End по стороне соседа
        [InlineData(true, false, false, false, WallShape.End, 0)]
        [InlineData(false, true, false, false, WallShape.End, 1)]
        [InlineData(false, false, true, false, WallShape.End, 2)]
        [InlineData(false, false, false, true, WallShape.End, 3)]
        // count 2 — противоположные → Straight
        [InlineData(true, false, true, false, WallShape.Straight, 0)]
        [InlineData(false, true, false, true, WallShape.Straight, 1)]
        // count 2 — смежные → Corner по квадранту
        [InlineData(true, true, false, false, WallShape.Corner, 0)]
        [InlineData(false, true, true, false, WallShape.Corner, 1)]
        [InlineData(false, false, true, true, WallShape.Corner, 2)]
        [InlineData(true, false, false, true, WallShape.Corner, 3)]
        // count 3 — T по отсутствующей стороне
        [InlineData(true, true, true, false, WallShape.T, 0)]
        [InlineData(false, true, true, true, WallShape.T, 1)]
        [InlineData(true, false, true, true, WallShape.T, 2)]
        [InlineData(true, true, false, true, WallShape.T, 3)]
        // count 4
        [InlineData(true, true, true, true, WallShape.Cross, 0)]
        public void Resolve_MapsAll16Combinations(bool n, bool e, bool s, bool w, WallShape expectedShape, int expectedSteps)
        {
            var (shape, steps) = WallConnection.Resolve(n, e, s, w);
            Assert.Equal(expectedShape, shape);
            Assert.Equal(expectedSteps, steps);
        }

        [Fact]
        public void End_ProducesAllFourDistinctSteps()
        {
            var steps = new[]
            {
                WallConnection.Resolve(true, false, false, false).rotationSteps,
                WallConnection.Resolve(false, true, false, false).rotationSteps,
                WallConnection.Resolve(false, false, true, false).rotationSteps,
                WallConnection.Resolve(false, false, false, true).rotationSteps,
            };
            Assert.Equal(new[] { 0, 1, 2, 3 }, steps);
        }

        [Fact]
        public void Corner_ProducesAllFourDistinctSteps()
        {
            var steps = new[]
            {
                WallConnection.Resolve(true, true, false, false).rotationSteps,
                WallConnection.Resolve(false, true, true, false).rotationSteps,
                WallConnection.Resolve(false, false, true, true).rotationSteps,
                WallConnection.Resolve(true, false, false, true).rotationSteps,
            };
            Assert.Equal(new[] { 0, 1, 2, 3 }, steps);
        }

        [Fact]
        public void T_ProducesAllFourDistinctSteps()
        {
            var steps = new[]
            {
                WallConnection.Resolve(true, true, true, false).rotationSteps,
                WallConnection.Resolve(false, true, true, true).rotationSteps,
                WallConnection.Resolve(true, false, true, true).rotationSteps,
                WallConnection.Resolve(true, true, false, true).rotationSteps,
            };
            Assert.Equal(new[] { 0, 1, 2, 3 }, steps);
        }

        [Fact]
        public void Straight_ProducesStepsZeroAndOne()
        {
            Assert.Equal(0, WallConnection.Resolve(true, false, true, false).rotationSteps);
            Assert.Equal(1, WallConnection.Resolve(false, true, false, true).rotationSteps);
        }

        [Fact]
        public void SingleAndCross_AreSymmetricWithZeroSteps()
        {
            Assert.Equal((WallShape.Single, 0), WallConnection.Resolve(false, false, false, false));
            Assert.Equal((WallShape.Cross, 0), WallConnection.Resolve(true, true, true, true));
        }
    }
}
