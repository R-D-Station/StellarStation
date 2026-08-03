namespace Shared.World.Blocks
{
    /// <summary>Чтение типа и state блока по координате (оси Unity, Y — высота). Сервер — BlockGrid напрямую;
    /// клиент — StreamedBlockWorld: нестримленная секция читается как solid-стопор (в.20).</summary>
    public interface IBlockSampler
    {
        ushort GetBlock(int x, int y, int z);

        /// <summary>State-байт (facing/Open — коллизия дверей зависит от него).</summary>
        byte GetState(int x, int y, int z);

        /// <summary>Смещение клетки до якоря её структуры (слой принадлежности); false — якорь либо одиночный блок.</summary>
        bool TryGetStructOffset(int x, int y, int z, out int dx, out int dy, out int dz);
    }

    /// <summary>Константы блочного стрима — в Shared (обе стороны считают окно одинаково, в.42-стиль).</summary>
    public static class BlockStreaming
    {
        /// <summary>Горизонтальный радиус окна в секциях (окно (2R+1)²).</summary>
        public const int RadiusSections = 2;

        /// <summary>Вертикальный охват окна в секциях (±H).</summary>
        public const int HeightSections = 2;

        /// <summary>Клиентское «известное» окно уже, чем серверное (страховка от гонки на кромке).</summary>
        public const int KnownWindowMargin = 1;

        /// <summary>Тип-заглушка «неизвестный объём» у клиента: полный куб (стопор фронтира).</summary>
        public const ushort UnknownBlock = ushort.MaxValue;
    }
}
