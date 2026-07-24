using System;
using System.IO;

namespace Shared.Messages.Interaction
{
    /// <summary>Положить предмет из активной руки в открытый контейнер (client→server): фильтр/ёмкость/анти-нестинг проверяет сервер.</summary>
    public struct PutInContainer : INetMessage
    {
        public int NetId;

        public MessageType Type => MessageType.PutInContainer;

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            writer.Write(NetId);
            return ms.ToArray();
        }

        public void Deserialize(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data), "PutInContainer data cannot be null");

            const int expectedSize = 4;
            if (data.Length != expectedSize)
                throw new ArgumentException($"Invalid data size: expected {expectedSize} bytes, got {data.Length} bytes", nameof(data));

            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            NetId = reader.ReadInt32();

            if (ms.Position != ms.Length)
                throw new InvalidOperationException($"Unexpected extra data: {ms.Length - ms.Position} bytes remaining");
        }
    }
}
