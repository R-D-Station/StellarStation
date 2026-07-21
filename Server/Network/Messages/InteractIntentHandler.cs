using Shared.Messages;
using Shared.Messages.Interaction;

namespace Server.Network.Messages
{
    public sealed class InteractIntentHandler : IClientMessageHandler
    {
        public MessageType Type => MessageType.InteractIntent;
        public string LogName => "InteractIntent";

        public void Handle(ClientConnection client, byte[] data)
        {
            var interact = new InteractIntent();
            interact.Deserialize(data);
            client.InteractQueue.Enqueue(interact);
        }
    }
}
