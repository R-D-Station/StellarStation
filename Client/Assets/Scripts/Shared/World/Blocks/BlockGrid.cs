using System;
using System.Collections.Generic;

namespace Shared.World.Blocks
{
    /// <summary>
    /// Разреженная блочная карта: секции 16³ по упакованному ключу (cx,cy,cz); отсутствие секции = Air.
    /// Оси как в Unity: X/Z — план, Y — ВЫСОТА. Мутации — ТОЛЬКО на GameLoop-потоке. Пустые секции
    /// удаляются; dirty-учёт секций — под сейв/сеть.
    /// </summary>
    public sealed class BlockGrid : IBlockSampler
    {
        /// <summary>Вертикальная граница мира: y ∈ [-512, 511].</summary>
        public const int VerticalLimit = 512;

        // План ограничен упаковкой ключа: 21 бит со знаком на ось секций.
        private const int MaxSectionCoord = (1 << 20) - 1;
        private const int MinSectionCoord = -(1 << 20);

        private readonly Dictionary<long, ChunkSection> _sections = new();
        private readonly HashSet<long> _dirty = new();
        private readonly List<ItemSpawn> _itemSpawns = new();

        /// <summary>Хук каскада обновлений соседей (сам каскад — фаза B+): координаты изменившегося блока.</summary>
        public event Action<int, int, int> BlockChanged;

        public IReadOnlyDictionary<long, ChunkSection> Sections => _sections;

        /// <summary>Секции, изменённые с последнего ClearDirty (включая удалённые).</summary>
        public IReadOnlyCollection<long> DirtySections => _dirty;

        public void ClearDirty() => _dirty.Clear();

        /// <summary>Ключ секции — единый codec для индекса, стрима и сейва. 21 бит со знаком на ось; выход = ошибка.</summary>
        public static long Key(int cx, int cy, int cz)
        {
            if (cx < MinSectionCoord || cx > MaxSectionCoord ||
                cy < MinSectionCoord || cy > MaxSectionCoord ||
                cz < MinSectionCoord || cz > MaxSectionCoord)
                throw new ArgumentOutOfRangeException(nameof(cx), $"Секция ({cx},{cy},{cz}) вне 21-битного диапазона ключа.");

            return ((long)(cx & 0x1FFFFF))
                 | ((long)(cy & 0x1FFFFF) << 21)
                 | ((long)(cz & 0x1FFFFF) << 42);
        }

        public static void UnpackKey(long key, out int cx, out int cy, out int cz)
        {
            cx = SignExtend21((int)(key & 0x1FFFFF));
            cy = SignExtend21((int)((key >> 21) & 0x1FFFFF));
            cz = SignExtend21((int)((key >> 42) & 0x1FFFFF));
        }

        private static int SignExtend21(int v) => (v & 0x100000) != 0 ? v | ~0x1FFFFF : v;

        // Floor-деление: корректно для отрицательных координат (обычное / округляет к нулю).
        private static int FloorDiv(int a, int b)
        {
            int q = a / b;
            if ((a % b != 0) && ((a < 0) != (b < 0))) q--;
            return q;
        }

        private static int Mod(int a, int b)
        {
            int r = a % b;
            return r < 0 ? r + b : r;
        }

        /// <summary>Запрос за вертикальную границу мира легален и даёт Air; запись — нет.</summary>
        public static bool InBounds(int y) => y >= -VerticalLimit && y < VerticalLimit;

        public ChunkSection GetSection(int cx, int cy, int cz)
        {
            // Чтение за 21-битный диапазон ключа легально и даёт Air (симметрично вертикали).
            if (cx < MinSectionCoord || cx > MaxSectionCoord ||
                cy < MinSectionCoord || cy > MaxSectionCoord ||
                cz < MinSectionCoord || cz > MaxSectionCoord)
                return null;
            return _sections.TryGetValue(Key(cx, cy, cz), out var s) ? s : null;
        }

        public ushort GetBlock(int x, int y, int z)
        {
            if (!InBounds(y))
                return 0;
            var s = GetSection(FloorDiv(x, ChunkSection.Size), FloorDiv(y, ChunkSection.Size), FloorDiv(z, ChunkSection.Size));
            return s?.GetBlock(LocalIndex(x, y, z)) ?? (ushort)0;
        }

        public ushort GetBlock(BlockCoord c) => GetBlock(c.X, c.Y, c.Z);

        /// <summary>Записать тип блока. true — если изменилось; создаёт/удаляет секцию, метит dirty, зовёт хук.</summary>
        public bool SetBlock(int x, int y, int z, ushort type)
        {
            CheckWriteBounds(y);

            long key = Key(FloorDiv(x, ChunkSection.Size), FloorDiv(y, ChunkSection.Size), FloorDiv(z, ChunkSection.Size));
            if (!_sections.TryGetValue(key, out var s))
            {
                if (type == 0)
                    return false; // Air в пустоту — no-op, секцию не создаём
                s = new ChunkSection();
                _sections[key] = s;
            }

            if (!s.SetBlock(LocalIndex(x, y, z), type))
                return false;

            if (s.IsEmpty)
                _sections.Remove(key);
            _dirty.Add(key);
            BlockChanged?.Invoke(x, y, z);
            return true;
        }

        public byte GetState(int x, int y, int z)
        {
            if (!InBounds(y))
                return 0;
            var s = GetSection(FloorDiv(x, ChunkSection.Size), FloorDiv(y, ChunkSection.Size), FloorDiv(z, ChunkSection.Size));
            return s?.GetState(LocalIndex(x, y, z)) ?? (byte)0;
        }

        /// <summary>Записать state-байт блока. false — если не изменился либо позиция Air/за границей.</summary>
        public bool SetState(int x, int y, int z, byte state)
        {
            CheckWriteBounds(y);

            long key = Key(FloorDiv(x, ChunkSection.Size), FloorDiv(y, ChunkSection.Size), FloorDiv(z, ChunkSection.Size));
            if (!_sections.TryGetValue(key, out var s))
                return false;

            if (!s.SetState(LocalIndex(x, y, z), state))
                return false;

            _dirty.Add(key);
            BlockChanged?.Invoke(x, y, z);
            return true;
        }

        private static void CheckWriteBounds(int y)
        {
            if (!InBounds(y))
                throw new ArgumentOutOfRangeException(nameof(y), $"y={y} вне вертикальной границы мира ±{VerticalLimit}.");
        }

        private static int LocalIndex(int x, int y, int z)
            => ChunkSection.LocalIndex(Mod(x, ChunkSection.Size), Mod(y, ChunkSection.Size), Mod(z, ChunkSection.Size));

        /// <summary>Добавить/заменить готовую секцию (десериализация/стрим). Пустые игнорируются.</summary>
        public void AddSection(int cx, int cy, int cz, ChunkSection section)
        {
            if (section.IsEmpty)
                return;
            _sections[Key(cx, cy, cz)] = section;
        }

        /// <summary>Удалить секцию целиком (клиентский стрим: выгрузка/опустошение). true — если была.</summary>
        public bool RemoveSection(int cx, int cy, int cz) => _sections.Remove(Key(cx, cy, cz));

        public byte GetBake(int x, int y, int z)
        {
            if (!InBounds(y))
                return 0;
            var s = GetSection(FloorDiv(x, ChunkSection.Size), FloorDiv(y, ChunkSection.Size), FloorDiv(z, ChunkSection.Size));
            return s?.GetBake(LocalIndex(x, y, z)) ?? (byte)0;
        }

        /// <summary>Записать бейк-байт (авторская разметка редактора). false — если не изменился либо Air.</summary>
        public bool SetBake(int x, int y, int z, byte bake)
        {
            CheckWriteBounds(y);
            long key = Key(FloorDiv(x, ChunkSection.Size), FloorDiv(y, ChunkSection.Size), FloorDiv(z, ChunkSection.Size));
            if (!_sections.TryGetValue(key, out var s) || !s.SetBake(LocalIndex(x, y, z), bake))
                return false;
            _dirty.Add(key);
            return true;
        }

        public ushort GetZone(int x, int y, int z)
        {
            if (!InBounds(y))
                return 0;
            var s = GetSection(FloorDiv(x, ChunkSection.Size), FloorDiv(y, ChunkSection.Size), FloorDiv(z, ChunkSection.Size));
            return s?.GetZone(LocalIndex(x, y, z)) ?? (ushort)0;
        }

        public bool SetZone(int x, int y, int z, ushort zone)
        {
            CheckWriteBounds(y);
            long key = Key(FloorDiv(x, ChunkSection.Size), FloorDiv(y, ChunkSection.Size), FloorDiv(z, ChunkSection.Size));
            if (!_sections.TryGetValue(key, out var s) || !s.SetZone(LocalIndex(x, y, z), zone))
                return false;
            _dirty.Add(key);
            return true;
        }

        public bool TryGetSeed(int x, int y, int z, out FloorSeed seed)
        {
            seed = default;
            if (!InBounds(y))
                return false;
            var s = GetSection(FloorDiv(x, ChunkSection.Size), FloorDiv(y, ChunkSection.Size), FloorDiv(z, ChunkSection.Size));
            return s != null && s.TryGetSeed(LocalIndex(x, y, z), out seed);
        }

        public bool SetSeed(int x, int y, int z, in FloorSeed seed)
        {
            CheckWriteBounds(y);
            long key = Key(FloorDiv(x, ChunkSection.Size), FloorDiv(y, ChunkSection.Size), FloorDiv(z, ChunkSection.Size));
            if (!_sections.TryGetValue(key, out var s) || !s.SetSeed(LocalIndex(x, y, z), in seed))
                return false;
            _dirty.Add(key);
            return true;
        }

        public bool RemoveSeed(int x, int y, int z)
        {
            if (!InBounds(y))
                return false;
            long key = KeyOfBlock(x, y, z);
            if (!_sections.TryGetValue(key, out var s) || !s.RemoveSeed(LocalIndex(x, y, z)))
                return false;
            _dirty.Add(key);
            return true;
        }

        public System.Collections.Generic.IReadOnlyList<ItemSpawn> ItemSpawns => _itemSpawns;

        public void AddItemSpawn(in ItemSpawn spawn) => _itemSpawns.Add(spawn);

        public bool RemoveItemSpawnsAt(int x, int y, int z)
        {
            bool removed = false;
            for (int i = _itemSpawns.Count - 1; i >= 0; i--)
                if (_itemSpawns[i].X == x && _itemSpawns[i].Y == y && _itemSpawns[i].Z == z)
                {
                    _itemSpawns.RemoveAt(i);
                    removed = true;
                }
            return removed;
        }

        internal List<ItemSpawn> ItemSpawnList => _itemSpawns;

        /// <summary>Ключ секции, содержащей блок (для стрима/дельт).</summary>
        public static long KeyOfBlock(int x, int y, int z)
            => Key(FloorDiv(x, ChunkSection.Size), FloorDiv(y, ChunkSection.Size), FloorDiv(z, ChunkSection.Size));
    }
}
