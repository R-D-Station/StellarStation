namespace Shared.World.Items
{
    /// <summary>Фиксированные индексы слотов инвентаря игрока; руки — строго индексы 0/1 (ActiveHand их же адресует).</summary>
    public static class InventorySlot
    {
        public const byte HandLeft = 0;
        public const byte HandRight = 1;
        public const byte PocketLeft = 2;
        public const byte PocketRight = 3;
        public const byte Belt = 4;
        public const byte Back = 5;

        public const byte SlotCount = 6;
    }
}
