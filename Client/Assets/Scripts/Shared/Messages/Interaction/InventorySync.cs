using System;
using System.IO;

namespace Shared.Messages.Interaction
{
    /// <summary>Один занятый слот инвентаря на проводе (8B): SlotIndex(1)+NetId(4)+ItemDefId(2)+StackCount(1).</summary>
    public struct SlotRecord
    {
        public byte SlotIndex;
        public int NetId;
        public ushort ItemDefId;
        public byte StackCount;
    }

    /// <summary>Полный слепок инвентаря игрока (server→client, OWNER-ONLY, full-state, фикс-6-слотов); содержимое
    /// контейнеров — ОТДЕЛЬНОЕ будущее сообщение по NetId, НЕ вкладывать сюда.</summary>
    // Wire: Header = InventoryVersion(4) + ActiveHand(1) + OccupiedCount(1), затем OccupiedCount × SlotRecord(8).
    // Максимум 6 + 6·8 = 54 байта. Held-предметы НЕ несут координат (X/Y/Z) — их местоположение = сам слот.
    public struct InventorySync : INetMessage
    {
        public uint InventoryVersion;
        public byte ActiveHand;
        public SlotRecord[] Slots; // только занятые (OccupiedCount ≤ 6)

        public const int PerSlotSize = 8;
        private const int HeaderSize = 6; // InventoryVersion(4) + ActiveHand(1) + OccupiedCount(1)

        public MessageType Type => MessageType.InventorySync;

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            writer.Write(InventoryVersion);
            writer.Write(ActiveHand);
            byte count = (byte)(Slots?.Length ?? 0);
            writer.Write(count);

            for (int i = 0; i < count; i++)
                WriteSlot(writer, in Slots![i]);

            return ms.ToArray();
        }

        private static void WriteSlot(BinaryWriter writer, in SlotRecord s)
        {
            writer.Write(s.SlotIndex);
            writer.Write(s.NetId);
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

            InventoryVersion = reader.ReadUInt32();

            byte activeHand = reader.ReadByte();
            if (activeHand >= 2)
                throw new InvalidOperationException($"Invalid ActiveHand value: {activeHand} (must be < 2)");
            ActiveHand = activeHand;

            byte occupiedCount = reader.ReadByte();
            // Кап ДО аллокации массива (враждебный/битый пир не должен вызвать неограниченную аллокацию).
            const int slotCount = 6;
            if (occupiedCount > slotCount)
                throw new InvalidOperationException($"Invalid OccupiedCount value: {occupiedCount} (must be <= {slotCount})");

            long need = (long)occupiedCount * PerSlotSize;
            if (ms.Position + need > ms.Length)
                throw new InvalidOperationException($"InventorySync data too short for {occupiedCount} slots");

            var slots = new SlotRecord[occupiedCount];
            byte seenMask = 0; // 6-битная маска — отклоняет дубль SlotIndex
            for (int i = 0; i < occupiedCount; i++)
            {
                var s = ReadSlot(reader);

                if (s.SlotIndex >= slotCount)
                    throw new InvalidOperationException($"Invalid SlotIndex value: {s.SlotIndex} (must be < {slotCount})");
                if (s.NetId < 1)
                    throw new InvalidOperationException($"Invalid NetId value: {s.NetId} (must be >= 1)");
                if (s.ItemDefId == 0)
                    throw new InvalidOperationException("Invalid ItemDefId value: 0");

                byte bit = (byte)(1 << s.SlotIndex);
                if ((seenMask & bit) != 0)
                    throw new InvalidOperationException($"Duplicate SlotIndex: {s.SlotIndex}");
                seenMask |= bit;

                slots[i] = s;
            }
            Slots = slots;

            if (ms.Position != ms.Length)
                throw new InvalidOperationException($"Unexpected extra data: {ms.Length - ms.Position} bytes remaining");
        }

        private static SlotRecord ReadSlot(BinaryReader reader) => new SlotRecord
        {
            SlotIndex = reader.ReadByte(),
            NetId = reader.ReadInt32(),
            ItemDefId = reader.ReadUInt16(),
            StackCount = reader.ReadByte()
        };
    }
}
