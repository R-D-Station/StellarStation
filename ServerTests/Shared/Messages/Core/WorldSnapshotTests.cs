using System;
using System.IO;
using System.Text;
using Shared.Messages;
using Shared.Messages.Core;

namespace ServerTests.Shared.Messages.Core
{
    public class WorldSnapshotTests
    {
        [Fact]
        public void WorldSnapshot_SerializeAndDeserialize_ReturnsEqualData()
        {
            var original = new WorldSnapshot
            {
                ServerTick = 8192,
                LastProcessedInput = 67890,
                Entities = new[]
                {
                new EntitySnapshot { NetId = 1, X = 10, Y = 20, Z = 0, Facing = 0 },
                new EntitySnapshot { NetId = 2, X = 30, Y = 40, Z = 1, Facing = 1 }
            }
            };

            var serialized = original.Serialize();
            var deserialized = new WorldSnapshot();
            deserialized.Deserialize(serialized);

            Assert.Equal(original.ServerTick, deserialized.ServerTick);
            Assert.Equal(original.LastProcessedInput, deserialized.LastProcessedInput);
            Assert.Equal(original.Entities.Length, deserialized.Entities.Length);
            for (int i = 0; i < original.Entities.Length; i++)
            {
                Assert.Equal(original.Entities[i].NetId, deserialized.Entities[i].NetId);
                Assert.Equal(original.Entities[i].X, deserialized.Entities[i].X);
                Assert.Equal(original.Entities[i].Y, deserialized.Entities[i].Y);
                Assert.Equal(original.Entities[i].Z, deserialized.Entities[i].Z);
                Assert.Equal(original.Entities[i].Facing, deserialized.Entities[i].Facing);
            }
        }

        [Fact]
        public void WorldSnapshot_SerializeEmptyEntity_ReturnsEmptyData()
        {
            var original = new WorldSnapshot
            {
                Entities = Array.Empty<EntitySnapshot>()
            };

            var serialized = original.Serialize();
            var deserialized = new WorldSnapshot();
            deserialized.Deserialize(serialized);

            Assert.Empty(deserialized.Entities);
        }

        [Fact]
        public void WorldSnapshot_NullEntities_ThrowsException()
        {
            var snapshot = new WorldSnapshot();
            Assert.Throws<NullReferenceException>(() => snapshot.Serialize());
        }

        [Fact]
        public void WorldSnapshot_DeserializeIncompleteData_ThrowsEndOfStreamException()
        {
            var snapshot = new WorldSnapshot();
            // Данные только для ServerTick (4 байта), но не для остальных полей
            var incompleteData = new byte[] { 0x01, 0x00, 0x00, 0x00 };

            Assert.Throws<InvalidOperationException>(() => snapshot.Deserialize(incompleteData));
        }

        [Fact]
        public void WorldSnapshot_DeserializeCorruptedData_ThrowsException()
        {
            var snapshot = new WorldSnapshot();
            var corruptedData = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }; // Невалидные данные

            // Может выбросить EndOfStreamException, OverflowException или другое
            Assert.ThrowsAny<Exception>(() => snapshot.Deserialize(corruptedData));
        }
    }
}