using Shared.Messages;

namespace Server.Network.Messages
{
    public sealed class UseIntentHandler : IClientMessageHandler
    {
        public MessageType Type => MessageType.UseIntent;
        public string LogName => "UseIntent";

        public void Handle(ClientConnection client, byte[] data)
        {
            client.UseRequested = true;
        }
    }
}
