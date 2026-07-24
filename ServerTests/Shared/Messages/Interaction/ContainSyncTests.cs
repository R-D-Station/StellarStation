using System;
using Shared.Messages.Interaction;

namespace ServerTests.Shared.Messages.Interaction
{
    public class ContainSyncTests
    {
        [Fact]
        public void RoundTrip_PreservesField()
        {
            var src = new ContainSync { ContainerNetId = 4242 };
            var dst = new ContainSync();
            dst.Deserialize(src.Serialize());

            Assert.Equal(4242, dst.ContainerNetId);
        }

        [Fact]
        public void RoundTrip_ReleaseZero_ExactSize()
        {
            var src = new ContainSync { ContainerNetId = 0 };
            var bytes = src.Serialize();
            Assert.Equal(4, bytes.Length);

            var dst = new ContainSync();
            dst.Deserialize(bytes);
            Assert.Equal(0, dst.ContainerNetId);
        }

        [Fact]
        public void Deserialize_WrongLength_Rejected()
        {
            Assert.ThrowsAny<Exception>(() => new ContainSync().Deserialize(new byte[3]));
            Assert.ThrowsAny<Exception>(() => new ContainSync().Deserialize(new byte[5]));
        }

        [Fact]
        public void Deserialize_TrailingBytes_Rejected()
        {
            var bytes = new ContainSync { ContainerNetId = 7 }.Serialize();
            Array.Resize(ref bytes, bytes.Length + 1);
            Assert.ThrowsAny<Exception>(() => new ContainSync().Deserialize(bytes));
        }

        [Fact]
        public void Deserialize_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ContainSync().Deserialize(null!));
        }
    }
}
