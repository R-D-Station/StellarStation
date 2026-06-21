using Shared.Messages.Core;
using Shared.Messages.Player;
using Shared.Messages.Interaction;
using System;

namespace Client.Net
{
    /// <summary>Обёртка над ITransport: события сервера и отправка intent'ов.</summary>
    public class NetClient
    {
        private readonly ITransport _transport;
        private uint _inputSequence;

        public event Action<WorldSnapshot> OnWorldSnapshot;
        public event Action<LoginResponse> OnLoginResponse;
        public event Action<MapDataMessage> OnMapData;
        public event Action<TileUpdate> OnTileUpdate;
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
            _transport.OnMapData += map => OnMapData?.Invoke(map);
            _transport.OnTileUpdate += tu => OnTileUpdate?.Invoke(tu);
        }

        public void Connect(string address, int port) => _transport.Connect(address, port);
        public void Disconnect() => _transport.Disconnect();
        public void Poll() => _transport.Poll();

        /// <summary>Отправить намерение движения; возвращает его Sequence (для reconciliation).</summary>
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

        /// <summary>«Использовать» (E): лестница/лифт под игроком. Без предсказания — z меняет сервер.</summary>
        public void SendUse() => _transport.Send(new UseIntent());
    }
}