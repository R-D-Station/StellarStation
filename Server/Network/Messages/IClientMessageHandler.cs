using Shared.Messages;

namespace Server.Network.Messages
{
    /// <summary>Обработчик одного клиент→сервер сообщения; регистрируется в <see cref="MessageRouter"/> по <see cref="Type"/>.</summary>
    public interface IClientMessageHandler
    {
        MessageType Type { get; }

        /// <summary>Имя для лога "Bad {LogName} from #id" — сохраняет старые строки логов, не всегда совпадает с именем enum.</summary>
        string LogName { get; }

        void Handle(ClientConnection client, byte[] data);
    }
}
