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
                State = (byte)PlayerState.Move,
                Reason = (byte)LayingReason.Voluntary,
                Speed = 0.25f
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
            Assert.Equal(original.Reason, deserialized.Reason);
            Assert.Equal(original.Speed, deserialized.Speed);
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
            Assert.Equal(original.Reason, deserialized.Reason); // дефолт 0 (None)
            Assert.Equal(original.Speed, deserialized.Speed); // дефолт 0
        }

        [Fact]
        public void EntitySnapshot_SerializeNonZeroStateAndReason_RoundTrips()
        {
            var original = new EntitySnapshot
            {
                NetId = 7,
                State = (byte)PlayerState.Laying,
                Reason = (byte)LayingReason.KnockedDown,
                Speed = 0.15f
            };

            var serialized = original.Serialize();
            Assert.Equal(EntitySnapshot.SerializedSize, serialized.Length);

            var deserialized = new EntitySnapshot();
            deserialized.Deserialize(serialized);

            Assert.Equal((byte)PlayerState.Laying, deserialized.State);
            Assert.Equal((byte)LayingReason.KnockedDown, deserialized.Reason);
            Assert.Equal(0.15f, deserialized.Speed);
        }

        [Fact]
        public void ReadFrom_MatchesWriteTo_RoundTrips()
        {
            // 2.5-A: клиент читает сущность напрямую из reader (WorldSnapshot без per-entity len-prefix). ReadFrom
            // должен байт-в-байт обратить WriteTo (тот же порядок/SerializedSize байт), что и Deserialize(byte[]).
            var original = new EntitySnapshot
            {
                NetId = -42, X = -7.5f, Y = 9.25f, Z = 3.0f,
                Facing = 3, State = (byte)PlayerState.Laying, Reason = (byte)LayingReason.KnockedDown, Speed = 0.07f
            };

            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                original.WriteTo(w);
            Assert.Equal(EntitySnapshot.SerializedSize, ms.Length); // без len-prefix

            ms.Position = 0;
            using var r = new BinaryReader(ms);
            var back = EntitySnapshot.ReadFrom(r);

            Assert.Equal(original.NetId, back.NetId);
            Assert.Equal(original.X, back.X);
            Assert.Equal(original.Y, back.Y);
            Assert.Equal(original.Z, back.Z);
            Assert.Equal(original.Facing, back.Facing);
            Assert.Equal(original.State, back.State);
            Assert.Equal(original.Reason, back.Reason);
            Assert.Equal(original.Speed, back.Speed);
            Assert.Equal(ms.Length, ms.Position); // прочитано ровно SerializedSize байт
        }

        [Fact]
        public void EntitySnapshot_WornUniformDefId_RoundTrips()
        {
            var original = new EntitySnapshot { NetId = 5, Speed = 0.1f, WornUniformDefId = 4242 };

            var back = new EntitySnapshot();
            back.Deserialize(original.Serialize());
            Assert.Equal((ushort)4242, back.WornUniformDefId);

            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                original.WriteTo(w);
            ms.Position = 0;
            using var r = new BinaryReader(ms);
            Assert.Equal((ushort)4242, EntitySnapshot.ReadFrom(r).WornUniformDefId);
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
        public void Deserialize_InvalidSpeed_ThrowsInvalidOperationException()
        {
            var snapshot = new EntitySnapshot();
            var data = CreateTestData(netId: 1, x: 10, y: 20, z: 5, facing: 2, state: 0, reason: 0, speed: float.NaN);

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

        private static byte[] CreateTestData(int netId, float x, float y, float z, byte facing, byte state, byte reason = 0, float speed = 0.1f, float vz = 0f)
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            writer.Write(netId);
            writer.Write(x);
            writer.Write(y);
            writer.Write(z);
            writer.Write(facing);
            writer.Write(state);
            writer.Write(reason);
            writer.Write(speed);
            writer.Write(vz);
            writer.Write((ushort)0); // 29 байт: иначе length-check (29) сработает раньше NaN-check

            return ms.ToArray();
        }
    }
}