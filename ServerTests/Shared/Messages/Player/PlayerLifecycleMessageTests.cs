using System;
using Shared.Messages.Player;

namespace ServerTests.Shared.Messages.Player
{
    public class PlayerLifecycleMessageTests
    {
        [Fact]
        public void PlayerJoined_RoundTrips()
        {
            var original = new PlayerJoined { NetId = 4242 };
            var deserialized = new PlayerJoined();
            deserialized.Deserialize(original.Serialize());
            Assert.Equal(original.NetId, deserialized.NetId);
        }

        [Fact]
        public void PlayerLeft_RoundTrips()
        {
            var original = new PlayerLeft { NetId = 7 };
            var deserialized = new PlayerLeft();
            deserialized.Deserialize(original.Serialize());
            Assert.Equal(original.NetId, deserialized.NetId);
        }

        [Fact]
        public void PlayerJoined_SerializedSize_IsFourBytes()
        {
            Assert.Equal(4, new PlayerJoined { NetId = 1 }.Serialize().Length);
        }

        [Fact]
        public void Deserialize_InvalidData_Throws()
        {
            Assert.Throws<ArgumentException>(() => new PlayerJoined().Deserialize(new byte[3]));
            Assert.Throws<ArgumentNullException>(() => new PlayerLeft().Deserialize(null!));
        }
    }
}
