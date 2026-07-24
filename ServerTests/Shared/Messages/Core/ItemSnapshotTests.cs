using System;
using Shared.Messages.Core;
using Shared.World.Items;

namespace ServerTests.Shared.Messages.Core
{
    /// <summary>ItemSnapshot: round-trip (float-позиции), точный per-item layout (20б), count-prefix, кап ДО аллокации, NaN/Inf-гард, битый/усечённый/хвост, пустой, null.</summary>
    public class ItemSnapshotTests
    {
        private static ItemInstance Item(int netId, ushort defId, float x, float y, float z, byte stack, byte open = 0) => new ItemInstance
        {
            NetId = netId, ItemDefId = defId, StackCount = stack, X = x, Y = y, Z = z, Placement = 0, Open = open
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
            Assert.Equal(10f, dst.Items[0].X);
            Assert.Equal(-20f, dst.Items[0].Y);
            Assert.Equal(1f, dst.Items[0].Z);
            Assert.Equal((byte)3, dst.Items[0].StackCount);
            Assert.Equal(6, dst.Items[1].NetId);
            Assert.Equal((byte)255, dst.Items[1].StackCount);
        }

        [Fact]
        public void RoundTrip_PreservesFractionalPositions()
        {
            var src = new ItemSnapshot { Items = new[] { Item(7, 2, 5.3f, -4.75f, 1.25f, 1) } };
            var dst = new ItemSnapshot();
            dst.Deserialize(src.Serialize());

            Assert.Equal(5.3f, dst.Items[0].X);
            Assert.Equal(-4.75f, dst.Items[0].Y);
            Assert.Equal(1.25f, dst.Items[0].Z);
        }

        [Fact]
        public void Deserialize_NaNCoordinate_Rejected()
        {
            var bytes = new ItemSnapshot { Items = new[] { Item(1, 1, 1f, 1f, 1f, 1) } }.Serialize();
            BitConverter.GetBytes(float.NaN).CopyTo(bytes, 10);
            Assert.ThrowsAny<Exception>(() => new ItemSnapshot().Deserialize(bytes));
        }

        [Fact]
        public void Deserialize_InfinityCoordinate_Rejected()
        {
            var bytes = new ItemSnapshot { Items = new[] { Item(1, 1, 1f, 1f, 1f, 1) } }.Serialize();
            BitConverter.GetBytes(float.PositiveInfinity).CopyTo(bytes, 14);
            Assert.ThrowsAny<Exception>(() => new ItemSnapshot().Deserialize(bytes));
        }

        [Fact]
        public void RoundTrip_PreservesOpenFlag()
        {
            var src = new ItemSnapshot { Items = new[] { Item(9, 3, 1, 2, 3, 1, 1), Item(10, 4, 0, 0, 0, 1, 0) } };
            var dst = new ItemSnapshot();
            dst.Deserialize(src.Serialize());

            Assert.Equal((byte)1, dst.Items[0].Open);
            Assert.Equal((byte)0, dst.Items[1].Open);
        }

        [Fact]
        public void Serialize_ExactSize_CountPrefixPlusPerItem20()
        {
            var src = new ItemSnapshot { Items = new[] { Item(1, 1, 1, 1, 1, 1), Item(2, 2, 2, 2, 2, 2), Item(3, 3, 3, 3, 3, 3) } };
            var bytes = src.Serialize();
            Assert.Equal(4 + 3 * ItemSnapshot.PerItemSize, bytes.Length); // 4 (count i32) + 3×20
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
