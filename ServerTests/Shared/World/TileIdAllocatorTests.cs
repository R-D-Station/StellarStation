using System.Collections.Generic;
using Shared.World;

namespace ServerTests.Shared.World
{
    public class TileIdAllocatorTests
    {
        [Theory]
        [InlineData(new byte[] { }, 1)]
        [InlineData(new byte[] { 1 }, 2)]
        [InlineData(new byte[] { 1, 2 }, 3)]
        [InlineData(new byte[] { 2, 3 }, 1)]
        [InlineData(new byte[] { 1, 3 }, 2)]
        [InlineData(new byte[] { 2 }, 1)]
        public void SmallestFreeId_ReturnsLowestUnusedId(byte[] used, int expected)
        {
            Assert.Equal(expected, TileIdAllocator.SmallestFreeId(used));
        }

        [Fact]
        public void SmallestFreeId_AllIdsTaken_ReturnsZero()
        {
            var all = new List<byte>(255);
            for (int i = 1; i <= 255; i++) all.Add((byte)i);
            Assert.Equal(0, TileIdAllocator.SmallestFreeId(all));
        }

        [Theory]
        [InlineData(3, new byte[] { 1, 2 }, false)]
        [InlineData(2, new byte[] { 1, 2 }, true)]
        [InlineData(0, new byte[] { 1, 2 }, false)]
        public void IsTaken_TrueOnlyForNonZeroMember(int candidate, byte[] others, bool expected)
        {
            Assert.Equal(expected, TileIdAllocator.IsTaken((byte)candidate, others));
        }
    }
}
