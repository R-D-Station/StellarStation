using Shared.Messages;
using Shared.Messages.Interaction;

namespace Server.Network.Messages
{
    public sealed class DropItemHandler : IClientMessageHandler
    {
        public MessageType Type => MessageType.DropItem;
        public string LogName => "DropItem";

        public void Handle(ClientConnection client, byte[] data)
        {
            var drop = new DropItem();
            drop.Deserialize(data);
            client.DropQueue.Enqueue(drop);
        }
    }
}
