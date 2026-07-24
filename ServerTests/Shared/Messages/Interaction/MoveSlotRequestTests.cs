using System;
using Shared.Messages.Interaction;
using Shared.World.Items;

namespace ServerTests.Shared.Messages.Interaction
{
    public class MoveSlotRequestTests
    {
        [Fact]
        public void RoundTrip()
        {
            var src = new MoveSlotRequest { FromCategory = SlotCategory.Hand, FromIndex = 0, ToCategory = SlotCategory.Belt, ToIndex = 0 };
            var bytes = src.Serialize();
            Assert.Equal(4, bytes.Length);

            var dst = new MoveSlotRequest();
            dst.Deserialize(bytes);
            Assert.Equal(SlotCategory.Hand, dst.FromCategory);
            Assert.Equal((byte)0, dst.FromIndex);
            Assert.Equal(SlotCategory.Belt, dst.ToCategory);
            Assert.Equal((byte)0, dst.ToIndex);
        }

        [Fact]
        public void Deserialize_SameSlot_Rejected()
        {
            Assert.ThrowsAny<Exception>(() => new MoveSlotRequest().Deserialize(new byte[] { (byte)SlotCategory.Hand, 0, (byte)SlotCategory.Hand, 0 }));
        }

        [Fact]
        public void Deserialize_BadCategory_Rejected()
        {
            Assert.ThrowsAny<Exception>(() => new MoveSlotRequest().Deserialize(new byte[] { 200, 0, (byte)SlotCategory.Belt, 0 }));
        }

        [Fact]
        public void Deserialize_IndexOutOfRange_Rejected()
        {
            Assert.ThrowsAny<Exception>(() => new MoveSlotRequest().Deserialize(new byte[] { (byte)SlotCategory.Belt, 1, (byte)SlotCategory.Hand, 0 }));
        }

        [Fact]
        public void Deserialize_WrongSize_Rejected()
        {
            Assert.ThrowsAny<Exception>(() => new MoveSlotRequest().Deserialize(new byte[] { 0, 0, 0 }));
        }

        [Fact]
        public void Deserialize_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new MoveSlotRequest().Deserialize(null!));
        }
    }
}
