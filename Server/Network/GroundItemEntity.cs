namespace Server.Network
{
    /// <summary>Предмет на земле как сущность общего реестра (_entities). Позиция — ДИСКРЕТНАЯ ЯЧЕЙКА CellX/CellY/Z
    /// (этаж сейчас, блок потом). IWorldEntity.X/Y — float-каст ячейки для переиспользования InInterest (PVS); суб-ячейковой высоты нет.</summary>
    public sealed class GroundItemEntity : IWorldEntity
    {
        public int NetId { get; }
        public ushort ItemDefId { get; set; }
        public byte StackCount { get; set; }
        public float CellX { get; private set; }
        public float CellY { get; private set; }
        public float Z { get; private set; }
        public byte Placement { get; set; }

        // IWorldEntity: float-позиция = ячейка (для InInterest). CellX/CellY, чтобы не путать с float X/Y интерфейса.
        public float X => CellX;
        public float Y => CellY;
        int IWorldEntity.Z => (int)System.MathF.Floor(Z);

        internal void MoveTo(float x, float y, float z)
        {
            CellX = x;
            CellY = y;
            Z = z;
        }

        public GroundItemEntity(int netId, ushort itemDefId, byte stackCount, float cellX, float cellY, float z, byte placement = 0)
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
