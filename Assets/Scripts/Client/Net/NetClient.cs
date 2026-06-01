using Shared.Net;
using System;
using UnityEngine.UIElements;

namespace Client.Net
{
    /// <summary>
    /// Клиентский фасад над транспортом. Единственное место в клиенте, которое
    /// знает про ITransport. Остальной клиентский код общается с NetClient,
    /// а не с транспортом напрямую.
    ///
    /// Не зависит от того, заглушка под ним или реальная сеть.
    /// </summary>
    public class NetClient
    {
        private readonly ITransport _transport;
        private uint _inputSequence;

        /// <summary>Пришёл снапшот мира — клиентский визуал/предсказание реагирует на это.</summary>
        public event Action<WorldSnapshot> OnSnapshot;
        public event Action OnConnected;
        public event Action OnDisconnected;

        public bool IsConnected => _transport.IsConnected;

        public NetClient(ITransport transport)
        {
            _transport = transport;
            _transport.OnConnected += () => OnConnected?.Invoke();
            _transport.OnDisconnected += () => OnDisconnected?.Invoke();
            _transport.OnSnapshot += snap => OnSnapshot?.Invoke(snap);
        }

        public void Connect() => _transport.Connect();
        public void Disconnect() => _transport.Disconnect();

        /// <summary>Прокачать транспорт. Вызывать каждый кадр из Unity (Update).</summary>
        public void Poll() => _transport.Poll();

        /// <summary>
        /// Отправить намерение движения. Sequence проставляется здесь
        /// автоматически и понадобится для reconciliation на этапе 2.
        /// </summary>
        public void SendMove(IntentDirection direction, bool sprint)
        {
            var intent = new MoveIntent
            {
                Direction = direction,
                Sprint = sprint,
                Sequence = ++_inputSequence
            };
            _transport.SendIntent(intent);
        }
    }
}