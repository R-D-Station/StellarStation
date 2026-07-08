using System;
using System.IO;
using System.Text;
using Shared.Messages;
using Shared.Messages.Core;
using Shared.Simulation;

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
                new EntitySnapshot { NetId = 1, X = 10, Y = 20, Z = 0, Facing = 0, State = (byte)PlayerState.Stand, Reason = (byte)LayingReason.None, Speed = 0.1f },
                new EntitySnapshot { NetId = 2, X = 30, Y = 40, Z = 1, Facing = 1, State = (byte)PlayerState.Laying, Reason = (byte)LayingReason.KnockedDown, Speed = 0.07f }
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
                Assert.Equal(original.Entities[i].State, deserialized.Entities[i].State);
                Assert.Equal(original.Entities[i].Reason, deserialized.Entities[i].Reason);
                Assert.Equal(original.Entities[i].Speed, deserialized.Entities[i].Speed);
            }
        }

        [Fact]
        public void WriteTo_ByteIdenticalToSerialize_AndExpectedLayout()
        {
            // WriteTo (zero-alloc broadcast-путь GameServer) и Serialize() дают байт-в-байт одно и то же (Serialize
            // зовёт WriteTo). Layout 2.5-A: 12 хедер + N·23 БЕЗ per-entity len-prefix.
            var snap = new WorldSnapshot
            {
                ServerTick = 8192,
                LastProcessedInput = 67890,
                Entities = new[]
                {
                    new EntitySnapshot { NetId = 1, X = 1.5f, Y = 2.5f, Z = 0, Facing = 0, State = (byte)PlayerState.Stand, Reason = 0, Speed = 0.1f },
                    new EntitySnapshot { NetId = 2, X = 3.5f, Y = 4.5f, Z = 1, Facing = 2, State = (byte)PlayerState.Move, Reason = (byte)LayingReason.KnockedDown, Speed = 0.2f },
                    new EntitySnapshot { NetId = 3, X = -7f, Y = 9f, Z = 2, Facing = 3, State = (byte)PlayerState.Laying, Reason = (byte)LayingReason.Voluntary, Speed = 0.07f }
                }
            };

            byte[] viaSerialize = snap.Serialize();

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            snap.WriteTo(bw);
            bw.Flush();
            byte[] viaWriteTo = ms.ToArray();

            Assert.Equal(viaSerialize, viaWriteTo); // байт-в-байт: WriteTo == Serialize (оба через WriteTo)
            Assert.Equal(12 + snap.Entities.Length * EntitySnapshot.SerializedSize, viaSerialize.Length); // 12 хедер + N×23 (len-prefix снят, 2.5-A)
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
            var incompleteData = new byte[] { 0x01, 0x00, 0x00, 0x00 }; // хватает только на ServerTick

            Assert.Throws<InvalidOperationException>(() => snapshot.Deserialize(incompleteData));
        }

        [Fact]
        public void WorldSnapshot_DeserializeCorruptedData_ThrowsException()
        {
            var snapshot = new WorldSnapshot();
            var corruptedData = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF };

            // Тип исключения не фиксируем: возможны EndOfStream/Overflow и др.
            Assert.ThrowsAny<Exception>(() => snapshot.Deserialize(corruptedData));
        }
    }
}