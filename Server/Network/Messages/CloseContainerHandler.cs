using Shared.Messages;
using Shared.Messages.Interaction;

namespace Server.Network.Messages
{
    public sealed class CloseContainerHandler : IClientMessageHandler
    {
        public MessageType Type => MessageType.CloseContainer;
        public string LogName => "CloseContainer";

        public void Handle(ClientConnection client, byte[] data)
        {
            var msg = new CloseContainer();
            msg.Deserialize(data);
            client.CloseContainerQueue.Enqueue(msg);
        }
    }
}
