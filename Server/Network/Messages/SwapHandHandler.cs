using Shared.Messages;
using Shared.Messages.Interaction;

namespace Server.Network.Messages
{
    public sealed class SwapHandHandler : IClientMessageHandler
    {
        public MessageType Type => MessageType.SwapHand;
        public string LogName => "SwapHandRequest";

        public void Handle(ClientConnection client, byte[] data)
        {
            var swap = new SwapHandRequest();
            swap.Deserialize(data);
            client.SwapQueue.Enqueue(swap);
        }
    }
}
