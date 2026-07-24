using Shared.Messages;
using Shared.Messages.Interaction;

namespace Server.Network.Messages
{
    /// <summary>OpenContainer → ClientConnection.OpenContainerQueue.</summary>
    public sealed class OpenContainerHandler : IClientMessageHandler
    {
        public MessageType Type => MessageType.OpenContainer;
        public string LogName => "OpenContainer";

        public void Handle(ClientConnection client, byte[] data)
        {
            var msg = new OpenContainer();
            msg.Deserialize(data);
            client.OpenContainerQueue.Enqueue(msg);
        }
    }
}
