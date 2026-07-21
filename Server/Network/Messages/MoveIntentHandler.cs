using Shared.Messages;
using Shared.Messages.Core;

namespace Server.Network.Messages
{
    public sealed class MoveIntentHandler : IClientMessageHandler
    {
        public MessageType Type => MessageType.MoveIntent;
        public string LogName => "MoveIntent";

        public void Handle(ClientConnection client, byte[] data)
        {
            var intent = new MoveIntent();
            intent.Deserialize(data);
            client.IntentQueue.Enqueue(intent);
        }
    }
}
