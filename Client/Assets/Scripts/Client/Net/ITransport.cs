using Shared.Messages;
using Shared.Messages.Core;
using Shared.Messages.Player;
using System;

namespace Client.Net
{
    /// <summary>
    /// Транспортный слой для клиента.
    /// </summary>
    public interface ITransport
    {
        /// <summary>
        /// Соединение установлено
        /// </summary>
        event Action OnConnected;

        /// <summary>
        /// Соединение потеряно
        /// </summary>
        event Action OnDisconnected;

        /// <summary>
        /// Получен снапшот от сервера
        /// </summary>
        event Action<WorldSnapshot> OnWorldSnapshot;

        /// <summary>
        /// Пришёл ответ на подключение — содержит наш NetId
        /// </summary>
        event Action<LoginResponse> OnLoginResponse;

        bool IsConnected { get; }

        /// <summary>
        /// Подключиться к серверу
        /// </summary>
        void Connect(string address, int port);

        /// <summary>
        /// Отключиться
        /// </summary>
        void Disconnect();

        /// <summary>
        /// Универсальный метод отправки ЛЮБОГО сообщения.
        /// </summary>
        void Send<T>(T message) where T : struct, INetMessage;

        /// <summary>
        /// Обработка сетевых событий.
        /// </summary>
        void Poll();
    }
}