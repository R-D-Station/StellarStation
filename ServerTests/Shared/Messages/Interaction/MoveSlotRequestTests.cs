using System;
using Shared.Messages;
using Shared.Messages.Interaction;
using Shared.World.Items;

namespace ServerTests.Shared.Messages.Interaction
{
    /// <summary>MoveSlotRequest (wire 205): FromSlot/ToSlot 2Б РОВНО, guard &lt;SlotCount, guard From!=To.</summary>
    public class MoveSlotRequestTests
    {
        [Fact]
        public void SerializeDeserialize_RoundTrips()
        {
            var original = new MoveSlotRequest { FromSlot = InventorySlot.HandLeft, ToSlot = InventorySlot.Belt };
            var back = new MoveSlotRequest();
            back.Deserialize(original.Serialize());

            Assert.Equal(InventorySlot.HandLeft, back.FromSlot);
            Assert.Equal(InventorySlot.Belt, back.ToSlot);
            Assert.Equal(MessageType.MoveSlot, back.Type);
        }

        [Fact]
        public void Serialize_ProducesTwoBytes()
        {
            Assert.Equal(2, new MoveSlotRequest().Serialize().Length);
        }

        [Fact]
        public void Deserialize_Null_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new MoveSlotRequest().Deserialize(null!));
        }

        [Fact]
        public void Deserialize_WrongSize_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new MoveSlotRequest().Deserialize(new byte[1]));
            Assert.Throws<ArgumentException>(() => new MoveSlotRequest().Deserialize(new byte[3]));
        }

        [Fact]
        public void Deserialize_SlotOutOfRange_Throws()
        {
            var data = new byte[] { InventorySlot.SlotCount, 0 };
            Assert.Throws<InvalidOperationException>(() => new MoveSlotRequest().Deserialize(data));
        }

        [Fact]
        public void Deserialize_FromEqualsTo_Throws()
        {
            var data = new byte[] { 2, 2 };
            Assert.Throws<InvalidOperationException>(() => new MoveSlotRequest().Deserialize(data));
        }
    }
}
