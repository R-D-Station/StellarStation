namespace Shared.World.Items
{
    /// <summary>Адрес слота инвентаря (14 категорий); None/Inherit — сентинелы EquipSlot у ItemProto, НЕ адреса слотов.</summary>
    public enum SlotCategory : byte
    {
        Hand,
        Backpack,
        Belt,
        Ear,
        Eye,
        Glove,
        Head,
        IdCard,
        Mask,
        Neck,
        Pocket,
        Boot,
        Jumpsuit,
        Suit,
        Uniform,
        None,
        Inherit
    }
}
