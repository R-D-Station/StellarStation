namespace Shared.World.Items
{
    public static class InventorySlot
    {
        public const int CategoryCount = 14;
        public const byte HandCount = 2;

        public static byte DefaultCount(SlotCategory cat) => cat switch
        {
            SlotCategory.Hand => 2,
            SlotCategory.Backpack => 1,
            SlotCategory.Belt => 1,
            SlotCategory.Ear => 2,
            SlotCategory.Eye => 2,
            SlotCategory.Glove => 2,
            SlotCategory.Head => 2,
            SlotCategory.IdCard => 1,
            SlotCategory.Mask => 1,
            SlotCategory.Neck => 1,
            SlotCategory.Pocket => 2,
            SlotCategory.Boot => 2,
            SlotCategory.Jumpsuit => 1,
            SlotCategory.Suit => 1,
            _ => 0
        };

        public static bool IsValid(SlotCategory cat, byte index) => (byte)cat < CategoryCount && index < DefaultCount(cat);
    }
}
