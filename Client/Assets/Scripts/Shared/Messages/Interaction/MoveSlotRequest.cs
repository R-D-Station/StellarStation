using System;
using System.IO;
using Shared.World.Items;

namespace Shared.Messages.Interaction
{
    /// <summary>Переместить предмет между слотами (client→server): адресация (FromCategory,FromIndex)→(ToCategory,ToIndex), same-slot отклоняется.</summary>
    public struct MoveSlotRequest : INetMessage
    {
        public SlotCategory FromCategory;
        public byte FromIndex;
        public SlotCategory ToCategory;
        public byte ToIndex;

        public MessageType Type => MessageType.MoveSlot;

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            writer.Write((byte)FromCategory);
            writer.Write(FromIndex);
            writer.Write((byte)ToCategory);
            writer.Write(ToIndex);
            return ms.ToArray();
        }

        public void Deserialize(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data), "MoveSlotRequest data cannot be null");

            const int expectedSize = 4;
            if (data.Length != expectedSize)
                throw new ArgumentException($"Invalid data size: expected {expectedSize} bytes, got {data.Length} bytes", nameof(data));

            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            byte fromCat = reader.ReadByte();
            byte fromIdx = reader.ReadByte();
            byte toCat = reader.ReadByte();
            byte toIdx = reader.ReadByte();

            if (fromCat >= InventorySlot.CategoryCount || toCat >= InventorySlot.CategoryCount)
                throw new InvalidOperationException($"Invalid SlotCategory: from={fromCat}, to={toCat}");
            if (fromIdx >= InventorySlot.DefaultCount((SlotCategory)fromCat) || toIdx >= InventorySlot.DefaultCount((SlotCategory)toCat))
                throw new InvalidOperationException($"Invalid slot index: from={fromIdx}, to={toIdx}");
            if (fromCat == toCat && fromIdx == toIdx)
                throw new InvalidOperationException("From and To slots must differ");

            FromCategory = (SlotCategory)fromCat;
            FromIndex = fromIdx;
            ToCategory = (SlotCategory)toCat;
            ToIndex = toIdx;

            if (ms.Position != ms.Length)
                throw new InvalidOperationException($"Unexpected extra data: {ms.Length - ms.Position} bytes remaining");
        }
    }
}
