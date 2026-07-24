using Shared.Messages;
using Shared.Messages.Interaction;

namespace Server.Network.Messages
{
    /// <summary>SwapHand → ClientConnection.SwapQueue.</summary>
    public sealed class SwapHandHandler : IClientMessageHandler
    {
        public MessageType Type => MessageType.SwapHand;

        // сохраняет старую строку лога ("...Request"), не совпадает с именем enum SwapHand
        public string LogName => "SwapHandRequest";

        public void Handle(ClientConnection client, byte[] data)
        {
            var swap = new SwapHandRequest();
            swap.Deserialize(data);
            client.SwapQueue.Enqueue(swap);
        }
    }
}
