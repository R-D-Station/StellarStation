namespace Shared.World.Items
{
    /// <summary>Раскладка слотов по категориям (фикс-таблица); известна обеим сторонам, по проводу не шлётся.</summary>
    public static class InventorySlot
    {
        public const int CategoryCount = 15;
        public const byte HandCount = 2;

        /// <summary>Кол-во слотов категории (0 для непокрытых значений enum, напр. None/Inherit).</summary>
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
            SlotCategory.Uniform => 1,
            _ => 0
        };

        /// <summary>Валиден ли адрес (cat, index) — категория в диапазоне и index в её DefaultCount.</summary>
        public static bool IsValid(SlotCategory cat, byte index) => (byte)cat < CategoryCount && index < DefaultCount(cat);
    }
}
