namespace Shared.World.Items
{
    /// <summary>Данные предмета (engine-agnostic): одинаковы на земле и в инвентаре (инвентарь — 4.5).</summary>
    // Позиция — ДИСКРЕТНАЯ ЯЧЕЙКА int X/Y/Z (этаж сейчас, блок потом): логика НИКОГДА не делает z·высота-математику,
    // высоту маппит только выбрасываемый клиент-рендер. Тогда переход этаж→блок — релейбл, а не переписывание.
    public struct ItemInstance
    {
        public int NetId;
        public ushort ItemDefId;
        public byte StackCount;
        public int X;
        public int Y;
        public int Z;
        public byte Placement; // РЕЗЕРВ: суб-ячейковое размещение (пол/стол/стопка) — block-world-ready; в MVP-wire не идёт
    }
}
