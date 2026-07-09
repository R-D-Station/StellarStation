using System;
using System.IO;
using Shared.World.Items;

namespace Shared.Messages.Interaction
{
    /// <summary>Переместить предмет между слотами инвентаря (client→server); занятый dest — fail (без авто-swap в MVP).</summary>
    public struct MoveSlotRequest : INetMessage
    {
        public byte FromSlot;
        public byte ToSlot;

        public MessageType Type => MessageType.MoveSlot;

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            writer.Write(FromSlot);
            writer.Write(ToSlot);
            return ms.ToArray();
        }

        public void Deserialize(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data), "MoveSlotRequest data cannot be null");

            const int expectedSize = 2; // FromSlot(1) + ToSlot(1)
            if (data.Length != expectedSize)
                throw new ArgumentException($"Invalid data size: expected {expectedSize} bytes, got {data.Length} bytes", nameof(data));

            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            byte fromSlot = reader.ReadByte();
            byte toSlot = reader.ReadByte();

            if (fromSlot >= InventorySlot.SlotCount || toSlot >= InventorySlot.SlotCount)
                throw new InvalidOperationException($"Invalid slot index: from={fromSlot}, to={toSlot} (must be < {InventorySlot.SlotCount})");
            if (fromSlot == toSlot)
                throw new InvalidOperationException("FromSlot and ToSlot must differ");

            FromSlot = fromSlot;
            ToSlot = toSlot;

            if (ms.Position != ms.Length)
                throw new InvalidOperationException($"Unexpected extra data: {ms.Length - ms.Position} bytes remaining");
        }
    }
}
