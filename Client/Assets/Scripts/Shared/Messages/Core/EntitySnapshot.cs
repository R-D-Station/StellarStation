using System;
using System.IO;
using Shared.Messages;

namespace Shared.Messages.Core
{
    public struct EntitySnapshot : INetMessage
    {
        public int NetId;
        public float X;
        public float Y;
        public float Z;
        public byte Facing;

        public MessageType Type => MessageType.EntitySnapshot;

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            writer.Write(NetId);
            writer.Write(X);
            writer.Write(Y);
            writer.Write(Z);
            writer.Write(Facing);

            return ms.ToArray();
        }

        public void Deserialize(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            NetId = reader.ReadInt32();
            X = reader.ReadSingle();
            Y = reader.ReadSingle();
            Z = reader.ReadSingle();
            Facing = reader.ReadByte();
        }

        public int TileX => (int)MathF.Floor(X);
        public int TileY => (int)MathF.Floor(Y);
    }
}