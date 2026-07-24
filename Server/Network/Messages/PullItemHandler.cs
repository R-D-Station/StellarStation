using Shared.Messages;
using Shared.Messages.Interaction;

namespace Server.Network.Messages
{
    /// <summary>PullItem → ClientConnection.PullQueue.</summary>
    public sealed class PullItemHandler : IClientMessageHandler
    {
        public MessageType Type => MessageType.PullItem;
        public string LogName => "PullItem";

        public void Handle(ClientConnection client, byte[] data)
        {
            var msg = new PullItem();
            msg.Deserialize(data);
            client.PullQueue.Enqueue(msg);
        }
    }
}
