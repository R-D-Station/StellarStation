using System;
using System.IO;

namespace Shared.Messages.Interaction
{
    /// <summary>Взять предмет по индексу из открытого контейнера в активную руку (client→server): требует пустую руку.</summary>
    public struct TakeFromContainer : INetMessage
    {
        public int NetId;
        public ushort Index;

        public MessageType Type => MessageType.TakeFromContainer;

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            writer.Write(NetId);
            writer.Write(Index);
            return ms.ToArray();
        }

        public void Deserialize(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data), "TakeFromContainer data cannot be null");

            const int expectedSize = 6;
            if (data.Length != expectedSize)
                throw new ArgumentException($"Invalid data size: expected {expectedSize} bytes, got {data.Length} bytes", nameof(data));

            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            NetId = reader.ReadInt32();
            Index = reader.ReadUInt16();

            if (ms.Position != ms.Length)
                throw new InvalidOperationException($"Unexpected extra data: {ms.Length - ms.Position} bytes remaining");
        }
    }
}
