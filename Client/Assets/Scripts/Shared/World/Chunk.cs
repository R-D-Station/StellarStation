namespace Shared.World
{
    /// <summary>
    /// Плотный квадрат тайлов Size×Size на одном этаже z. Единица хранения и
    /// репликации; отсутствующий чанк = весь космос. Вертикаль — чанки с разным z.
    /// </summary>
    public sealed class Chunk
    {
        public const int Size = 16;
        public const int TileCount = Size * Size;

        public readonly int ChunkX;
        public readonly int ChunkY;
        public readonly int Z;

        private readonly Tile[] _tiles;

        public Chunk(int chunkX, int chunkY, int z)
        {
            ChunkX = chunkX;
            ChunkY = chunkY;
            Z = z;
            _tiles = new Tile[TileCount];
            for (int i = 0; i < TileCount; i++)
                _tiles[i] = Tile.Space;
        }

        /// <summary>Локальные координаты внутри чанка (0..Size-1).</summary>
        public Tile this[int localX, int localY]
        {
            get => _tiles[localY * Size + localX];
            set => _tiles[localY * Size + localX] = value;
        }

        /// <summary>Прямой доступ к массиву — для сериализации, без копий.</summary>
        public Tile[] Raw => _tiles;
    }
}