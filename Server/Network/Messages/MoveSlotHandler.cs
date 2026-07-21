using Shared.Messages;
using Shared.Messages.Interaction;

namespace Server.Network.Messages
{
    public sealed class MoveSlotHandler : IClientMessageHandler
    {
        public MessageType Type => MessageType.MoveSlot;
        public string LogName => "MoveSlotRequest";

        public void Handle(ClientConnection client, byte[] data)
        {
            var moveSlot = new MoveSlotRequest();
            moveSlot.Deserialize(data);
            client.MoveSlotQueue.Enqueue(moveSlot);
        }
    }
}
