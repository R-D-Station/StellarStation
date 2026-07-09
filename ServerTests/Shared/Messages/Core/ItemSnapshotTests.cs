using System;
using Shared.Messages.Core;
using Shared.World.Items;

namespace ServerTests.Shared.Messages.Core
{
    /// <summary>ItemSnapshot: round-trip, точный per-item layout (19б), count-prefix, кап ДО аллокации, битый/усечённый/хвост, пустой, null.</summary>
    public class ItemSnapshotTests
    {
        private static ItemInstance Item(int netId, ushort defId, int x, int y, int z, byte stack) => new ItemInstance
        {
            NetId = netId, ItemDefId = defId, StackCount = stack, X = x, Y = y, Z = z, Placement = 0
        };

        [Fact]
        public void RoundTrip_PreservesAllWireFields()
        {
            var src = new ItemSnapshot { Items = new[] { Item(5, 42, 10, -20, 1, 3), Item(6, 7, 0, 0, 0, 255) } };
            var bytes = src.Serialize();

            var dst = new ItemSnapshot();
            dst.Deserialize(bytes);

            Assert.Equal(2, dst.Items.Length);
            Assert.Equal(5, dst.Items[0].NetId);
            Assert.Equal((ushort)42, dst.Items[0].ItemDefId);
            Assert.Equal(10, dst.Items[0].X);
            Assert.Equal(-20, dst.Items[0].Y);
            Assert.Equal(1, dst.Items[0].Z);
            Assert.Equal((byte)3, dst.Items[0].StackCount);
            Assert.Equal(6, dst.Items[1].NetId);
            Assert.Equal((byte)255, dst.Items[1].StackCount);
        }

        [Fact]
        public void Serialize_ExactSize_CountPrefixPlusPerItem19()
        {
            var src = new ItemSnapshot { Items = new[] { Item(1, 1, 1, 1, 1, 1), Item(2, 2, 2, 2, 2, 2), Item(3, 3, 3, 3, 3, 3) } };
            var bytes = src.Serialize();
            Assert.Equal(4 + 3 * ItemSnapshot.PerItemSize, bytes.Length); // 4 (count i32) + 3×19
        }

        [Fact]
        public void RoundTrip_Empty_CountZeroOnly()
        {
            var src = new ItemSnapshot { Items = Array.Empty<ItemInstance>() };
            var bytes = src.Serialize();
            Assert.Equal(4, bytes.Length); // только count-префикс

            var dst = new ItemSnapshot();
            dst.Deserialize(bytes);
            Assert.NotNull(dst.Items);
            Assert.Empty(dst.Items);
        }

        [Fact]
        public void Deserialize_HugeCount_RejectsBeforeAllocation()
        {
            // count = int.MaxValue, данных нет → отказ по капу, НЕ попытка аллоцировать (иначе OOM).
            var bytes = BitConverter.GetBytes(int.MaxValue);
            var msg = new ItemSnapshot();
            Assert.ThrowsAny<Exception>(() => msg.Deserialize(bytes));
        }

        [Fact]
        public void Deserialize_CountWithinCapButNoData_RejectedByBlockCheck()
        {
            // count = 500 (в пределах капа), но байт предметов нет → отказ ДО new[] (проверка длины блока).
            var bytes = BitConverter.GetBytes(500);
            var msg = new ItemSnapshot();
            Assert.ThrowsAny<Exception>(() => msg.Deserialize(bytes));
        }

        [Fact]
        public void Deserialize_NegativeCount_Rejected()
        {
            var bytes = BitConverter.GetBytes(-1);
            var msg = new ItemSnapshot();
            Assert.ThrowsAny<Exception>(() => msg.Deserialize(bytes));
        }

        [Fact]
        public void Deserialize_TruncatedItemBlock_Rejected()
        {
            var src = new ItemSnapshot { Items = new[] { Item(1, 1, 1, 1, 1, 1), Item(2, 2, 2, 2, 2, 2) } };
            var bytes = src.Serialize();
            Array.Resize(ref bytes, bytes.Length - 3); // отрезаем хвост последнего предмета
            var msg = new ItemSnapshot();
            Assert.ThrowsAny<Exception>(() => msg.Deserialize(bytes));
        }

        [Fact]
        public void Deserialize_TrailingBytes_Rejected()
        {
            var src = new ItemSnapshot { Items = new[] { Item(1, 1, 1, 1, 1, 1) } };
            var bytes = src.Serialize();
            Array.Resize(ref bytes, bytes.Length + 2); // лишние байты в хвосте
            var msg = new ItemSnapshot();
            Assert.ThrowsAny<Exception>(() => msg.Deserialize(bytes));
        }

        [Fact]
        public void Deserialize_Null_Throws()
        {
            var msg = new ItemSnapshot();
            Assert.Throws<ArgumentNullException>(() => msg.Deserialize(null!));
        }
    }
}
