using System;
using System.IO;
using Shared.Messages;
using Shared.World;

namespace Shared.Messages.Core
{
    /// <summary>Один чанк карты от сервера клиенту (стриминг по PVS вместо разовой полной карты). Тело:
    /// [cx][cy][z] + Chunk.TileCount тайлов в формате .smap (MapSerializer.WriteTile/ReadTile). Двери-апдейты
    /// приходят отдельно через TileUpdate — ChunkData несёт снимок тайла из _map на момент отправки.</summary>
    public struct ChunkData : INetMessage
    {
        public Chunk Chunk;

        public MessageType Type => MessageType.MapChunk;

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(Chunk.ChunkX);
            w.Write(Chunk.ChunkY);
            w.Write(Chunk.Z);
            var tiles = Chunk.Raw;
            for (int i = 0; i < tiles.Length; i++)
                MapSerializer.WriteTile(w, in tiles[i]);
            return ms.ToArray();
        }

        public void Deserialize(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            // Фиксированный размер: [cx][cy][z] (12) + 256 тайлов по 4 байта (v3-тайл: FloorType+StructureType+
            // Special+flags). Строгая проверка ловит рассинхрон формата тайла между клиентом и сервером.
            const int tileBytes = 4;
            const int expectedSize = 12 + Chunk.TileCount * tileBytes;
            if (data.Length != expectedSize)
                throw new ArgumentException($"Invalid ChunkData size: expected {expectedSize}, got {data.Length}", nameof(data));

            using var ms = new MemoryStream(data);
            using var r = new BinaryReader(ms);
            int cx = r.ReadInt32();
            int cy = r.ReadInt32();
            int z = r.ReadInt32();

            var chunk = new Chunk(cx, cy, z);
            var tiles = chunk.Raw;
            for (int i = 0; i < tiles.Length; i++)
                tiles[i] = MapSerializer.ReadTile(r);
            Chunk = chunk;

            if (ms.Position != ms.Length)
                throw new InvalidOperationException("Unexpected extra data in ChunkData");
        }
    }
}
