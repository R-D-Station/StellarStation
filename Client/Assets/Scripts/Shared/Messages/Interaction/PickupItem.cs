using System;
using System.IO;

namespace Shared.Messages.Interaction
{
    /// <summary>Подобрать наземный предмет целиком (client→server): whole-stack pickup; целевой хенд — серверный
    /// ActiveHand, клиентский хенд НЕ принимается.</summary>
    public struct PickupItem : INetMessage
    {
        public int TargetNetId;

        public MessageType Type => MessageType.PickupItem;

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            writer.Write(TargetNetId);
            return ms.ToArray();
        }

        public void Deserialize(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data), "PickupItem data cannot be null");

            const int expectedSize = 4; // TargetNetId(4)
            if (data.Length != expectedSize)
                throw new ArgumentException($"Invalid data size: expected {expectedSize} bytes, got {data.Length} bytes", nameof(data));

            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            TargetNetId = reader.ReadInt32();

            if (ms.Position != ms.Length)
                throw new InvalidOperationException($"Unexpected extra data: {ms.Length - ms.Position} bytes remaining");
        }
    }
}
