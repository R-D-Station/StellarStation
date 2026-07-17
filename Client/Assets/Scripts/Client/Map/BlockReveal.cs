using Shared.World.Blocks;

namespace Client.Map
{
    public sealed class BlockReveal
    {
        public const float Budget = 10f;
        public const float VerticalStep = 1.5f;

        private readonly int _window;
        private readonly float[] _budget;
        private readonly int[] _srcY;
        private readonly int[] _queue;
        private int _originX, _originZ;
        private int _head, _tail;

        public BlockReveal(int window)
        {
            _window = window;
            _budget = new float[window * window];
            _srcY = new int[window * window];
            _queue = new int[window * window];
        }

        public void Begin(int originX, int originZ)
        {
            _originX = originX;
            _originZ = originZ;
            _head = 0;
            _tail = 0;
            for (int i = 0; i < _budget.Length; i++)
            {
                _budget[i] = 0f;
                _srcY[i] = int.MaxValue;
            }
        }

        public void Seed(int x, int z, int srcY)
        {
            int lx = x - _originX, lz = z - _originZ;
            if (lx < 0 || lz < 0 || lx >= _window || lz >= _window)
                return;
            int idx = lz * _window + lx;
            if (_budget[idx] >= Budget)
            {
                if (_srcY[idx] == int.MaxValue || srcY > _srcY[idx])
                    _srcY[idx] = srcY;
                return;
            }
            _budget[idx] = Budget;
            _srcY[idx] = srcY;
            if (_tail < _queue.Length)
                _queue[_tail++] = idx;
        }

        public void Spread()
        {
            int w = _window;
            while (_head < _tail)
            {
                int idx = _queue[_head++];
                float next = _budget[idx] - 1f;
                if (next <= 0f)
                    continue;
                int lx = idx % w, lz = idx / w;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0)
                            continue;
                        int nx = lx + dx, nz = lz + dz;
                        if (nx < 0 || nz < 0 || nx >= w || nz >= w)
                            continue;
                        int n = nz * w + nx;
                        if (_budget[n] >= next)
                            continue;
                        _budget[n] = next;
                        _srcY[n] = _srcY[idx];
                        if (_tail < _queue.Length)
                            _queue[_tail++] = n;
                    }
            }
        }

        public void Recompute(BlockGrid grid, int[] cutStartY, int originX, int originZ)
        {
            Begin(originX, originZ);
            int w = _window;
            for (int lz = 0; lz < w; lz++)
                for (int lx = 0; lx < w; lx++)
                {
                    if (cutStartY[lz * w + lx] != int.MaxValue)
                        continue;
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        bool seeded = false;
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dz == 0)
                                continue;
                            int nx = lx + dx, nz = lz + dz;
                            if (nx < 0 || nz < 0 || nx >= w || nz >= w)
                                continue;
                            int ny = cutStartY[nz * w + nx];
                            if (ny == int.MaxValue)
                                continue;
                            if (grid.GetBlock(originX + lx, ny, originZ + lz) == 0)
                            {
                                Seed(originX + lx, originZ + lz, ny);
                                seeded = true;
                                break;
                            }
                        }
                        if (seeded)
                            break;
                    }
                }
            Spread();
        }

        public float Alpha(int x, int y, int z)
        {
            int lx = x - _originX, lz = z - _originZ;
            if (lx < 0 || lz < 0 || lx >= _window || lz >= _window)
                return 0f;
            int idx = lz * _window + lx;
            float b = _budget[idx];
            if (b <= 0f)
                return 0f;
            int sy = _srcY[idx];
            float a = (b - (y > sy ? (y - sy) * VerticalStep : 0f)) / Budget;
            return a < 0f ? 0f : (a > 1f ? 1f : a);
        }
    }
}
