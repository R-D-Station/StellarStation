using System;
using Shared.Messages;
using Shared.Messages.Interaction;

namespace ServerTests.Shared.Messages.Interaction
{
    /// <summary>SwapHandRequest (wire 204): смена активной руки, 1Б РОВНО, Hand&lt;2.</summary>
    public class SwapHandRequestTests
    {
        [Fact]
        public void SerializeDeserialize_RoundTrips()
        {
            var original = new SwapHandRequest { Hand = 1 };
            var back = new SwapHandRequest();
            back.Deserialize(original.Serialize());

            Assert.Equal((byte)1, back.Hand);
            Assert.Equal(MessageType.SwapHand, back.Type);
        }

        [Fact]
        public void Serialize_ProducesOneByte()
        {
            Assert.Equal(1, new SwapHandRequest().Serialize().Length);
        }

        [Fact]
        public void Deserialize_Null_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new SwapHandRequest().Deserialize(null!));
        }

        [Fact]
        public void Deserialize_WrongSize_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new SwapHandRequest().Deserialize(Array.Empty<byte>()));
            Assert.Throws<ArgumentException>(() => new SwapHandRequest().Deserialize(new byte[2]));
        }

        [Fact]
        public void Deserialize_HandTooLarge_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => new SwapHandRequest().Deserialize(new byte[] { 2 }));
        }
    }
}
