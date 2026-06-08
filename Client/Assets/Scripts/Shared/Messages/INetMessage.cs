using System;

namespace Shared.Messages
{
    /// <summary>
    /// Ѕазовый интерфейс дл€ всех сетевых сообщений.
    /// Ќе зависит от конкретной библиотеки сериализации!
    /// </summary>
    public interface INetMessage
    {
        MessageType Type { get; }

        /// <summary>
        /// —ериализовать сообщение в массив байт
        /// </summary>
        byte[] Serialize();

        /// <summary>
        /// ƒесериализовать сообщение из массива байт
        /// </summary>
        void Deserialize(byte[] data);
    }
}