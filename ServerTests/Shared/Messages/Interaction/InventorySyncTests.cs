using System;
using Shared.Messages.Interaction;
using Shared.World.Items;

namespace ServerTests.Shared.Messages.Interaction
{
    public class InventorySyncTests
    {
        private static SlotRecord Rec(SlotCategory cat, byte idx, ushort defId, byte stack) => new SlotRecord
        {
            Category = cat, Index = idx, ItemDefId = defId, StackCount = stack
        };

        [Fact]
        public void RoundTrip_PreservesActiveHandAndSlots()
        {
            var src = new InventorySync
            {
                ActiveHand = 1,
                Slots = new[] { Rec(SlotCategory.Hand, 0, 10, 3), Rec(SlotCategory.Belt, 0, 7, 1), Rec(SlotCategory.Pocket, 1, 42, 255) }
            };
            var bytes = src.Serialize();

            var dst = new InventorySync();
            dst.Deserialize(bytes);

            Assert.Equal((byte)1, dst.ActiveHand);
            Assert.Equal(3, dst.Slots.Length);
            Assert.Equal(SlotCategory.Hand, dst.Slots[0].Category);
            Assert.Equal((byte)0, dst.Slots[0].Index);
            Assert.Equal((ushort)10, dst.Slots[0].ItemDefId);
            Assert.Equal((byte)3, dst.Slots[0].StackCount);
            Assert.Equal(SlotCategory.Pocket, dst.Slots[2].Category);
            Assert.Equal((byte)1, dst.Slots[2].Index);
            Assert.Equal((byte)255, dst.Slots[2].StackCount);
        }

        [Fact]
        public void RoundTrip_Empty()
        {
            var src = new InventorySync { ActiveHand = 0, Slots = Array.Empty<SlotRecord>() };
            var bytes = src.Serialize();
            Assert.Equal(3, bytes.Length);

            var dst = new InventorySync();
            dst.Deserialize(bytes);
            Assert.Empty(dst.Slots);
        }

        [Fact]
        public void Serialize_ExactSize()
        {
            var src = new InventorySync { ActiveHand = 0, Slots = new[] { Rec(SlotCategory.Hand, 0, 1, 1), Rec(SlotCategory.Belt, 0, 2, 1) } };
            Assert.Equal(3 + 2 * InventorySync.PerSlotSize, src.Serialize().Length);
        }

        [Fact]
        public void Deserialize_ActiveHandOutOfRange_Rejected()
        {
            Assert.ThrowsAny<Exception>(() => new InventorySync().Deserialize(new byte[] { 2, 0, 0 }));
        }

        [Fact]
        public void Deserialize_HugeCount_RejectsBeforeAllocation()
        {
            Assert.ThrowsAny<Exception>(() => new InventorySync().Deserialize(new byte[] { 0, 0xFF, 0xFF }));
        }

        [Fact]
        public void Deserialize_CategoryOutOfRange_Rejected()
        {
            var bytes = new InventorySync { ActiveHand = 0, Slots = new[] { Rec(SlotCategory.Hand, 0, 1, 1) } }.Serialize();
            bytes[3] = 200;
            Assert.ThrowsAny<Exception>(() => new InventorySync().Deserialize(bytes));
        }

        [Fact]
        public void Deserialize_IndexOutOfRangeForCategory_Rejected()
        {
            var bytes = new byte[] { 0, 1, 0, (byte)SlotCategory.Belt, 1, 5, 0, 1 };
            Assert.ThrowsAny<Exception>(() => new InventorySync().Deserialize(bytes));
        }

        [Fact]
        public void Deserialize_ZeroItemDefId_Rejected()
        {
            var bytes = new byte[] { 0, 1, 0, (byte)SlotCategory.Hand, 0, 0, 0, 1 };
            Assert.ThrowsAny<Exception>(() => new InventorySync().Deserialize(bytes));
        }

        [Fact]
        public void Deserialize_TrailingBytes_Rejected()
        {
            var bytes = new InventorySync { ActiveHand = 0, Slots = new[] { Rec(SlotCategory.Hand, 0, 1, 1) } }.Serialize();
            Array.Resize(ref bytes, bytes.Length + 1);
            Assert.ThrowsAny<Exception>(() => new InventorySync().Deserialize(bytes));
        }

        [Fact]
        public void Deserialize_TruncatedBlock_Rejected()
        {
            var bytes = new InventorySync { ActiveHand = 0, Slots = new[] { Rec(SlotCategory.Hand, 0, 1, 1), Rec(SlotCategory.Belt, 0, 2, 1) } }.Serialize();
            Array.Resize(ref bytes, bytes.Length - 2);
            Assert.ThrowsAny<Exception>(() => new InventorySync().Deserialize(bytes));
        }

        [Fact]
        public void Deserialize_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new InventorySync().Deserialize(null!));
        }
    }
}
