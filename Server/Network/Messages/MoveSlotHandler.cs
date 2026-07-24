using Shared.Messages;
using Shared.Messages.Interaction;

namespace Server.Network.Messages
{
    /// <summary>MoveSlot → ClientConnection.MoveSlotQueue.</summary>
    public sealed class MoveSlotHandler : IClientMessageHandler
    {
        public MessageType Type => MessageType.MoveSlot;

        // сохраняет старую строку лога ("...Request"), не совпадает с именем enum MoveSlot
        public string LogName => "MoveSlotRequest";

        public void Handle(ClientConnection client, byte[] data)
        {
            var moveSlot = new MoveSlotRequest();
            moveSlot.Deserialize(data);
            client.MoveSlotQueue.Enqueue(moveSlot);
        }
    }
}
