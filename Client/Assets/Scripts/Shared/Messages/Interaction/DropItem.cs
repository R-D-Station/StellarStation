using System;
using System.IO;
using Shared.World.Items;

namespace Shared.Messages.Interaction
{
    /// <summary>Выбросить предмет из слота (client→server): drop-at-feet — сервер роняет на floor(client.X/Y),
    /// клиентские координаты не принимаются.</summary>
    public struct DropItem : INetMessage
    {
        public byte SlotIndex;

        public MessageType Type => MessageType.DropItem;

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            writer.Write(SlotIndex);
            return ms.ToArray();
        }

        public void Deserialize(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data), "DropItem data cannot be null");

            const int expectedSize = 1; // SlotIndex(1)
            if (data.Length != expectedSize)
                throw new ArgumentException($"Invalid data size: expected {expectedSize} bytes, got {data.Length} bytes", nameof(data));

            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            byte slotIndex = reader.ReadByte();
            if (slotIndex >= InventorySlot.SlotCount)
                throw new InvalidOperationException($"Invalid SlotIndex value: {slotIndex} (must be < {InventorySlot.SlotCount})");
            SlotIndex = slotIndex;

            if (ms.Position != ms.Length)
                throw new InvalidOperationException($"Unexpected extra data: {ms.Length - ms.Position} bytes remaining");
        }
    }
}
