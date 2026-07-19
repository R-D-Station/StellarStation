namespace Server.Network
{
    /// <summary>Предмет на земле как сущность общего реестра (_entities). Позиция — ДИСКРЕТНАЯ ЯЧЕЙКА CellX/CellY/Z
    /// (этаж сейчас, блок потом). IWorldEntity.X/Y — float-каст ячейки для переиспользования InInterest (PVS); суб-ячейковой высоты нет.</summary>
    public sealed class GroundItemEntity : IWorldEntity
    {
        public int NetId { get; }
        public ushort ItemDefId { get; set; }
        public byte StackCount { get; set; }
        public int CellX { get; set; }
        public int CellY { get; set; }
        public int Z { get; set; }
        public byte Placement { get; set; }

        // IWorldEntity: float-позиция = ячейка (для InInterest). CellX/CellY, чтобы не путать с float X/Y интерфейса.
        public float X => CellX;
        public float Y => CellY;

        public GroundItemEntity(int netId, ushort itemDefId, byte stackCount, int cellX, int cellY, int z, byte placement = 0)
        {
            NetId = netId;
            ItemDefId = itemDefId;
            StackCount = stackCount;
            CellX = cellX;
            CellY = cellY;
            Z = z;
            Placement = placement;
        }
    }
}
