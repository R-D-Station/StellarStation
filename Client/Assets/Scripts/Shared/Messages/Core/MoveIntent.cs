using System;
using System.IO;
using Shared.Messages;

namespace Shared.Messages.Core
{
    public struct MoveIntent : INetMessage
    {
        public IntentDirection Direction;
        public bool Sprint;
        public uint Sequence;

        public MessageType Type => MessageType.MoveIntent;

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            writer.Write((byte)Direction);
            writer.Write(Sprint);
            writer.Write(Sequence);

            return ms.ToArray();
        }

        public void Deserialize(byte[] data)
        {
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            Direction = (IntentDirection)reader.ReadByte();
            Sprint = reader.ReadBoolean();
            Sequence = reader.ReadUInt32();
        }
    }

    public enum IntentDirection : byte
    {
        None = 0,
        North, 
        South, 
        East, 
        West
    }
}