using System;
using System.IO;
using Shared.Messages;
using Shared.World;

namespace Shared.Messages.Core
{
    /// <summary>Изменение одного тайла в рантайме (дверь, лифт, постройка). Формат тела — как в .smap.</summary>
    public struct TileUpdate : INetMessage
    {
        public int X;
        public int Y;
        public int Z;
        public Tile Tile;

        public MessageType Type => MessageType.TileUpdate;

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(X);
            w.Write(Y);
            w.Write(Z);
            MapSerializer.WriteTile(w, in Tile);
            return ms.ToArray();
        }

        public void Deserialize(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            using var ms = new MemoryStream(data);
            using var r = new BinaryReader(ms);
            X = r.ReadInt32();
            Y = r.ReadInt32();
            Z = r.ReadInt32();
            Tile = MapSerializer.ReadTile(r);
        }
    }
}
