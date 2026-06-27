using Shared.Messages;
using Shared.Messages.Interaction;

namespace ServerTests.Shared.Messages.Interaction
{
    public class UseIntentTests
    {
        [Fact]
        public void SerializeDeserialize_DoesNotThrow()
        {
            var msg = new UseIntent();
            var bytes = msg.Serialize();
            var back = new UseIntent();
            back.Deserialize(bytes);
            Assert.Equal(MessageType.UseIntent, back.Type);
            Assert.Empty(bytes);
        }
    }
}
