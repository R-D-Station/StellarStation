using Shared.Messages;
using Shared.Messages.Interaction;

namespace Server.Network.Messages
{
    /// <summary>InteractIntent → ClientConnection.InteractQueue.</summary>
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
