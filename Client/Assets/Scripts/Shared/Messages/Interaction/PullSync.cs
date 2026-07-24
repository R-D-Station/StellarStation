using System;
using System.IO;

namespace Shared.Messages.Interaction
{
    public struct PullSync : INetMessage
    {
        public int PulledNetId;
        public ushort ItemDefId;

        public MessageType Type => MessageType.PullSync;

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            writer.Write(PulledNetId);
            writer.Write(ItemDefId);
            return ms.ToArray();
        }

        public void Deserialize(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data), "PullSync data cannot be null");

            const int expectedSize = 6;
            if (data.Length != expectedSize)
                throw new ArgumentException($"Invalid data size: expected {expectedSize} bytes, got {data.Length} bytes", nameof(data));

            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            PulledNetId = reader.ReadInt32();
            ItemDefId = reader.ReadUInt16();

            if (ms.Position != ms.Length)
                throw new InvalidOperationException($"Unexpected extra data: {ms.Length - ms.Position} bytes remaining");
        }
    }
}
