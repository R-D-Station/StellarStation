using Shared.Messages;
using Shared.Messages.Lifts;

namespace Server.Network.Messages
{
    /// <summary>LiftFloorRequest → ClientConnection.LiftFloorQueue.</summary>
    public sealed class LiftFloorRequestHandler : IClientMessageHandler
    {
        public MessageType Type => MessageType.LiftFloorRequest;
        public string LogName => "LiftFloorRequest";

        public void Handle(ClientConnection client, byte[] data)
        {
            var request = new LiftFloorRequest();
            request.Deserialize(data);
            client.LiftFloorQueue.Enqueue(request);
        }
    }
}
