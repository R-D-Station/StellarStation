using Shared.Messages;
using Shared.Messages.Interaction;

namespace Server.Network.Messages
{
    public sealed class PutInContainerHandler : IClientMessageHandler
    {
        public MessageType Type => MessageType.PutInContainer;
        public string LogName => "PutInContainer";

        public void Handle(ClientConnection client, byte[] data)
        {
            var msg = new PutInContainer();
            msg.Deserialize(data);
            client.PutInContainerQueue.Enqueue(msg);
        }
    }
}
