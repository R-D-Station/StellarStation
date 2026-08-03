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

        /// <summary>Ключ КЛЕТКИ (21 бит на ось). НЕ путать с <see cref="Key"/>/<see cref="KeyOfBlock"/> — те дают ключ СЕКЦИИ 16³.</summary>
        public static long PackCell(int x, int y, int z)
            => ((long)(x & 0x1FFFFF)) | ((long)(y & 0x1FFFFF) << 21) | ((long)(z & 0x1FFFFF) << 42);

        public static void UnpackCell(long key, out int x, out int y, out int z)
        {
            x = SignExtend21((int)(key & 0x1FFFFF));
            y = SignExtend21((int)((key >> 21) & 0x1FFFFF));
            z = SignExtend21((int)((key >> 42) & 0x1FFFFF));
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

        /// <summary>Записать бейк-байт (авторская разметка редактора). false — если не изменился либо нет секции.</summary>
        public bool SetBake(int x, int y, int z, byte bake)
        {
            CheckWriteBounds(y);
            long key = Key(FloorDiv(x, ChunkSection.Size), FloorDiv(y, ChunkSection.Size), FloorDiv(z, ChunkSection.Size));
            if (!_sections.TryGetValue(key, out var s) || !s.SetBake(LocalIndex(x, y, z), bake))
                return false;
            _dirty.Add(key);
            return true;
        }

        public bool TryGetStructOffset(int x, int y, int z, out int dx, out int dy, out int dz)
        {
            dx = dy = dz = 0;
            if (!InBounds(y))
                return false;
            var s = GetSection(FloorDiv(x, ChunkSection.Size), FloorDiv(y, ChunkSection.Size), FloorDiv(z, ChunkSection.Size));
            ushort packed = s?.GetStruct(LocalIndex(x, y, z)) ?? (ushort)0;
            if (packed == 0)
                return false;
            MultiBlock.UnpackOffset(packed, out dx, out dy, out dz);
            return true;
        }

        public bool SetStructOffset(int x, int y, int z, int dx, int dy, int dz)
        {
            if (!MultiBlock.OffsetInRange(dx, dy, dz))
                return false;
            CheckWriteBounds(y);
            long key = Key(FloorDiv(x, ChunkSection.Size), FloorDiv(y, ChunkSection.Size), FloorDiv(z, ChunkSection.Size));
            ushort packed = dx == 0 && dy == 0 && dz == 0 ? (ushort)0 : (ushort)MultiBlock.PackOffset(dx, dy, dz);
            if (!_sections.TryGetValue(key, out var s) || !s.SetStruct(LocalIndex(x, y, z), packed))
                return false;
            _dirty.Add(key);
            return true;
        }

        /// <summary>Клетка — якорь мульти-блока: тип с PartCount &gt; 1 и БЕЗ записи слоя.</summary>
        public bool IsStructAnchor(int x, int y, int z)
        {
            var info = BlockCatalog.Get(GetBlock(x, y, z));
            return info.PartCount > 1 && !TryGetStructOffset(x, y, z, out _, out _, out _);
        }

        /// <summary>Якорь структуры, которой принадлежит клетка (сама клетка, если якорь). false — не структура либо орфан.</summary>
        public bool AnchorOf(int x, int y, int z, out int ax, out int ay, out int az)
        {
            ax = x; ay = y; az = z;
            ushort type = GetBlock(x, y, z);
            if (BlockCatalog.Get(type).PartCount <= 1)
                return false;
            if (!TryGetStructOffset(x, y, z, out int dx, out int dy, out int dz))
                return true;

            ax = x - dx; ay = y - dy; az = z - dz;
            if (GetBlock(ax, ay, az) == type)
                return true;

            // Секции якоря нет (клиентский стрим: структура через границу 16³) — это НЕ повреждение, орфаном не считаем.
            bool anchorSectionLoaded = InBounds(ay) && GetSection(
                FloorDiv(ax, ChunkSection.Size), FloorDiv(ay, ChunkSection.Size), FloorDiv(az, ChunkSection.Size)) != null;
            if (anchorSectionLoaded)
                OrphanStructReads++;
            ax = x; ay = y; az = z;
            return false;
        }

        /// <summary>Записей слоя, приведших к якорю чужого типа (диагностика; печатает потребитель — Shared молчит).</summary>
        public int OrphanStructReads { get; private set; }

        /// <summary>Разместить мульти-блок якорем в (x,y,z): все части + facing + записи слоя не-якорным. false — занято/вне пределов.</summary>
        public bool PlaceMultiBlock(int x, int y, int z, ushort type, int facing)
        {
            var info = BlockCatalog.Get(type);
            int parts = MultiBlock.PartCount(info.SizeX, info.SizeY, info.SizeZ);
            for (int p = 0; p < parts; p++)
            {
                MultiBlock.PartWorldOffset(p, info.SizeX, info.SizeZ, facing, out int dx, out int dy, out int dz);
                if (!MultiBlock.OffsetInRange(dx, dy, dz) || !InBounds(y + dy) || GetBlock(x + dx, y + dy, z + dz) != 0)
                    return false;
            }

            byte state = BlockState.WithFacing(0, facing);
            for (int p = 0; p < parts; p++)
            {
                MultiBlock.PartWorldOffset(p, info.SizeX, info.SizeZ, facing, out int dx, out int dy, out int dz);
                SetBlock(x + dx, y + dy, z + dz, type);
                SetState(x + dx, y + dy, z + dz, state);
                if (dx != 0 || dy != 0 || dz != 0)
                    SetStructOffset(x + dx, y + dy, z + dz, dx, dy, dz);
            }
            return true;
        }

        /// <summary>Снести структуру от ЛЮБОЙ её клетки (блоки+state+записи слоя). false — не структура.</summary>
        public bool EraseMultiBlock(int x, int y, int z)
        {
            ushort type = GetBlock(x, y, z);
            var info = BlockCatalog.Get(type);
            if (info.PartCount <= 1)
                return false;
            if (!AnchorOf(x, y, z, out int ax, out int ay, out int az))
                return false;

            int facing = BlockState.GetFacing(GetState(ax, ay, az));
            int parts = MultiBlock.PartCount(info.SizeX, info.SizeY, info.SizeZ);
            for (int p = 0; p < parts; p++)
            {
                MultiBlock.PartWorldOffset(p, info.SizeX, info.SizeZ, facing, out int dx, out int dy, out int dz);
                int px = ax + dx, py = ay + dy, pz = az + dz;
                if (!InBounds(py))
                    continue;
                SetStructOffset(px, py, pz, 0, 0, 0); // до проверки типа: осиротевшая запись не должна пережить снос
                if (GetBlock(px, py, pz) != type)
                    continue;
                SetBlock(px, py, pz, 0);
            }
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

        /// <summary>Стереть все спавны предмета в ячейке (ластик редактора). true — если что-то удалено.</summary>
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

        // Прямой доступ к списку — только для сериализатора (обход IReadOnlyList-обёртки).
        internal List<ItemSpawn> ItemSpawnList => _itemSpawns;

        /// <summary>Ключ секции, содержащей блок (для стрима/дельт).</summary>
        public static long KeyOfBlock(int x, int y, int z)
            => Key(FloorDiv(x, ChunkSection.Size), FloorDiv(y, ChunkSection.Size), FloorDiv(z, ChunkSection.Size));
    }
}
