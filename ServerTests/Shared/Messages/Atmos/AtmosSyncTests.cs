using System;
using Shared.Messages.Atmos;

namespace ServerTests.Shared.Messages.Atmos
{
    public class AtmosSyncTests
    {
        [Fact]
        public void RoundTrip_PreservesFields()
        {
            var src = new AtmosSync { PressureKpa = 101.3f, OxygenKpa = 21.2f };
            var dst = new AtmosSync();
            dst.Deserialize(src.Serialize());

            Assert.Equal(101.3f, dst.PressureKpa);
            Assert.Equal(21.2f, dst.OxygenKpa);
        }

        [Fact]
        public void Serialize_SizeIsEight()
        {
            Assert.Equal(8, new AtmosSync { PressureKpa = 0f, OxygenKpa = 0f }.Serialize().Length);
        }

        [Fact]
        public void RoundTrip_ZeroZero()
        {
            var dst = new AtmosSync();
            dst.Deserialize(new AtmosSync { PressureKpa = 0f, OxygenKpa = 0f }.Serialize());
            Assert.Equal(0f, dst.PressureKpa);
            Assert.Equal(0f, dst.OxygenKpa);
        }

        [Fact]
        public void Deserialize_WrongLength_Rejected()
        {
            Assert.ThrowsAny<Exception>(() => new AtmosSync().Deserialize(new byte[7]));
            Assert.ThrowsAny<Exception>(() => new AtmosSync().Deserialize(new byte[9]));
        }

        [Fact]
        public void Deserialize_TrailingBytes_Rejected()
        {
            var bytes = new AtmosSync { PressureKpa = 1f, OxygenKpa = 2f }.Serialize();
            Array.Resize(ref bytes, bytes.Length + 1);
            Assert.ThrowsAny<Exception>(() => new AtmosSync().Deserialize(bytes));
        }

        [Fact]
        public void Deserialize_Null_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new AtmosSync().Deserialize(null!));
        }

        [Fact]
        public void Deserialize_NaNPressure_Rejected()
        {
            var bytes = new AtmosSync { PressureKpa = float.NaN, OxygenKpa = 1f }.Serialize();
            Assert.ThrowsAny<Exception>(() => new AtmosSync().Deserialize(bytes));
        }

        [Fact]
        public void Deserialize_InfinityOxygen_Rejected()
        {
            var bytes = new AtmosSync { PressureKpa = 1f, OxygenKpa = float.PositiveInfinity }.Serialize();
            Assert.ThrowsAny<Exception>(() => new AtmosSync().Deserialize(bytes));
        }
    }
}
