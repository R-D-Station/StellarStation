using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Shared.World.Blocks
{
    /// <summary>Бинарная сериализация BlockGrid — формат .smap v14, append-only поверх v10/v11/v12/v13 (bake→zone→seeds→item-спавны), детерминированный порядок → побайтовая стабильность.</summary>
    public static class BlockMapSerializer
    {
        public const int Magic = MapSerializer.Magic; // 'SMAP' — общее семейство форматов карт
        public const ushort Version = 15;
        public const ushort MinVersion = 10;

        private const byte EncodingPalette = 0;
        private const byte EncodingRaw = 1;

        public static void Write(Stream stream, BlockGrid grid, byte gridId = 0)
        {
            using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);

            w.Write(Magic);
            w.Write(Version);
            w.Write((byte)1); // gridCount: фаза A пишет ровно один грид (резерв под фазу E)
            w.Write(gridId);

            // Детерминированный порядок секций — по (cy,cz,cx).
            var keys = new List<long>(grid.Sections.Keys);
            keys.Sort(CompareKeys);

            w.Write(keys.Count);
            foreach (long key in keys)
            {
                BlockGrid.UnpackKey(key, out int cx, out int cy, out int cz);
                w.Write(cx);
                w.Write(cy);
                w.Write(cz);
                WriteSection(w, grid.Sections[key]);
            }

            // v13: хвост списка item-спавнов ПОСЛЕ секций (диск-only — в BlockChunkData/wire не входит).
            var spawns = grid.ItemSpawnList;
            w.Write((ushort)spawns.Count);
            for (int i = 0; i < spawns.Count; i++)
            {
                w.Write(spawns[i].X);
                w.Write(spawns[i].Y);
                w.Write(spawns[i].Z);
                w.Write(spawns[i].DefId);
                w.Write(spawns[i].Stack);
            }
        }

        private static int CompareKeys(long a, long b)
        {
            BlockGrid.UnpackKey(a, out int ax, out int ay, out int az);
            BlockGrid.UnpackKey(b, out int bx, out int by, out int bz);
            int c = ay.CompareTo(by);
            if (c != 0) return c;
            c = az.CompareTo(bz);
            return c != 0 ? c : ax.CompareTo(bx);
        }

        /// <summary>Кодек одной секции: диск-формат = wire-формат (переиспользуется BlockChunkData, реш. в.17).</summary>
        public static void WriteSection(BinaryWriter w, ChunkSection s)
        {
            if (s.IsRaw)
            {
                w.Write(EncodingRaw);
                var raw = s.Raw;
                for (int i = 0; i < ChunkSection.BlockCount; i++)
                    w.Write(raw[i]);
            }
            else
            {
                w.Write(EncodingPalette);
                var types = s.Palette.Types;
                w.Write((ushort)types.Count);
                for (int i = 1; i < types.Count; i++) // слот 0 (Air) в палитре всегда — на диск не пишем
                    w.Write(types[i]);
                w.Write(s.Indices, 0, ChunkSection.BlockCount);
            }

            var states = s.States;
            w.Write((ushort)states.Count);
            foreach (var kv in states.OrderBy(p => p.Key))
            {
                w.Write((ushort)kv.Key);
                w.Write(kv.Value);
            }

            var bake = s.Bake; // v11: авторская разметка (потолки/полы-интерьеры)
            w.Write((ushort)bake.Count);
            foreach (var kv in bake.OrderBy(p => p.Key))
            {
                w.Write((ushort)kv.Key);
                w.Write(kv.Value);
            }

            var zone = s.Zone;
            w.Write(s.DefaultZone);
            w.Write((ushort)zone.Count);
            foreach (var kv in zone.OrderBy(p => p.Key))
            {
                w.Write((ushort)kv.Key);
                w.Write(kv.Value);
            }

            var seeds = s.Seeds;
            w.Write((ushort)seeds.Count);
            foreach (var kv in seeds.OrderBy(p => p.Key))
            {
                w.Write((ushort)kv.Key);
                w.Write(kv.Value.Name ?? string.Empty);
                w.Write(kv.Value.Rank);
                w.Write(kv.Value.Floor);
            }

            var structs = s.Struct;
            w.Write((ushort)structs.Count);
            foreach (var kv in structs.OrderBy(p => p.Key))
            {
                w.Write((ushort)kv.Key);
                w.Write(kv.Value);
            }
        }

        public static BlockGrid Read(Stream stream)
        {
            using var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);

            int magic = r.ReadInt32();
            if (magic != Magic)
                throw new InvalidDataException("Not a station map file (bad magic).");

            ushort version = r.ReadUInt16();
            if (version < MinVersion)
                throw new InvalidDataException($"Тайловый формат v{version}: конвертация в блочный мир не поддерживается.");
            if (version > Version)
                throw new InvalidDataException($"Unsupported block map version {version} (expected {MinVersion}..{Version}).");
            _lastLoadedVersion = version;

            byte gridCount = r.ReadByte();
            if (gridCount != 1)
                throw new InvalidDataException($"Multi-grid maps not supported yet (gridCount={gridCount}).");
            r.ReadByte(); // gridId — до фазы E всегда 0, значение не используется

            var grid = new BlockGrid();
            _migrateMoved = 0;
            _migrateSkipped = 0;
            int sectionCount = r.ReadInt32();
            for (int i = 0; i < sectionCount; i++)
            {
                int cx = r.ReadInt32();
                int cy = r.ReadInt32();
                int cz = r.ReadInt32();
                grid.AddSection(cx, cy, cz, ReadSection(r, version));
            }

            if (version >= 13) // v10-12: списка нет — грид без предметов, не ошибка
            {
                ushort spawnCount = r.ReadUInt16();
                for (int i = 0; i < spawnCount; i++)
                {
                    int x = r.ReadInt32();
                    int y = r.ReadInt32();
                    int z = r.ReadInt32();
                    ushort defId = r.ReadUInt16();
                    byte stack = r.ReadByte();
                    grid.AddItemSpawn(new ItemSpawn(x, y, z, defId, stack));
                }
            }
            return grid;
        }

        /// <summary>Обратный кодек секции (см. WriteSection). version — формат источника (файл может быть v10).</summary>
        public static ChunkSection ReadSection(BinaryReader r, ushort version = Version)
        {
            byte encoding = r.ReadByte();

            BlockPalette palette = null;
            byte[] indices = null;
            ushort[] raw = null;

            if (encoding == EncodingRaw)
            {
                raw = new ushort[ChunkSection.BlockCount];
                for (int i = 0; i < ChunkSection.BlockCount; i++)
                    raw[i] = r.ReadUInt16();
            }
            else if (encoding == EncodingPalette)
            {
                palette = new BlockPalette();
                ushort count = r.ReadUInt16();
                for (int i = 1; i < count; i++) // слот 0 (Air) палитра уже содержит
                {
                    ushort type = r.ReadUInt16();
                    if (palette.IndexOf(type) >= 0 || palette.Add(type) < 0)
                        throw new InvalidDataException("Corrupt palette (duplicate type or overflow).");
                }
                indices = r.ReadBytes(ChunkSection.BlockCount);
                if (indices.Length != ChunkSection.BlockCount)
                    throw new InvalidDataException("Truncated section indices.");
            }
            else
            {
                throw new InvalidDataException($"Unknown section encoding {encoding}.");
            }

            var states = new Dictionary<int, byte>();
            ushort stateCount = r.ReadUInt16();
            for (int i = 0; i < stateCount; i++)
            {
                ushort li = r.ReadUInt16();
                states[li] = r.ReadByte();
            }

            Dictionary<int, byte> bake = null;
            if (version >= 11)
            {
                bake = new Dictionary<int, byte>();
                ushort bakeCount = r.ReadUInt16();
                for (int i = 0; i < bakeCount; i++)
                {
                    ushort li = r.ReadUInt16();
                    bake[li] = r.ReadByte();
                }
            }

            Dictionary<int, ushort> zone = null;
            Dictionary<int, FloorSeed> seeds = null;
            ushort defaultZone = 0;
            if (version >= 12)
            {
                zone = new Dictionary<int, ushort>();
                if (version >= 14)
                    defaultZone = r.ReadUInt16();
                ushort zoneCount = r.ReadUInt16();
                for (int i = 0; i < zoneCount; i++)
                {
                    ushort li = r.ReadUInt16();
                    zone[li] = r.ReadUInt16();
                }

                seeds = new Dictionary<int, FloorSeed>();
                ushort seedCount = r.ReadUInt16();
                for (int i = 0; i < seedCount; i++)
                {
                    ushort li = r.ReadUInt16();
                    string name = r.ReadString();
                    int rank = r.ReadInt32();
                    int floor = r.ReadInt32();
                    seeds[li] = new FloorSeed(name, rank, floor);
                }
            }

            Dictionary<int, ushort> structs;
            if (version >= 15)
            {
                structs = new Dictionary<int, ushort>();
                ushort structCount = r.ReadUInt16();
                for (int i = 0; i < structCount; i++)
                {
                    ushort li = r.ReadUInt16();
                    structs[li] = r.ReadUInt16();
                }
            }
            else
            {
                structs = MigratePartBits(states, palette, indices, raw);
            }

            return ChunkSection.FromData(palette, indices, raw, states, bake, zone, seeds, defaultZone, structs);
        }

        private static int _migrateMoved;
        private static int _migrateSkipped;

        /// <summary>Счётчики последней миграции v&lt;15 (перенесено частей / пропущено — неизвестный либо одиночный тип).</summary>
        public static (int Moved, int Skipped) LastMigrationStats => (_migrateMoved, _migrateSkipped);

        private static ushort _lastLoadedVersion;

        /// <summary>Версия формата ПОСЛЕДНЕЙ прочитанной карты (0 — ещё ничего не читали); печатает потребитель.</summary>
        public static ushort LastLoadedVersion => _lastLoadedVersion;

        // Миграция v<15: номер части жил в state-битах 5-6 (СТАРАЯ раскладка — константы локальны намеренно,
        // BlockState их больше не знает). Часть → мировое смещение до якоря → запись слоя; part-биты обнуляются.
        private static Dictionary<int, ushort> MigratePartBits(Dictionary<int, byte> states, BlockPalette? palette, byte[]? indices, ushort[]? raw)
        {
            const int legacyPartShift = 5;
            const byte legacyPartMask = 0b11 << legacyPartShift;

            var result = new Dictionary<int, ushort>();
            var lis = new List<int>(states.Keys);
            lis.Sort();
            foreach (int li in lis)
            {
                byte st = states[li];
                int part = (st & legacyPartMask) >> legacyPartShift;
                if (part == 0)
                    continue;

                // Сначала валидация, потом мутация: иначе у неизвестного/одиночного типа биты стёрты, а записи нет.
                ushort type = TypeAt(li, palette, indices, raw);
                var info = BlockCatalog.Get(type);
                if (info.PartCount <= 1)
                {
                    _migrateSkipped++;
                    continue;
                }

                int facing = (st >> 3) & 0b11;
                MultiBlock.PartWorldOffset(part, info.SizeX, info.SizeZ, facing, out int dx, out int dy, out int dz);
                if (!MultiBlock.OffsetInRange(dx, dy, dz))
                {
                    _migrateSkipped++;
                    continue;
                }

                states[li] = (byte)(st & ~legacyPartMask);
                result[li] = (ushort)MultiBlock.PackOffset(dx, dy, dz);
                _migrateMoved++;
            }

            foreach (int li in lis)
                if (states[li] == 0)
                    states.Remove(li);
            return result;
        }

        private static ushort TypeAt(int localIndex, BlockPalette? palette, byte[]? indices, ushort[]? raw)
        {
            if (raw != null)
                return raw[localIndex];
            if (palette == null || indices == null)
                return 0;
            var types = palette.Types;
            int slot = indices[localIndex];
            return slot >= 0 && slot < types.Count ? types[slot] : (ushort)0;
        }

        public static void SaveToFile(string path, BlockGrid grid)
        {
            using var fs = File.Create(path);
            Write(fs, grid);
        }

        public static BlockGrid LoadFromFile(string path)
        {
            using var fs = File.OpenRead(path);
            return Read(fs);
        }
    }
}
