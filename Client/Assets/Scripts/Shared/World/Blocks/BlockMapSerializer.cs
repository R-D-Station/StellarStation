using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Shared.World.Blocks
{
    /// <summary>
    /// Бинарная сериализация BlockGrid — формат .smap v10 (один формат для файла и сети).
    /// Заголовок: [magic 'SMAP'][version=10][gridCount byte]; на грид: [gridId byte][sectionCount int32];
    /// на секцию: [cx][cy][cz][encoding byte] + палитра/индексы либо raw-ushort + разреженный state.
    /// Секции пишутся сортированно по (cy,cz,cx) — вертикаль Y первой, state — по локальному индексу →
    /// round-trip побайтово стабилен.
    /// Тайловые версии (1..3) НЕ читаются: мигратора нет, карты пересобираются в блочном редакторе.
    /// </summary>
    public static class BlockMapSerializer
    {
        public const int Magic = MapSerializer.Magic; // 'SMAP' — общее семейство форматов карт
        public const ushort Version = 11;             // v11 = v10 + бейк-канал; v10 читается (бейк пуст)
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

            byte gridCount = r.ReadByte();
            if (gridCount != 1)
                throw new InvalidDataException($"Multi-grid maps not supported yet (gridCount={gridCount}).");
            r.ReadByte(); // gridId — до фазы E всегда 0, значение не используется

            var grid = new BlockGrid();
            int sectionCount = r.ReadInt32();
            for (int i = 0; i < sectionCount; i++)
            {
                int cx = r.ReadInt32();
                int cy = r.ReadInt32();
                int cz = r.ReadInt32();
                grid.AddSection(cx, cy, cz, ReadSection(r, version));
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

            return ChunkSection.FromData(palette, indices, raw, states, bake);
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
