using Shared.World.Blocks;

namespace Client.Map
{
    /// <summary>Кольцевая multi-source волна проявления в скользящем окне вокруг игрока (0 аллокаций/кадр); один экземпляр — один канал (эвристика ИЛИ зона).</summary>
    public sealed class BlockReveal
    {
        public const float Budget = 10f;
        public const float VerticalStep = 1.5f;

        private readonly int _window;
        private readonly float[] _budget;
        private readonly int[] _srcY;
        private readonly ushort[] _seedZone;
        private readonly int[] _queue;
        private int _originX, _originZ;
        private int _head, _tail;
        private float _budgetMax = Budget;
        private float _vertStep = VerticalStep;

        /// <summary>Задать дальность (бюджет волны) и вертикальный шаг угасания (клампы: дальность ≥1, шаг ≥0).</summary>
        public void Configure(float distance, float vertical)
        {
            _budgetMax = distance < 1f ? 1f : distance;
            _vertStep = vertical < 0f ? 0f : vertical;
        }

        public BlockReveal(int window)
        {
            _window = window;
            _budget = new float[window * window];
            _srcY = new int[window * window];
            _seedZone = new ushort[window * window];
            _queue = new int[window * window];
        }

        /// <summary>Сбросить окно на новый центр (обнуляет бюджет/очередь) — звать перед Seed/Recompute.</summary>
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
                _seedZone[i] = 0;
            }
        }

        /// <summary>Посеять источник волны без канала зоны (эвристический путь) — обёртка над Seed(zone: 0).</summary>
        public void Seed(int x, int z, int srcY) => Seed(x, z, srcY, 0);

        /// <summary>Посеять источник волны в очередь на максимальный бюджет; zone метит канал (0 = эвристика).</summary>
        public void Seed(int x, int z, int srcY, ushort zone)
        {
            int lx = x - _originX, lz = z - _originZ;
            if (lx < 0 || lz < 0 || lx >= _window || lz >= _window)
                return;
            int idx = lz * _window + lx;
            if (_budget[idx] >= _budgetMax)
            {
                if (_seedZone[idx] == zone && (_srcY[idx] == int.MaxValue || srcY > _srcY[idx]))
                    _srcY[idx] = srcY;
                return;
            }
            _budget[idx] = _budgetMax;
            _srcY[idx] = srcY;
            _seedZone[idx] = zone;
            if (_tail < _queue.Length)
                _queue[_tail++] = idx;
        }

        /// <summary>Разогнать волну по 8-соседству от посеянных ячеек, затухая на 1 за шаг (BFS по кольцевой очереди, без аллокаций).</summary>
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
                        _seedZone[n] = _seedZone[idx];
                        if (_tail < _queue.Length)
                            _queue[_tail++] = n;
                    }
            }
        }

        /// <summary>Эвристический путь целиком: детект вертикальных проёмов у среза (Begin+сид+Spread) — открытая колонна рядом со скрытой, воздух на уровне среза.</summary>
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

        /// <summary>Альфа эвристического канала (zone-агностик) в ячейке: budget/vertStep-затухание от источника, clamp[0..1].</summary>
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
            float a = (b - (y > sy ? (y - sy) * _vertStep : 0f)) / _budgetMax;
            return a < 0f ? 0f : (a > 1f ? 1f : a);
        }

        /// <summary>Zone-метка ячейки, засеявшей волну сюда (0 = не засеяно) — цель волны для потолка с беззонным верхом.</summary>
        public ushort ZoneAt(int x, int z)
        {
            int lx = x - _originX, lz = z - _originZ;
            if (lx < 0 || lz < 0 || lx >= _window || lz >= _window)
                return 0;
            return _seedZone[lz * _window + lx];
        }

        /// <summary>Y источника волны в ячейке (MinValue вне окна/не засеяно) — различает вертикальный/боковой стык.</summary>
        public int SrcYAt(int x, int z)
        {
            int lx = x - _originX, lz = z - _originZ;
            if (lx < 0 || lz < 0 || lx >= _window || lz >= _window)
                return int.MinValue;
            int idx = lz * _window + lx;
            return _budget[idx] > 0f ? _srcY[idx] : int.MinValue;
        }

        /// <summary>Альфа зонного канала: 0, если ячейка засеяна не под этой zone (иначе то же budget/vertStep-затухание).</summary>
        public float AlphaFor(int x, int y, int z, ushort zone)
        {
            if (zone == 0)
                return 0f;
            int lx = x - _originX, lz = z - _originZ;
            if (lx < 0 || lz < 0 || lx >= _window || lz >= _window)
                return 0f;
            int idx = lz * _window + lx;
            if (_seedZone[idx] != zone)
                return 0f;
            float b = _budget[idx];
            if (b <= 0f)
                return 0f;
            int sy = _srcY[idx];
            float a = (b - (y > sy ? (y - sy) * _vertStep : 0f)) / _budgetMax;
            return a < 0f ? 0f : (a > 1f ? 1f : a);
        }
    }
}
