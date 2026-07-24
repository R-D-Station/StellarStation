using Shared.World.Items;

namespace ServerTests.Shared.World.Items
{
    public class SlotCategoryTests
    {
        [Theory]
        [InlineData(SlotCategory.Hand, 2)]
        [InlineData(SlotCategory.Backpack, 1)]
        [InlineData(SlotCategory.Belt, 1)]
        [InlineData(SlotCategory.Ear, 2)]
        [InlineData(SlotCategory.Eye, 2)]
        [InlineData(SlotCategory.Glove, 2)]
        [InlineData(SlotCategory.Head, 2)]
        [InlineData(SlotCategory.IdCard, 1)]
        [InlineData(SlotCategory.Mask, 1)]
        [InlineData(SlotCategory.Neck, 1)]
        [InlineData(SlotCategory.Pocket, 2)]
        [InlineData(SlotCategory.Boot, 2)]
        [InlineData(SlotCategory.Jumpsuit, 1)]
        [InlineData(SlotCategory.Suit, 1)]
        [InlineData(SlotCategory.Uniform, 1)]
        public void DefaultCount_MatchesPhase1Table(SlotCategory cat, int expected)
        {
            Assert.Equal((byte)expected, InventorySlot.DefaultCount(cat));
        }

        [Fact]
        public void CategoryCount_Is15()
        {
            Assert.Equal(15, InventorySlot.CategoryCount);
        }

        [Fact]
        public void IsValid_BoundsByDefaultCount()
        {
            Assert.True(InventorySlot.IsValid(SlotCategory.Hand, 0));
            Assert.True(InventorySlot.IsValid(SlotCategory.Hand, 1));
            Assert.False(InventorySlot.IsValid(SlotCategory.Hand, 2));
            Assert.True(InventorySlot.IsValid(SlotCategory.Backpack, 0));
            Assert.False(InventorySlot.IsValid(SlotCategory.Backpack, 1));
            Assert.True(InventorySlot.IsValid(SlotCategory.Uniform, 0));
            Assert.False(InventorySlot.IsValid(SlotCategory.Uniform, 1));
        }

        [Fact]
        public void IsValid_RejectsGarbageCategory()
        {
            Assert.False(InventorySlot.IsValid((SlotCategory)200, 0));
            Assert.Equal((byte)0, InventorySlot.DefaultCount((SlotCategory)200));
        }
    }
}
