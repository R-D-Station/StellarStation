using System;
using System.IO;
using Shared.Messages;
using Shared.Messages.Interaction;

namespace ServerTests.Shared.Messages.Interaction
{
    /// <summary>InteractIntent (wire 200): round-trip тайл/сущность + строгая валидация Deserialize
    /// (размер 19, enum-диапазон, HandIndex&lt;2, диапазон координат, TargetNetId≥-1, лишний хвост).</summary>
    public class InteractIntentTests
    {
        [Fact]
        public void SerializeDeserialize_TileTarget_RoundTrips()
        {
            var original = new InteractIntent
            {
                TargetKind = (byte)InteractTargetKind.Tile,
                Verb = (byte)InteractVerb.Primary,
                HandIndex = 0,
                TileX = 12,
                TileY = -7,
                TileZ = 3,
                TargetNetId = -1
            };

            var back = new InteractIntent();
            back.Deserialize(original.Serialize());

            Assert.Equal(original.TargetKind, back.TargetKind);
            Assert.Equal(original.Verb, back.Verb);
            Assert.Equal(original.HandIndex, back.HandIndex);
            Assert.Equal(original.TileX, back.TileX);
            Assert.Equal(original.TileY, back.TileY);
            Assert.Equal(original.TileZ, back.TileZ);
            Assert.Equal(original.TargetNetId, back.TargetNetId);
            Assert.Equal(MessageType.InteractIntent, back.Type);
        }

        [Fact]
        public void SerializeDeserialize_EntityTarget_RoundTrips()
        {
            var original = new InteractIntent
            {
                TargetKind = (byte)InteractTargetKind.Entity,
                Verb = (byte)InteractVerb.Alt,
                HandIndex = 1,
                TileX = 0,
                TileY = 0,
                TileZ = 0,
                TargetNetId = 42
            };

            var back = new InteractIntent();
            back.Deserialize(original.Serialize());

            Assert.Equal((byte)InteractTargetKind.Entity, back.TargetKind);
            Assert.Equal((byte)InteractVerb.Alt, back.Verb);
            Assert.Equal(1, back.HandIndex);
            Assert.Equal(42, back.TargetNetId);
        }

        [Fact]
        public void Serialize_ProducesNineteenBytes()
        {
            Assert.Equal(19, new InteractIntent().Serialize().Length);
        }

        [Fact]
        public void Deserialize_Null_ThrowsArgumentNullException()
        {
            var m = new InteractIntent();
            Assert.Throws<ArgumentNullException>(() => m.Deserialize(null!));
        }

        [Fact]
        public void Deserialize_WrongSize_ThrowsArgumentException()
        {
            var m = new InteractIntent();
            Assert.Throws<ArgumentException>(() => m.Deserialize(new byte[18]));
            Assert.Throws<ArgumentException>(() => m.Deserialize(new byte[20]));
        }

        [Fact]
        public void Deserialize_ExtraTrailingByte_ThrowsArgumentException()
        {
            // 19 корректных полей + 1 лишний байт = 20 → отвергается exact-size guard.
            var data = Build((byte)InteractTargetKind.Tile, (byte)InteractVerb.Primary, 0, 1, 1, 0, -1, extraTail: 1);
            Assert.Equal(20, data.Length);
            Assert.Throws<ArgumentException>(() => new InteractIntent().Deserialize(data));
        }

        [Fact]
        public void Deserialize_InvalidTargetKind_ThrowsInvalidOperationException()
        {
            var data = Build(9, (byte)InteractVerb.Primary, 0, 0, 0, 0, -1);
            Assert.Throws<InvalidOperationException>(() => new InteractIntent().Deserialize(data));
        }

        [Fact]
        public void Deserialize_InvalidVerb_ThrowsInvalidOperationException()
        {
            var data = Build((byte)InteractTargetKind.Tile, 9, 0, 0, 0, 0, -1);
            Assert.Throws<InvalidOperationException>(() => new InteractIntent().Deserialize(data));
        }

        [Fact]
        public void Deserialize_HandIndexTooLarge_ThrowsInvalidOperationException()
        {
            var data = Build((byte)InteractTargetKind.Tile, (byte)InteractVerb.Primary, 2, 0, 0, 0, -1);
            Assert.Throws<InvalidOperationException>(() => new InteractIntent().Deserialize(data));
        }

        [Fact]
        public void Deserialize_CoordOutOfRange_ThrowsInvalidOperationException()
        {
            var data = Build((byte)InteractTargetKind.Tile, (byte)InteractVerb.Primary, 0, 20_000_000, 0, 0, -1);
            Assert.Throws<InvalidOperationException>(() => new InteractIntent().Deserialize(data));
        }

        [Fact]
        public void Deserialize_TargetNetIdBelowMinusOne_ThrowsInvalidOperationException()
        {
            var data = Build((byte)InteractTargetKind.Tile, (byte)InteractVerb.Primary, 0, 0, 0, 0, -2);
            Assert.Throws<InvalidOperationException>(() => new InteractIntent().Deserialize(data));
        }

        private static byte[] Build(byte kind, byte verb, byte hand, int tx, int ty, int tz, int netId, byte? extraTail = null)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(kind);
            w.Write(verb);
            w.Write(hand);
            w.Write(tx);
            w.Write(ty);
            w.Write(tz);
            w.Write(netId);
            if (extraTail.HasValue) w.Write(extraTail.Value);
            w.Flush();
            return ms.ToArray();
        }
    }
}
