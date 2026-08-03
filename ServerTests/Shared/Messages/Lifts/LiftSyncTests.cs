using System;
using Shared.Messages.Lifts;

namespace ServerTests.Shared.Messages.Lifts
{
    public class LiftSyncTests
    {
        [Fact]
        public void RoundTrip_PreservesAllFields()
        {
            // Значения попарно РАЗЛИЧНЫ, иначе перестановка полей в Write/Read невидима; сверяются ВСЕ семь
            // (прошлая версия сверяла пять из семи — тест ЕДИНСТВЕННОГО бампа не покрывал то, ради чего бамп делался).
            var src = new LiftSync
            {
                LiftId = 7, FromY = 1.5f, ToY = 5.25f, StartTick = 12345u,
                BlocksPerTick = 0.05f, DwellUntilTick = 999u, Calls = 0xDEADBEEFu
            };

            var dst = new LiftSync();
            dst.Deserialize(src.Serialize());

            Assert.Equal(src.LiftId, dst.LiftId);
            Assert.Equal(src.FromY, dst.FromY);
            Assert.Equal(src.ToY, dst.ToY);
            Assert.Equal(src.StartTick, dst.StartTick);
            Assert.Equal(src.BlocksPerTick, dst.BlocksPerTick);
            Assert.Equal(src.DwellUntilTick, dst.DwellUntilTick);
            Assert.Equal(src.Calls, dst.Calls);
        }

        [Fact]
        public void SerializedSize_Is28Bytes()
        {
            var bytes = new LiftSync { LiftId = 1, FromY = 0f, ToY = 1f, StartTick = 0u, BlocksPerTick = 0.1f }.Serialize();
            Assert.Equal(28, bytes.Length);
        }

        [Fact]
        public void Deserialize_Null_Throws()
            => Assert.Throws<ArgumentNullException>(() => new LiftSync().Deserialize(null!));

        [Theory]
        [InlineData(0)]
        [InlineData(19)]
        [InlineData(21)]
        [InlineData(40)]
        public void Deserialize_WrongSize_Throws(int size)
            => Assert.Throws<ArgumentException>(() => new LiftSync().Deserialize(new byte[size]));

        [Theory]
        [InlineData(4)]
        [InlineData(8)]
        [InlineData(16)]
        public void Deserialize_NaN_Throws(int floatOffset)
        {
            var bytes = new LiftSync { LiftId = 1, FromY = 1f, ToY = 2f, StartTick = 3u, BlocksPerTick = 0.1f }.Serialize();
            BitConverter.GetBytes(float.NaN).CopyTo(bytes, floatOffset);
            Assert.Throws<InvalidOperationException>(() => new LiftSync().Deserialize(bytes));
        }

        [Fact]
        public void Deserialize_Infinity_Throws()
        {
            var bytes = new LiftSync { LiftId = 1, FromY = 1f, ToY = 2f, StartTick = 3u, BlocksPerTick = 0.1f }.Serialize();
            BitConverter.GetBytes(float.PositiveInfinity).CopyTo(bytes, 8);
            Assert.Throws<InvalidOperationException>(() => new LiftSync().Deserialize(bytes));
        }
    }
}
