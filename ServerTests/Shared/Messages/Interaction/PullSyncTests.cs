using System;
using Shared.Messages.Interaction;

namespace ServerTests.Shared.Messages.Interaction
{
    public class PullSyncTests
    {
        [Fact]
        public void RoundTrip_PreservesFields()
        {
            var src = new PullSync { PulledNetId = 4242, ItemDefId = 90 };
            var dst = new PullSync();
            dst.Deserialize(src.Serialize());

            Assert.Equal(4242, dst.PulledNetId);
            Assert.Equal((ushort)90, dst.ItemDefId);
        }

        [Fact]
        public void RoundTrip_ReleaseZeroZero()
        {
            var src = new PullSync { PulledNetId = 0, ItemDefId = 0 };
            var bytes = src.Serialize();
            Assert.Equal(6, bytes.Length);

            var dst = new PullSync();
            dst.Deserialize(bytes);
            Assert.Equal(0, dst.PulledNetId);
            Assert.Equal((ushort)0, dst.ItemDefId);
        }

        [Fact]
        public void Deserialize_WrongLength_Rejected()
        {
            Assert.ThrowsAny<Exception>(() => new PullSync().Deserialize(new byte[5]));
            Assert.ThrowsAny<Exception>(() => new PullSync().Deserialize(new byte[7]));
        }

        [Fact]
        public void Deserialize_TrailingBytes_Rejected()
        {
            var bytes = new PullSync { PulledNetId = 1, ItemDefId = 2 }.Serialize();
            Array.Resize(ref bytes, bytes.Length + 1);
            Assert.ThrowsAny<Exception>(() => new PullSync().Deserialize(bytes));
        }

        [Fact]
        public void Deserialize_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new PullSync().Deserialize(null!));
        }
    }
}
