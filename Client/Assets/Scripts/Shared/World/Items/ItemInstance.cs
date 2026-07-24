namespace Shared.World.Items
{
    /// <summary>Данные предмета (engine-agnostic): одинаковы на земле и в инвентаре (инвентарь — 4.5).</summary>
    // Позиция — СВОБОДНЫЙ float X/Y/Z (F1): X план-X, Y план-Z (Unity Z), Z высота. Клетка выводится через floor()
    // ОДИНАКОВО на сервере и клиенте (индекс препятствий, reach). Коллизия пока cell-keyed (floor обеих сторон) —
    // world-AABB унификация в F1b. GROUND-stream shape only; Placement — ТОЛЬКО при Location==Ground, Held/Inside=0.
    // Held НИКОГДА не идёт через ItemInstance/WriteItem — живёт как HeldItem в слоте владельца (InventorySync).
    public struct ItemInstance
    {
        public int NetId;
        public ushort ItemDefId;
        public byte StackCount;
        public float X;
        public float Y;
        public float Z;
        public byte Placement; // РЕЗЕРВ: суб-ячейковое размещение (пол/стол/стопка) — block-world-ready; в MVP-wire не идёт
        public byte Open;
    }
}
