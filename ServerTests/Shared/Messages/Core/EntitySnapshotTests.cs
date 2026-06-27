using System;
using System.IO;
using Shared.Messages;
using Shared.Messages.Core;
using Shared.Simulation;

namespace ServerTests.Shared.Messages.Core
{
    public class EntitySnapshotTests
    {
        [Fact]
        public void EntitySnapshot_SerializeAndDeserialize_ReturnsEqualData()
        {
            var original = new EntitySnapshot
            {
                NetId = 123,
                X = 10.5f,
                Y = 20.5f,
                Z = 0.0f,
                Facing = 1,
                State = (byte)PlayerState.Move
            };

            var serialized = original.Serialize();
            var deserialized = new EntitySnapshot();
            deserialized.Deserialize(serialized);

            Assert.Equal(original.NetId, deserialized.NetId);
            Assert.Equal(original.X, deserialized.X);
            Assert.Equal(original.Y, deserialized.Y);
            Assert.Equal(original.Z, deserialized.Z);
            Assert.Equal(original.Facing, deserialized.Facing);
            Assert.Equal(original.State, deserialized.State);
        }

        [Fact]
        public void EntitySnapshot_SerializeDefaultValues_ReturnsDefaultData()
        {
            var original = new EntitySnapshot();

            var serialized = original.Serialize();
            var deserialized = new EntitySnapshot();
            deserialized.Deserialize(serialized);

            Assert.Equal(original.NetId, deserialized.NetId);
            Assert.Equal(original.X, deserialized.X);
            Assert.Equal(original.Y, deserialized.Y);
            Assert.Equal(original.Z, deserialized.Z);
            Assert.Equal(original.Facing, deserialized.Facing);
            Assert.Equal(original.State, deserialized.State); // дефолт 0 (Stand)
        }

        [Fact]
        public void EntitySnapshot_SerializeNonZeroState_RoundTrips()
        {
            var original = new EntitySnapshot { NetId = 7, State = (byte)PlayerState.Move };

            var serialized = original.Serialize();
            Assert.Equal(18, serialized.Length);

            var deserialized = new EntitySnapshot();
            deserialized.Deserialize(serialized);

            Assert.Equal((byte)PlayerState.Move, deserialized.State);
        }

        [Fact]
        public void Deserialize_NullData_ThrowsArgumentNullException()
        {
            var snapshot = new EntitySnapshot();
            Assert.Throws<ArgumentNullException>(() => snapshot.Deserialize(null!));
        }

        [Fact]
        public void Deserialize_WrongSize_ThrowsArgumentException()
        {
            var snapshot = new EntitySnapshot();
            var wrongSizeData = new byte[10];
            Assert.Throws<ArgumentException>(() => snapshot.Deserialize(wrongSizeData));
        }

        [Fact]
        public void Deserialize_InvalidXCoordinate_ThrowsInvalidOperationException()
        {
            var snapshot = new EntitySnapshot();
            var data = CreateTestData(netId: 1, x: float.NaN, y: 20, z: 5, facing: 2, state: 0);

            Assert.Throws<InvalidOperationException>(() => snapshot.Deserialize(data));
        }

        [Fact]
        public void Deserialize_InvalidYCoordinate_ThrowsInvalidOperationException()
        {
            var snapshot = new EntitySnapshot();
            var data = CreateTestData(netId: 1, x: 10, y: float.NaN, z: 5, facing: 2, state: 0);

            Assert.Throws<InvalidOperationException>(() => snapshot.Deserialize(data));
        }

        [Fact]
        public void Deserialize_InvalidZCoordinate_ThrowsInvalidOperationException()
        {
            var snapshot = new EntitySnapshot();
            var data = CreateTestData(netId: 1, x: 10, y: 20, z: float.NaN, facing: 2, state: 0);

            Assert.Throws<InvalidOperationException>(() => snapshot.Deserialize(data));
        }

        [Fact]
        public void TileX_ValidValue_CalculatesCorrectly()
        {
            var snapshot = new EntitySnapshot { X = 5.7f };
            Assert.Equal(5, snapshot.TileX);
        }

        [Fact]
        public void TileX_NegativeValue_CalculatesCorrectly()
        {
            var snapshot = new EntitySnapshot { X = -3.2f };
            Assert.Equal(-4, snapshot.TileX);
        }

        [Fact]
        public void TileY_ValidValue_CalculatesCorrectly()
        {
            var snapshot = new EntitySnapshot { Y = 10.9f };
            Assert.Equal(10, snapshot.TileY);
        }

        private static byte[] CreateTestData(int netId, float x, float y, float z, byte facing, byte state)
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            writer.Write(netId);
            writer.Write(x);
            writer.Write(y);
            writer.Write(z);
            writer.Write(facing);
            writer.Write(state); // 18 байт: иначе length-check (18) сработает раньше NaN-check

            return ms.ToArray();
        }
    }
}