using System;
using Shared.Messages.Player;

namespace ServerTests.Shared.Messages.Player
{
    public class LoginResponseTests
    {
        [Fact]
        public void LoginResponse_SerializeAndDeserialize_RoundTrips()
        {
            var original = new LoginResponse { NetId = 12345, TickRate = 40 };

            var deserialized = new LoginResponse();
            deserialized.Deserialize(original.Serialize());

            Assert.Equal(original.NetId, deserialized.NetId);
            Assert.Equal(original.TickRate, deserialized.TickRate);
        }

        [Fact]
        public void LoginResponse_SerializedSize_IsFourteenBytes()
        {
            var data = new LoginResponse
            {
                NetId = 1, TickRate = 30, ShapesMode = 1,
                ZoneFadeDistance = 6.5f, ZoneFadeVertical = 2f
            }.Serialize();
            Assert.Equal(14, data.Length);

            var deserialized = new LoginResponse();
            deserialized.Deserialize(data);
            Assert.Equal(1, deserialized.ShapesMode);
            Assert.Equal(6.5f, deserialized.ZoneFadeDistance);
            Assert.Equal(2f, deserialized.ZoneFadeVertical);
        }

        [Fact]
        public void Deserialize_NullData_ThrowsArgumentNullException()
        {
            var r = new LoginResponse();
            Assert.Throws<ArgumentNullException>(() => r.Deserialize(null!));
        }

        [Fact]
        public void Deserialize_WrongSize_ThrowsArgumentException()
        {
            var r = new LoginResponse();
            // Старый размер 4 байта ≠ 15 → mixed-build (старый пир) отлавливается строгой проверкой длины.
            Assert.Throws<ArgumentException>(() => r.Deserialize(new byte[4]));
        }
    }
}
