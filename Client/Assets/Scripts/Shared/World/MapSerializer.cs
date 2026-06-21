using System;
using System.IO;

namespace Shared.World
{
    /// <summary>
    /// Бинарная сериализация GridMap (один формат для файла .smap и сетевой передачи).
    /// Формат (little-endian): [magic 'SMAP'][version uint16][chunkCount int32],
    /// далее на чанк: [cx][cy][z] + TileCount тайлов (по 5 байт, см. WriteTile).
    /// </summary>
    public static class MapSerializer
    {
        public const int Magic = ('S') | ('M' << 8) | ('A' << 16) | ('P' << 24);
        public const ushort Version = 2;

        // Битовые флаги тайла (упакованы в один байт).
        private const byte FlagSupport = 1 << 0;
        private const byte FlagHorizBlock = 1 << 1;
        private const byte FlagVertBlock = 1 << 2;
        private const byte FlagSealHoriz = 1 << 3;
        private const byte FlagSealVert = 1 << 4;
        private const byte FlagDoorOpen = 1 << 5;

        public static void Write(Stream stream, GridMap map)
        {
            using var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);

            w.Write(Magic);
            w.Write(Version);

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
            if (version < 1 || version > Version)
                throw new InvalidDataException($"Unsupported map version {version} (expected 1..{Version}).");

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
                    tiles[i] = ReadTile(r, version);

                map.AddChunk(chunk);
            }

            return map;
        }

        /// <summary>Записать один тайл в текущем формате (v2). Публично — переиспользует TileUpdate.</summary>
        public static void WriteTile(BinaryWriter w, in Tile t)
        {
            w.Write(t.FloorType);
            w.Write(t.WallType);
            w.Write(t.DoorType);
            w.Write((byte)t.Special);

            byte flags = 0;
            if (t.Support) flags |= FlagSupport;
            if (t.BlocksHorizontalSight) flags |= FlagHorizBlock;
            if (t.BlocksVerticalSight) flags |= FlagVertBlock;
            if (t.SealsHorizontal) flags |= FlagSealHoriz;
            if (t.SealsVertical) flags |= FlagSealVert;
            if (t.DoorOpen) flags |= FlagDoorOpen;
            w.Write(flags);
        }

        /// <summary>Прочитать один тайл текущего формата (v2).</summary>
        public static Tile ReadTile(BinaryReader r) => ReadTile(r, Version);

        /// <summary>Прочитать тайл с учётом версии файла (v1 — без двери/спец-тайла).</summary>
        public static Tile ReadTile(BinaryReader r, ushort version)
        {
            var t = new Tile
            {
                FloorType = r.ReadByte(),
                WallType = r.ReadByte()
            };

            if (version >= 2)
            {
                t.DoorType = r.ReadByte();
                t.Special = (TileSpecial)r.ReadByte();
            }

            byte flags = r.ReadByte();
            t.Support = (flags & FlagSupport) != 0;
            t.BlocksHorizontalSight = (flags & FlagHorizBlock) != 0;
            t.BlocksVerticalSight = (flags & FlagVertBlock) != 0;
            t.SealsHorizontal = (flags & FlagSealHoriz) != 0;
            t.SealsVertical = (flags & FlagSealVert) != 0;
            if (version >= 2)
                t.DoorOpen = (flags & FlagDoorOpen) != 0;
            return t;
        }

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