using System;
using System.IO;

namespace Shared.World
{
    /// <summary>
    /// Ѕинарна€ сериализаци€ GridMap в нейтральный формат. „истый System.IO Ч
    /// Ќ≈ LiteNetLib (карта это файл на диске, не сетевой пакет) и Ќ≈ Unity
    /// (читает сервер на чистом .NET). ќдин и тот же код пишет редактор и
    /// читает сервер Ч формат не может разъехатьс€.
    ///
    /// ‘ормат (little-endian, как BinaryWriter по умолчанию):
    ///   [magic int32 = 'S','M','A','P'] [version uint16] [chunkCount int32]
    ///   далее chunkCount раз:
    ///     [cx int32][cy int32][z int32]
    ///     TileCount раз: один тайл (5 байт, см. WriteTile)
    ///
    /// “айл пишетс€ как 2 байта типов + 1 байт упакованных флагов = 3 байта.
    /// Ёто компактно и расшир€емо: новые флаги Ч новые биты, верси€ растЄт.
    /// </summary>
    public static class MapSerializer
    {
        private const int Magic = ('S') | ('M' << 8) | ('A' << 16) | ('P' << 24);
        private const ushort Version = 1;

        // Ѕиты упакованного байта флагов.
        private const byte FlagSupport = 1 << 0;
        private const byte FlagHorizBlock = 1 << 1;
        private const byte FlagVertBlock = 1 << 2;
        private const byte FlagSealHoriz = 1 << 3;
        private const byte FlagSealVert = 1 << 4;

        public static void Write(Stream stream, GridMap map)
        {
            using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);

            w.Write(Magic);
            w.Write(Version);

            // —читаем чанки заранее Ч IReadOnlyCollection.Count есть у Dictionary.Values.
            w.Write(map.Chunks.Count);

            foreach (var chunk in map.Chunks)
            {
                w.Write(chunk.ChunkX);
                w.Write(chunk.ChunkY);
                w.Write(chunk.Z);

                var tiles = chunk.Raw;
                for (int i = 0; i < tiles.Length; i++)
                    WriteTile(w, in tiles[i]);
            }
        }

        public static GridMap Read(Stream stream)
        {
            using var r = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);

            int magic = r.ReadInt32();
            if (magic != Magic)
                throw new InvalidDataException("Not a station map file (bad magic).");

            ushort version = r.ReadUInt16();
            if (version != Version)
                throw new InvalidDataException($"Unsupported map version {version} (expected {Version}).");

            var map = new GridMap();
            int chunkCount = r.ReadInt32();

            for (int ci = 0; ci < chunkCount; ci++)
            {
                int cx = r.ReadInt32();
                int cy = r.ReadInt32();
                int z = r.ReadInt32();

                var chunk = new Chunk(cx, cy, z);
                var tiles = chunk.Raw;
                for (int i = 0; i < tiles.Length; i++)
                    tiles[i] = ReadTile(r);

                map.AddChunk(chunk);
            }

            return map;
        }

        private static void WriteTile(BinaryWriter w, in Tile t)
        {
            w.Write(t.FloorType);
            w.Write(t.WallType);

            byte flags = 0;
            if (t.Support) flags |= FlagSupport;
            if (t.BlocksHorizontalSight) flags |= FlagHorizBlock;
            if (t.BlocksVerticalSight) flags |= FlagVertBlock;
            if (t.SealsHorizontal) flags |= FlagSealHoriz;
            if (t.SealsVertical) flags |= FlagSealVert;
            w.Write(flags);
        }

        private static Tile ReadTile(BinaryReader r)
        {
            var t = new Tile
            {
                FloorType = r.ReadByte(),
                WallType = r.ReadByte()
            };
            byte flags = r.ReadByte();
            t.Support = (flags & FlagSupport) != 0;
            t.BlocksHorizontalSight = (flags & FlagHorizBlock) != 0;
            t.BlocksVerticalSight = (flags & FlagVertBlock) != 0;
            t.SealsHorizontal = (flags & FlagSealHoriz) != 0;
            t.SealsVertical = (flags & FlagSealVert) != 0;
            return t;
        }

        // ”добные обЄртки дл€ файлов.
        public static void SaveToFile(string path, GridMap map)
        {
            using var fs = File.Create(path);
            Write(fs, map);
        }

        public static GridMap LoadFromFile(string path)
        {
            using var fs = File.OpenRead(path);
            return Read(fs);
        }
    }
}