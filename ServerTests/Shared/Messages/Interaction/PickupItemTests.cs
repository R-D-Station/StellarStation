using System;
using Shared.Messages;
using Shared.Messages.Interaction;

namespace ServerTests.Shared.Messages.Interaction
{
    /// <summary>PickupItem (wire 201): whole-stack, только TargetNetId (4Б РОВНО), НЕТ hand/count/координат.</summary>
    public class PickupItemTests
    {
        [Fact]
        public void SerializeDeserialize_RoundTrips()
        {
            var original = new PickupItem { TargetNetId = 123 };
            var back = new PickupItem();
            back.Deserialize(original.Serialize());

            Assert.Equal(123, back.TargetNetId);
            Assert.Equal(MessageType.PickupItem, back.Type);
        }

        [Fact]
        public void Serialize_ProducesFourBytes()
        {
            Assert.Equal(4, new PickupItem().Serialize().Length);
        }

        [Fact]
        public void Deserialize_Null_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new PickupItem().Deserialize(null!));
        }

        [Fact]
        public void Deserialize_WrongSize_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new PickupItem().Deserialize(new byte[3]));
            Assert.Throws<ArgumentException>(() => new PickupItem().Deserialize(new byte[5]));
        }
    }
}
