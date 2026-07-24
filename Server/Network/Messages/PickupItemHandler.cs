using Shared.Messages;
using Shared.Messages.Interaction;

namespace Server.Network.Messages
{
    /// <summary>PickupItem → ClientConnection.PickupQueue.</summary>
    public sealed class PickupItemHandler : IClientMessageHandler
    {
        public MessageType Type => MessageType.PickupItem;
        public string LogName => "PickupItem";

        public void Handle(ClientConnection client, byte[] data)
        {
            var pickup = new PickupItem();
            pickup.Deserialize(data);
            client.PickupQueue.Enqueue(pickup);
        }
    }
}
