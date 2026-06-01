using System;

namespace Shared.Net
{
    /// <summary>
    /// Контракт транспортного слоя. Точка входа для серверной/сетевой части.
    ///
    /// SERVER-OWNED: реальную реализацию (LiteNetLib) пишет владелец серверной
    /// части. Клиент работает ТОЛЬКО через этот интерфейс и не знает,
    /// что под ним — заглушка или настоящая сеть.
    ///
    /// Клиент: вызывает Connect/Disconnect/SendIntent, слушает OnSnapshot/OnConnected/OnDisconnected.
    /// </summary>
    public interface ITransport
    {
        /// <summary>Транспорт установил соединение и готов принимать intent.</summary>
        event Action OnConnected;

        /// <summary>Соединение потеряно/закрыто.</summary>
        event Action OnDisconnected;

        /// <summary>Пришёл новый снапшот мира от сервера.</summary>
        event Action<WorldSnapshot> OnSnapshot;

        bool IsConnected { get; }

        /// <summary>Начать подключение. Адрес/порт — забота реализации.</summary>
        void Connect();

        void Disconnect();

        /// <summary>Отправить намерение игрока серверу.</summary>
        void SendIntent(MoveIntent intent);

        /// <summary>
        /// Прокачать транспорт. Вызывается клиентом каждый кадр/тик.
        /// Реализация на LiteNetLib дёргает здесь PollEvents().
        /// </summary>
        void Poll();
    }
}