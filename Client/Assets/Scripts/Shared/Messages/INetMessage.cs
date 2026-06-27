using System;

namespace Shared.Messages
{
    /// <summary>
    /// Базовый интерфейс всех сетевых сообщений (не зависит от библиотеки сериализации).
    /// </summary>
    public interface INetMessage
    {
        MessageType Type { get; }

        byte[] Serialize();

        void Deserialize(byte[] data);
    }
}