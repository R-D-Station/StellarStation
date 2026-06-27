using System;
using System.IO;

namespace Shared.World
{
    /// <summary>
    /// Бинарная сериализация GridMap (один формат для файла .smap и сетевой передачи).
    /// Формат (little-endian): [magic 'SMAP'][version uint16][chunkCount int32],
    /// далее на чанк: [cx][cy][z] + TileCount тайлов (v3 — по 4 байта, см. WriteTile).
    /// </summary>
    public static class MapSerializer
    {
        public const int Magic = ('S') | ('M' << 8) | ('A' << 16) | ('P' << 24);
        public const ushort Version = 3;

        // Битовые флаги тайла (упакованы в один байт).
        private const byte FlagSupport = 1 << 0;
        private const byte FlagHorizBlock = 1 << 1;
        private const byte FlagVertBlock = 1 << 2;
        private const byte FlagSealHoriz = 1 << 3;
        private const byte FlagSealVert = 1 << 4;
        private const byte FlagOpen = 1 << 5;
        private const byte FlagOpenable = 1 << 6;

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

        /// <summary>Записать один тайл в текущем формате (v3). Публично — переиспользует TileUpdate.</summary>
        public static void WriteTile(BinaryWriter w, in Tile t)
        {
            w.Write(t.FloorType);
            w.Write(t.StructureType);
            w.Write((byte)t.Special);

            byte flags = 0;
            if (t.Support) flags |= FlagSupport;
            if (t.BlocksHorizontalSight) flags |= FlagHorizBlock;
            if (t.BlocksVerticalSight) flags |= FlagVertBlock;
            if (t.SealsHorizontal) flags |= FlagSealHoriz;
            if (t.SealsVertical) flags |= FlagSealVert;
            if (t.Open) flags |= FlagOpen;
            if (t.Openable) flags |= FlagOpenable;
            w.Write(flags);
        }

        /// <summary>Прочитать один тайл текущего формата (v3).</summary>
        public static Tile ReadTile(BinaryReader r) => ReadTile(r, Version);

        /// <summary>Прочитать тайл с учётом версии файла. Старые v1/v2 (слои стены+двери)
        /// сводятся к StructureType: дверь → id 2 (открываемая), стена → свой id.</summary>
        public static Tile ReadTile(BinaryReader r, ushort version)
        {
            var t = new Tile { FloorType = r.ReadByte() };

            if (version >= 3)
            {
                t.StructureType = r.ReadByte();
                t.Special = (TileSpecial)r.ReadByte();
                byte flags = r.ReadByte();
                t.Support = (flags & FlagSupport) != 0;
                t.BlocksHorizontalSight = (flags & FlagHorizBlock) != 0;
                t.BlocksVerticalSight = (flags & FlagVertBlock) != 0;
                t.SealsHorizontal = (flags & FlagSealHoriz) != 0;
                t.SealsVertical = (flags & FlagSealVert) != 0;
                t.Open = (flags & FlagOpen) != 0;
                t.Openable = (flags & FlagOpenable) != 0;
                return t;
            }

            // Legacy v1/v2: FloorType, WallType, [DoorType, Special], flags.
            byte wall = r.ReadByte();
            byte door = 0;
            if (version >= 2)
            {
                door = r.ReadByte();
                t.Special = (TileSpecial)r.ReadByte();
            }
            byte lflags = r.ReadByte();
            t.Support = (lflags & FlagSupport) != 0;
            t.BlocksHorizontalSight = (lflags & FlagHorizBlock) != 0;
            t.BlocksVerticalSight = (lflags & FlagVertBlock) != 0;
            t.SealsHorizontal = (lflags & FlagSealHoriz) != 0;
            t.SealsVertical = (lflags & FlagSealVert) != 0;
            if (door != 0)
            {
                t.StructureType = 2;
                t.Openable = true;
                t.Open = version >= 2 && (lflags & FlagOpen) != 0;
            }
            else if (wall != 0)
            {
                t.StructureType = wall;
            }
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