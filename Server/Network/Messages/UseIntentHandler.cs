using Shared.Messages;

namespace Server.Network.Messages
{
    /// <summary>UseIntent (пустой payload) → флаг ClientConnection.UseRequested.</summary>
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
