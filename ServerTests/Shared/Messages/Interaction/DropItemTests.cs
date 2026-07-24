using System;
using Shared.Messages.Interaction;
using Shared.World.Items;

namespace ServerTests.Shared.Messages.Interaction
{
    public class DropItemTests
    {
        [Fact]
        public void RoundTrip()
        {
            var src = new DropItem { Category = SlotCategory.Pocket, Index = 1 };
            var bytes = src.Serialize();
            Assert.Equal(2, bytes.Length);

            var dst = new DropItem();
            dst.Deserialize(bytes);
            Assert.Equal(SlotCategory.Pocket, dst.Category);
            Assert.Equal((byte)1, dst.Index);
        }

        [Fact]
        public void Deserialize_BadCategory_Rejected()
        {
            Assert.ThrowsAny<Exception>(() => new DropItem().Deserialize(new byte[] { 200, 0 }));
        }

        [Fact]
        public void Deserialize_IndexOutOfRange_Rejected()
        {
            Assert.ThrowsAny<Exception>(() => new DropItem().Deserialize(new byte[] { (byte)SlotCategory.Belt, 1 }));
        }

        [Fact]
        public void Deserialize_WrongSize_Rejected()
        {
            Assert.ThrowsAny<Exception>(() => new DropItem().Deserialize(new byte[] { 0 }));
            Assert.ThrowsAny<Exception>(() => new DropItem().Deserialize(new byte[] { 0, 0, 0 }));
        }

        [Fact]
        public void Deserialize_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new DropItem().Deserialize(null!));
        }
    }
}
