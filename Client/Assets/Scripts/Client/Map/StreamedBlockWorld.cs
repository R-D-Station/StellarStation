using System.Collections.Generic;
using Shared.Simulation.Blocks;
using Shared.World.Blocks;

namespace Client.Map
{
    /// <summary>
    /// Клиентский взгляд на стримимый блок-мир: полученные секции читаются из грида, НЕполученные —
    /// solid-стопор (в.20: движение упирается в фронтир). Пустые секции не приходят вовсе, поэтому
    /// «известное» = полученные секции ∪ окно вокруг игрока (уже серверного на KnownWindowMargin).
    /// </summary>
    public sealed class StreamedBlockWorld : IBlockSampler
    {
        private readonly BlockGrid _grid;
        private readonly HashSet<long> _received = new();
        private int _centerCx, _centerCy, _centerCz;

        public StreamedBlockWorld(BlockGrid grid) => _grid = grid;

        public BlockGrid Grid => _grid;

        /// <summary>Центр «известного» окна — предсказанная позиция игрока (обновлять каждый тик).</summary>
        public void SetCenter(float x, float y, float z)
        {
            _centerCx = FloorDiv((int)System.MathF.Floor(x), ChunkSection.Size);
            _centerCy = FloorDiv((int)System.MathF.Floor(y), ChunkSection.Size);
            _centerCz = FloorDiv((int)System.MathF.Floor(z), ChunkSection.Size);
        }

        public void MarkReceived(int cx, int cy, int cz) => _received.Add(BlockGrid.Key(cx, cy, cz));

        public void Forget(int cx, int cy, int cz) => _received.Remove(BlockGrid.Key(cx, cy, cz));

        public ushort GetBlock(int x, int y, int z)
        {
            if (!BlockGrid.InBounds(y))
                return 0;

            int cx = FloorDiv(x, ChunkSection.Size);
            int cy = FloorDiv(y, ChunkSection.Size);
            int cz = FloorDiv(z, ChunkSection.Size);

            if (_received.Contains(BlockGrid.Key(cx, cy, cz)) || InKnownWindow(cx, cy, cz))
                return _grid.GetBlock(x, y, z);
            return BlockStreaming.UnknownBlock; // фронтир: неизвестно = solid
        }

        public byte GetState(int x, int y, int z) => _grid.GetState(x, y, z); // неизвестное = 0 и так

        public bool TryGetStructOffset(int x, int y, int z, out int dx, out int dy, out int dz)
            => _grid.TryGetStructOffset(x, y, z, out dx, out dy, out dz);

        // Пустые секции не стримятся: внутри суженного окна отсутствие секции = воздух, а не «неизвестно».
        private bool InKnownWindow(int cx, int cy, int cz)
        {
            int r = BlockStreaming.RadiusSections - BlockStreaming.KnownWindowMargin;
            int h = BlockStreaming.HeightSections - BlockStreaming.KnownWindowMargin;
            return System.Math.Abs(cx - _centerCx) <= r
                && System.Math.Abs(cz - _centerCz) <= r
                && System.Math.Abs(cy - _centerCy) <= h;
        }

        private static int FloorDiv(int a, int b)
        {
            int q = a / b;
            if ((a % b != 0) && ((a < 0) != (b < 0))) q--;
            return q;
        }
    }

    /// <summary>Формы + заглушка UnknownBlock (полный куб) поверх любого источника форм.</summary>
    public sealed class ShapesWithUnknown : IBlockShapes
    {
        private static readonly BlockBox[] FullBox = { BlockBox.Full };
        private readonly IBlockShapes _inner;

        public ShapesWithUnknown(IBlockShapes inner) => _inner = inner;

        public BlockBox[] GetBoxes(ushort type, byte state)
            => type == BlockStreaming.UnknownBlock ? FullBox : _inner.GetBoxes(type, state);

        public BlockBox[] GetBoxes(ushort type, byte state, IBlockSampler grid, int x, int y, int z)
            => type == BlockStreaming.UnknownBlock ? FullBox : _inner.GetBoxes(type, state, grid, x, y, z);
    }
}
