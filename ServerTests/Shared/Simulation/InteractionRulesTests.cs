using Shared.Simulation;

namespace ServerTests.Shared.Simulation
{
    /// <summary>InteractionRules.InReachBlocks: chebyshev ≤ 1 по плану + вертикальный допуск ±1 блок.</summary>
    public class InteractionRulesTests
    {
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
