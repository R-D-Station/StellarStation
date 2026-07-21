using Shared.Messages;

namespace Server.Network.Messages
{
    public interface IClientMessageHandler
    {
        MessageType Type { get; }
        string LogName { get; }
        void Handle(ClientConnection client, byte[] data);
    }
}
