using Shared.Messages.Core;
using Shared.Messages.Player;
using System;

namespace Client.Net
{
    /// <summary>
    /// Фасад над транспортом для клиента. Единственное место в клиенте, кроме
    /// самого транспорта, которое работает с ITransport. Остальной код общается
    /// с NetClient, а не с конкретной реализацией транспорта.
    /// </summary>
    public class NetClient
    {
        private readonly ITransport _transport;
        private uint _inputSequence;

        /// <summary>Пришёл снапшот мира — отдаём его подписчикам.</summary>
        public event Action<WorldSnapshot> OnWorldSnapshot;

        /// <summary>Пришёл наш NetId от сервера при подключении.</summary>
        public event Action<LoginResponse> OnLoginResponse;

        public event Action OnConnected;
        public event Action OnDisconnected;

        public bool IsConnected => _transport.IsConnected;

        public NetClient(ITransport transport)
        {
            _transport = transport;
            _transport.OnConnected += () => OnConnected?.Invoke();
            _transport.OnDisconnected += () => OnDisconnected?.Invoke();
            _transport.OnWorldSnapshot += snap => OnWorldSnapshot?.Invoke(snap);
            _transport.OnLoginResponse += login => OnLoginResponse?.Invoke(login);
        }

        public void Connect(string address, int port) => _transport.Connect(address, port);
        public void Disconnect() => _transport.Disconnect();

        /// <summary>Прокачать транспорт. Вызывать каждый кадр из Unity (Update).</summary>
        public void Poll() => _transport.Poll();

        /// <summary>
        /// Отправить намерение движения. Возвращает проставленный Sequence —
        /// он нужен предсказанию, чтобы привязать локальный шаг к серверному
        /// подтверждению (reconciliation).
        /// </summary>
        public uint SendMove(IntentDirection direction, bool sprint)
        {
            var intent = new MoveIntent
            {
                Direction = direction,
                Sprint = sprint,
                Sequence = ++_inputSequence
            };
            _transport.Send(intent);
            return intent.Sequence;
        }
    }
}