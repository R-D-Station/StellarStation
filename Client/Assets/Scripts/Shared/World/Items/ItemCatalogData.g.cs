namespace Shared.World.Items
{
    public static class ItemCatalogData
    {
        public static bool TryGetEquipSlot(ushort defId, out SlotCategory slot)
        {
            slot = default;
            switch (defId)
            {
                default: return false;
            }
        }
    }
}
