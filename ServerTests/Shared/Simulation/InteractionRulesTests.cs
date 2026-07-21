using Shared.Simulation;

namespace ServerTests.Shared.Simulation
{
    /// <summary>InteractionRules.InReach: chebyshev ≤ 1 на своём этаже. Свой тайл/сосед/диагональ → true;
    /// дальше 1 или другой Z → false.</summary>
    public class InteractionRulesTests
    {
        [Fact]
        public void SameTile_InReach()
        {
            Assert.True(InteractionRules.InReach(5, 5, 0, 5, 5, 0));
        }

        [Fact]
        public void OrthogonalNeighbor_InReach()
        {
            Assert.True(InteractionRules.InReach(5, 5, 0, 6, 5, 0));
            Assert.True(InteractionRules.InReach(5, 5, 0, 5, 4, 0));
        }

        [Fact]
        public void DiagonalNeighbor_InReach()
        {
            Assert.True(InteractionRules.InReach(5, 5, 0, 6, 6, 0));
            Assert.True(InteractionRules.InReach(5, 5, 0, 4, 4, 0));
        }

        [Fact]
        public void TwoTilesAway_NotInReach()
        {
            Assert.False(InteractionRules.InReach(5, 5, 0, 7, 5, 0));
            Assert.False(InteractionRules.InReach(5, 5, 0, 7, 7, 0));
        }

        [Fact]
        public void DifferentZ_NotInReach()
        {
            Assert.False(InteractionRules.InReach(5, 5, 0, 5, 5, 1));
            Assert.False(InteractionRules.InReach(5, 5, 1, 5, 5, 0));
        }

        [Fact]
        public void Range_IsOne()
        {
            Assert.Equal(1, InteractionRules.InteractionRange);
        }

        [Fact]
        public void Blocks_SameCell_InReach()
        {
            Assert.True(InteractionRules.InReachBlocks(5, 5, 2, 5, 5, 2));
        }

        [Fact]
        public void Blocks_OneCellUpOrDown_InReach()
        {
            Assert.True(InteractionRules.InReachBlocks(5, 5, 2, 5, 5, 3));
            Assert.True(InteractionRules.InReachBlocks(5, 5, 2, 5, 5, 1));
            Assert.True(InteractionRules.InReachBlocks(5, 5, 2, 6, 5, 3)); // диагональ плана + 1 вверх
        }

        [Fact]
        public void Blocks_TwoCellsVertical_NotInReach()
        {
            Assert.False(InteractionRules.InReachBlocks(5, 5, 2, 5, 5, 4));
            Assert.False(InteractionRules.InReachBlocks(5, 5, 4, 5, 5, 2));
        }

        [Fact]
        public void Blocks_TwoTilesPlan_NotInReach()
        {
            Assert.False(InteractionRules.InReachBlocks(5, 5, 2, 7, 5, 2));
        }
    }
}
