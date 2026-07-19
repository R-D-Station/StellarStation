using System;
using System.IO;
using Shared.Messages;
using Shared.Messages.Core;

namespace ServerTests.Shared.Messages.Core
{
    public class MoveIntentTests
    {
        [Fact]
        public void MoveIntent_SerializeAndDeserialize_ReturnsEqualData()
        {
            var original = new MoveIntent
            {
                Direction = IntentDirection.North,
                Sprint = false,
                LayToggle = true,
                Sequence = 898316,
            };

            var serialized = original.Serialize();
            var deserialized = new MoveIntent();
            deserialized.Deserialize(serialized);

            Assert.Equal(original.Direction, deserialized.Direction);
            Assert.Equal(original.Sprint, deserialized.Sprint);
            Assert.Equal(original.LayToggle, deserialized.LayToggle);
            Assert.Equal(original.Sequence, deserialized.Sequence);
        }

        [Fact]
        public void MoveIntent_LayToggleBit_RoundTrips()
        {
            // Бит LayToggle независим от Sprint — проверяем все 4 комбинации.
            foreach (var (sprint, lay) in new[] { (false, false), (true, false), (false, true), (true, true) })
            {
                var original = new MoveIntent
                {
                    Direction = IntentDirection.East,
                    Sprint = sprint,
                    LayToggle = lay,
                    Sequence = 5,
                };

                var deserialized = new MoveIntent();
                deserialized.Deserialize(original.Serialize());

                Assert.Equal(sprint, deserialized.Sprint);
                Assert.Equal(lay, deserialized.LayToggle);
            }
        }

        [Fact]
        public void MoveIntent_SerializeDefaultValues_ReturnsDefaultData()
        {
            var original = new MoveIntent();

            var serialized = original.Serialize();
            var deserialized = new MoveIntent();
            deserialized.Deserialize(serialized);

            Assert.Equal(original.Direction, deserialized.Direction);
            Assert.Equal(original.Sprint, deserialized.Sprint);
            Assert.Equal(original.LayToggle, deserialized.LayToggle);
            Assert.Equal(original.Sequence, deserialized.Sequence);
        }

        [Fact]
        public void Deserialize_NullData_ThrowsArgumentNullException()
        {
            var intent = new MoveIntent();
            Assert.Throws<ArgumentNullException>(() => intent.Deserialize(null!));
        }

        [Fact]
        public void Deserialize_WrongSize_ThrowsArgumentException()
        {
            var intent = new MoveIntent();
            var wrongSizeData = new byte[10];
            Assert.Throws<ArgumentException>(() => intent.Deserialize(wrongSizeData));
        }

        [Fact]
        public void Deserialize_TooShortData_ThrowsArgumentException()
        {
            var intent = new MoveIntent();
            var tooShortData = new byte[3]; // меньше требуемых 7 байт
            Assert.Throws<ArgumentException>(() => intent.Deserialize(tooShortData));
        }

        [Fact]
        public void Deserialize_InvalidDirection_ThrowsInvalidOperationException()
        {
            var intent = new MoveIntent();
            var data = CreateTestData(direction: 99, sprint: true, sequence: 42); // 99 — нет такого направления

            Assert.Throws<InvalidOperationException>(() => intent.Deserialize(data));
        }

        [Fact]
        public void Deserialize_TooLargeSequence_ThrowsInvalidOperationException()
        {
            var intent = new MoveIntent();
            var data = CreateTestData(direction: 1, sprint: true, sequence: 20_000_000); // выше допустимого предела

            Assert.Throws<InvalidOperationException>(() => intent.Deserialize(data));
        }

        [Fact]
        public void Serialize_AllDirections_WorkCorrectly()
        {
            var directions = new[]
            {
                IntentDirection.None,
                IntentDirection.North,
                IntentDirection.South,
                IntentDirection.East,
                IntentDirection.West
            };

            foreach (var direction in directions)
            {
                var intent = new MoveIntent
                {
                    Direction = direction,
                    Sprint = false,
                    Sequence = 0
                };

                var data = intent.Serialize();
                var deserialized = new MoveIntent();
                deserialized.Deserialize(data);

                Assert.Equal(direction, deserialized.Direction);
            }
        }

        [Fact]
        public void Serialize_SprintValues_WorkCorrectly()
        {
            var intentTrue = new MoveIntent
            {
                Direction = IntentDirection.North,
                Sprint = true,
                Sequence = 1
            };

            var dataTrue = intentTrue.Serialize();
            var deserializedTrue = new MoveIntent();
            deserializedTrue.Deserialize(dataTrue);
            Assert.True(deserializedTrue.Sprint);

            var intentFalse = new MoveIntent
            {
                Direction = IntentDirection.North,
                Sprint = false,
                Sequence = 1
            };

            var dataFalse = intentFalse.Serialize();
            var deserializedFalse = new MoveIntent();
            deserializedFalse.Deserialize(dataFalse);
            Assert.False(deserializedFalse.Sprint);
        }

        [Fact]
        public void Serialize_SequenceBoundaryValues_WorkCorrectly()
        {
            var intentMin = new MoveIntent
            {
                Direction = IntentDirection.North,
                Sprint = false,
                Sequence = 0
            };

            var dataMin = intentMin.Serialize();
            var deserializedMin = new MoveIntent();
            deserializedMin.Deserialize(dataMin);
            Assert.Equal(0u, deserializedMin.Sequence);

            var intentMax = new MoveIntent
            {
                Direction = IntentDirection.North,
                Sprint = false,
                Sequence = 10_000_000
            };

            var dataMax = intentMax.Serialize();
            var deserializedMax = new MoveIntent();
            deserializedMax.Deserialize(dataMax);
            Assert.Equal(10_000_000u, deserializedMax.Sequence);
        }

        private static byte[] CreateTestData(byte direction, bool sprint, uint sequence, bool layToggle = false, bool jump = false)
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            writer.Write(direction);
            writer.Write(sprint);
            writer.Write(layToggle);
            writer.Write(jump);
            writer.Write(sequence);

            return ms.ToArray();
        }
    }
}