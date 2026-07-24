namespace Shared.World.Blocks
{
    /// <summary>Точка начального спавна предмета на карте (не блок — при загрузке становится entity).</summary>
    public readonly struct ItemSpawn
    {
        // Оси Unity: X/Z — план, Y — высота (ячейка блока, куда кладётся предмет).
        public readonly int X, Y, Z;
        public readonly ushort DefId;
        public readonly byte Stack;

        public ItemSpawn(int x, int y, int z, ushort defId, byte stack)
        {
            X = x; Y = y; Z = z;
            DefId = defId;
            Stack = stack;
        }
    }
}
