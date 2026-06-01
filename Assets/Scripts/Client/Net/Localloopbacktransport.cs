using System;
using Shared.Net;

namespace Client.Net
{
    /// <summary>
    /// Заглушка транспорта. Имитирует сервер ЛОКАЛЬНО, без сети и без задержки.
    /// Принимает intent -> сразу применяет -> сразу отдаёт snapshot.
    ///
    /// Назначение: клиент гоняется и рисуется ДО появления реального сервера.
    /// Когда владелец серверной части подключит настоящий транспорт на
    /// LiteNetLib, эта заглушка просто подменяется — клиент не меняется,
    /// т.к. работает только через ITransport.
    ///
    /// ВНИМАНИЕ: вся логика ниже помечена SERVER-OWNED. Это НЕ настоящая
    /// серверная симуляция, а минимальный фейк, чтобы труба ожила.
    /// Реальные правила движения/валидации — зона сервера.
    /// </summary>
    public class LocalLoopbackTransport : ITransport
    {
        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<WorldSnapshot> OnSnapshot;

        public bool IsConnected { get; private set; }

        // SERVER-OWNED (фейк): локальное состояние одной сущности игрока.
        // X/Y дробные (суб-тайл), Z — целый этаж.
        private readonly int _localNetId;
        private float _x, _y;
        private int _z;
        private byte _facing;
        private uint _serverTick;
        private uint _lastProcessedInput;

        // SERVER-OWNED (фейк): сколько суб-тайла проходим за один intent.
        // На реальном сервере это будет скорость * длительность тика, а не
        // фикс-шаг. Спринт здесь множит шаг — груба имитация Speed.
        private const float StepPerIntent = 0.25f;
        private const float SprintMultiplier = 1.6f;

        public LocalLoopbackTransport(int localNetId = 1, float startX = 0f, float startY = 0f, int startZ = 0)
        {
            _localNetId = localNetId;
            _x = startX;
            _y = startY;
            _z = startZ;
        }

        public void Connect()
        {
            IsConnected = true;
            OnConnected?.Invoke();
            EmitSnapshot(); // отдать стартовое состояние
        }

        public void Disconnect()
        {
            if (!IsConnected) return;
            IsConnected = false;
            OnDisconnected?.Invoke();
        }

        public void SendIntent(MoveIntent intent)
        {
            if (!IsConnected) return;

            // SERVER-OWNED (фейк): применяем намерение как дробное смещение.
            // Никакой валидации проходимости/коллизий — это сделает сервер.
            // Facing в снапшоте — в системе Entity.Direction (North=0,South=1,
            // East=2,West=3), НЕ IntentDirection. Маппим явно.
            float step = StepPerIntent * (intent.Sprint ? SprintMultiplier : 1f);
            switch (intent.Direction)
            {
                case IntentDirection.North: _y += step; _facing = 0; break; // Direction.North
                case IntentDirection.South: _y -= step; _facing = 1; break; // Direction.South
                case IntentDirection.East: _x += step; _facing = 2; break; // Direction.East
                case IntentDirection.West: _x -= step; _facing = 3; break; // Direction.West
                case IntentDirection.None: break;
            }

            _lastProcessedInput = intent.Sequence;
            _serverTick++;
            EmitSnapshot();
        }

        // На заглушке поллить нечего — сеть отсутствует. Метод есть ради контракта.
        public void Poll() { }

        private void EmitSnapshot()
        {
            var snap = new WorldSnapshot
            {
                ServerTick = _serverTick,
                LastProcessedInput = _lastProcessedInput,
                Entities = new[]
                {
                    new EntitySnapshot
                    {
                        NetId = _localNetId,
                        X = _x, Y = _y, Z = _z,
                        Facing = _facing
                    }
                }
            };
            OnSnapshot?.Invoke(snap);
        }
    }
}