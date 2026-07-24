using Shared.Messages;
using Shared.Messages.Interaction;

namespace Server.Network.Messages
{
    public sealed class TakeFromContainerHandler : IClientMessageHandler
    {
        public MessageType Type => MessageType.TakeFromContainer;
        public string LogName => "TakeFromContainer";

        public void Handle(ClientConnection client, byte[] data)
        {
            var msg = new TakeFromContainer();
            msg.Deserialize(data);
            client.TakeFromContainerQueue.Enqueue(msg);
        }
    }
}
