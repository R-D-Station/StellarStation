using System;
using System.IO;
using Shared.Messages;
using Shared.Messages.Interaction;
using Shared.World.Items;

namespace ServerTests.Shared.Messages.Interaction
{
    /// <summary>DropItem (wire 202): drop-at-feet — только SlotIndex (1Б РОВНО), НЕТ координат; guard SlotIndex&lt;SlotCount.</summary>
    public class DropItemTests
    {
        [Fact]
        public void SerializeDeserialize_RoundTrips()
        {
            var original = new DropItem { SlotIndex = InventorySlot.HandRight };
            var back = new DropItem();
            back.Deserialize(original.Serialize());

            Assert.Equal(InventorySlot.HandRight, back.SlotIndex);
            Assert.Equal(MessageType.DropItem, back.Type);
        }

        [Fact]
        public void Serialize_ProducesOneByte()
        {
            Assert.Equal(1, new DropItem().Serialize().Length);
        }

        [Fact]
        public void Deserialize_Null_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new DropItem().Deserialize(null!));
        }

        [Fact]
        public void Deserialize_WrongSize_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new DropItem().Deserialize(Array.Empty<byte>()));
            Assert.Throws<ArgumentException>(() => new DropItem().Deserialize(new byte[2]));
        }

        [Fact]
        public void Deserialize_SlotIndexOutOfRange_Throws()
        {
            var data = new byte[] { InventorySlot.SlotCount }; // == SlotCount, invalid (must be <)
            Assert.Throws<InvalidOperationException>(() => new DropItem().Deserialize(data));
        }
    }
}
