using System;
using System.IO;
using Shared.Messages;
using Shared.Messages.Interaction;

namespace ServerTests.Shared.Messages.Interaction
{
    /// <summary>InventorySync (wire 203, server→client, OWNER-ONLY): round-trip, cap-before-alloc (count&gt;6),
    /// ActiveHand&gt;=2, дубль-слот, NetId=0, ItemDefId=0, хвостовые байты.</summary>
    public class InventorySyncTests
    {
        private static SlotRecord Slot(byte idx, int netId, ushort defId, byte stack) => new SlotRecord
        {
            SlotIndex = idx, NetId = netId, ItemDefId = defId, StackCount = stack
        };

        [Fact]
        public void RoundTrip_PreservesAllFields()
        {
            var src = new InventorySync
            {
                InventoryVersion = 7,
                ActiveHand = 1,
                Slots = new[] { Slot(0, 5, 42, 3), Slot(4, 6, 7, 255) }
            };
            var bytes = src.Serialize();

            var dst = new InventorySync();
            dst.Deserialize(bytes);

            Assert.Equal(7u, dst.InventoryVersion);
            Assert.Equal((byte)1, dst.ActiveHand);
            Assert.Equal(2, dst.Slots.Length);
            Assert.Equal((byte)0, dst.Slots[0].SlotIndex);
            Assert.Equal(5, dst.Slots[0].NetId);
            Assert.Equal((ushort)42, dst.Slots[0].ItemDefId);
            Assert.Equal((byte)3, dst.Slots[0].StackCount);
            Assert.Equal((byte)4, dst.Slots[1].SlotIndex);
            Assert.Equal(MessageType.InventorySync, dst.Type);
        }

        [Fact]
        public void Serialize_EmptyInventory_HeaderOnly()
        {
            var src = new InventorySync { InventoryVersion = 1, ActiveHand = 0, Slots = Array.Empty<SlotRecord>() };
            var bytes = src.Serialize();
            Assert.Equal(6, bytes.Length); // Header only: version(4)+hand(1)+count(1)

            var dst = new InventorySync();
            dst.Deserialize(bytes);
            Assert.Empty(dst.Slots);
        }

        [Fact]
        public void Serialize_MaxSlots_54Bytes()
        {
            var slots = new SlotRecord[6];
            for (byte i = 0; i < 6; i++) slots[i] = Slot(i, i + 1, 1, 1);
            var src = new InventorySync { InventoryVersion = 1, ActiveHand = 0, Slots = slots };
            Assert.Equal(54, src.Serialize().Length); // 6 + 6*8
        }

        [Fact]
        public void Deserialize_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new InventorySync().Deserialize(null!));
        }

        [Fact]
        public void Deserialize_TooShortHeader_Throws()
        {
            Assert.Throws<ArgumentException>(() => new InventorySync().Deserialize(new byte[3]));
        }

        [Fact]
        public void Deserialize_OccupiedCountAboveSix_RejectedBeforeAllocation()
        {
            var data = Build(1, 0, 7); // count=7 > 6, ДО добавления записей
            Assert.Throws<InvalidOperationException>(() => new InventorySync().Deserialize(data));
        }

        [Fact]
        public void Deserialize_ActiveHandTooLarge_Throws()
        {
            var data = Build(1, 2, 0);
            Assert.Throws<InvalidOperationException>(() => new InventorySync().Deserialize(data));
        }

        [Fact]
        public void Deserialize_DuplicateSlotIndex_Throws()
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write((uint)1); w.Write((byte)0); w.Write((byte)2);
            WriteSlot(w, 0, 1, 1, 1);
            WriteSlot(w, 0, 2, 1, 1); // дубль SlotIndex=0
            w.Flush();

            Assert.Throws<InvalidOperationException>(() => new InventorySync().Deserialize(ms.ToArray()));
        }

        [Fact]
        public void Deserialize_ZeroNetId_Throws()
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write((uint)1); w.Write((byte)0); w.Write((byte)1);
            WriteSlot(w, 0, 0, 1, 1); // NetId=0 невалиден
            w.Flush();

            Assert.Throws<InvalidOperationException>(() => new InventorySync().Deserialize(ms.ToArray()));
        }

        [Fact]
        public void Deserialize_ZeroItemDefId_Throws()
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write((uint)1); w.Write((byte)0); w.Write((byte)1);
            WriteSlot(w, 0, 1, 0, 1); // ItemDefId=0 невалиден
            w.Flush();

            Assert.Throws<InvalidOperationException>(() => new InventorySync().Deserialize(ms.ToArray()));
        }

        [Fact]
        public void Deserialize_TrailingBytes_Throws()
        {
            var src = new InventorySync { InventoryVersion = 1, ActiveHand = 0, Slots = new[] { Slot(0, 1, 1, 1) } };
            var bytes = src.Serialize();
            Array.Resize(ref bytes, bytes.Length + 1);

            Assert.Throws<InvalidOperationException>(() => new InventorySync().Deserialize(bytes));
        }

        [Fact]
        public void Deserialize_SlotIndexOutOfRange_Throws()
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write((uint)1); w.Write((byte)0); w.Write((byte)1);
            WriteSlot(w, 6, 1, 1, 1); // SlotIndex==SlotCount, invalid
            w.Flush();

            Assert.Throws<InvalidOperationException>(() => new InventorySync().Deserialize(ms.ToArray()));
        }

        private static byte[] Build(uint version, byte activeHand, byte occupiedCount)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(version);
            w.Write(activeHand);
            w.Write(occupiedCount);
            w.Flush();
            return ms.ToArray();
        }

        private static void WriteSlot(BinaryWriter w, byte slotIndex, int netId, ushort itemDefId, byte stackCount)
        {
            w.Write(slotIndex);
            w.Write(netId);
            w.Write(itemDefId);
            w.Write(stackCount);
        }
    }
}
