using System;
using System.IO;

namespace Shared.Messages.Interaction
{
    /// <summary>Заморозка/освобождение владельца SS14-ящиком (server→owner): ContainerNetId≠0 — заперт (движение игнорируется, клиент скрывает), 0 — выпущен.</summary>
    public struct ContainSync : INetMessage
    {
        public int ContainerNetId;

        public MessageType Type => MessageType.ContainSync;

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            writer.Write(ContainerNetId);
            return ms.ToArray();
        }

        public void Deserialize(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data), "ContainSync data cannot be null");

            const int expectedSize = 4;
            if (data.Length != expectedSize)
                throw new ArgumentException($"Invalid data size: expected {expectedSize} bytes, got {data.Length} bytes", nameof(data));

            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            ContainerNetId = reader.ReadInt32();

            if (ms.Position != ms.Length)
                throw new InvalidOperationException($"Unexpected extra data: {ms.Length - ms.Position} bytes remaining");
        }
    }
}
