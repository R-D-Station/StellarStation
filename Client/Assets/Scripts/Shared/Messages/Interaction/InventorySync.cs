using System;
using System.IO;
using Shared.World.Items;

namespace Shared.Messages.Interaction
{
    public struct SlotRecord
    {
        public SlotCategory Category;
        public byte Index;
        public ushort ItemDefId;
        public byte StackCount;
    }

    public struct InventorySync : INetMessage
    {
        public byte ActiveHand;
        public SlotRecord[] Slots;

        public const int PerSlotSize = 5;
        private const int HeaderSize = 3;
        private const int MaxOccupied = 64;

        public MessageType Type => MessageType.InventorySync;

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            writer.Write(ActiveHand);
            ushort count = (ushort)(Slots?.Length ?? 0);
            writer.Write(count);

            for (int i = 0; i < count; i++)
                WriteSlot(writer, in Slots![i]);

            return ms.ToArray();
        }

        private static void WriteSlot(BinaryWriter writer, in SlotRecord s)
        {
            writer.Write((byte)s.Category);
            writer.Write(s.Index);
            writer.Write(s.ItemDefId);
            writer.Write(s.StackCount);
        }

        public void Deserialize(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data), "InventorySync data cannot be null");
            if (data.Length < HeaderSize)
                throw new ArgumentException($"Data too short: {data.Length} bytes (minimum {HeaderSize})", nameof(data));

            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            byte activeHand = reader.ReadByte();
            if (activeHand >= InventorySlot.HandCount)
                throw new InvalidOperationException($"Invalid ActiveHand value: {activeHand} (must be < {InventorySlot.HandCount})");
            ActiveHand = activeHand;

            ushort occupiedCount = reader.ReadUInt16();
            if (occupiedCount > MaxOccupied)
                throw new InvalidOperationException($"Invalid OccupiedCount value: {occupiedCount} (must be <= {MaxOccupied})");

            long need = (long)occupiedCount * PerSlotSize;
            if (ms.Position + need > ms.Length)
                throw new InvalidOperationException($"InventorySync data too short for {occupiedCount} slots");

            var slots = new SlotRecord[occupiedCount];
            for (int i = 0; i < occupiedCount; i++)
            {
                var s = ReadSlot(reader);

                if ((byte)s.Category >= InventorySlot.CategoryCount)
                    throw new InvalidOperationException($"Invalid SlotCategory value: {(byte)s.Category}");
                if (s.Index >= InventorySlot.DefaultCount(s.Category))
                    throw new InvalidOperationException($"Invalid slot Index {s.Index} for category {s.Category}");
                if (s.ItemDefId == 0)
                    throw new InvalidOperationException("Invalid ItemDefId value: 0");

                slots[i] = s;
            }
            Slots = slots;

            if (ms.Position != ms.Length)
                throw new InvalidOperationException($"Unexpected extra data: {ms.Length - ms.Position} bytes remaining");
        }

        private static SlotRecord ReadSlot(BinaryReader reader) => new SlotRecord
        {
            Category = (SlotCategory)reader.ReadByte(),
            Index = reader.ReadByte(),
            ItemDefId = reader.ReadUInt16(),
            StackCount = reader.ReadByte()
        };
    }
}
