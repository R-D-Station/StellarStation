using System;
using Shared.Messages.Interaction;

namespace ServerTests.Shared.Messages.Interaction
{
    public class ContainerSyncTests
    {
        private static ContainerItem Item(ushort defId, byte stack) => new ContainerItem { ItemDefId = defId, StackCount = stack };

        [Fact]
        public void RoundTrip_PreservesNetIdAndItems()
        {
            var src = new ContainerSync
            {
                ContainerNetId = 4242,
                Items = new[] { Item(10, 3), Item(7, 1), Item(42, 255) }
            };
            var bytes = src.Serialize();

            var dst = new ContainerSync();
            dst.Deserialize(bytes);

            Assert.Equal(4242, dst.ContainerNetId);
            Assert.Equal(3, dst.Items.Length);
            Assert.Equal((ushort)10, dst.Items[0].ItemDefId);
            Assert.Equal((byte)3, dst.Items[0].StackCount);
            Assert.Equal((ushort)42, dst.Items[2].ItemDefId);
            Assert.Equal((byte)255, dst.Items[2].StackCount);
        }

        [Fact]
        public void RoundTrip_Empty()
        {
            var src = new ContainerSync { ContainerNetId = 1, Items = Array.Empty<ContainerItem>() };
            var bytes = src.Serialize();
            Assert.Equal(6, bytes.Length);

            var dst = new ContainerSync();
            dst.Deserialize(bytes);
            Assert.Empty(dst.Items);
        }

        [Fact]
        public void RoundTrip_MaxItems()
        {
            var items = new ContainerItem[255];
            for (int i = 0; i < items.Length; i++) items[i] = Item((ushort)(i + 1), 1);
            var src = new ContainerSync { ContainerNetId = 9, Items = items };

            var dst = new ContainerSync();
            dst.Deserialize(src.Serialize());
            Assert.Equal(255, dst.Items.Length);
        }

        [Fact]
        public void Serialize_ExactSize()
        {
            var src = new ContainerSync { ContainerNetId = 1, Items = new[] { Item(1, 1), Item(2, 1) } };
            Assert.Equal(6 + 2 * ContainerSync.PerItemSize, src.Serialize().Length);
        }

        [Fact]
        public void Deserialize_HugeCount_RejectsBeforeAllocation()
        {
            var bytes = new byte[] { 0, 0, 0, 0, 0xFF, 0xFF };
            Assert.ThrowsAny<Exception>(() => new ContainerSync().Deserialize(bytes));
        }

        [Fact]
        public void Deserialize_ZeroItemDefId_Rejected()
        {
            var bytes = new byte[] { 0, 0, 0, 0, 1, 0, 0, 0, 1 };
            Assert.ThrowsAny<Exception>(() => new ContainerSync().Deserialize(bytes));
        }

        [Fact]
        public void Deserialize_TrailingBytes_Rejected()
        {
            var bytes = new ContainerSync { ContainerNetId = 1, Items = new[] { Item(1, 1) } }.Serialize();
            Array.Resize(ref bytes, bytes.Length + 1);
            Assert.ThrowsAny<Exception>(() => new ContainerSync().Deserialize(bytes));
        }

        [Fact]
        public void Deserialize_TruncatedBlock_Rejected()
        {
            var bytes = new ContainerSync { ContainerNetId = 1, Items = new[] { Item(1, 1), Item(2, 1) } }.Serialize();
            Array.Resize(ref bytes, bytes.Length - 2);
            Assert.ThrowsAny<Exception>(() => new ContainerSync().Deserialize(bytes));
        }

        [Fact]
        public void Deserialize_TooShortForHeader_Rejected()
        {
            Assert.ThrowsAny<Exception>(() => new ContainerSync().Deserialize(new byte[] { 0, 0, 0 }));
        }

        [Fact]
        public void Deserialize_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ContainerSync().Deserialize(null!));
        }
    }
}
