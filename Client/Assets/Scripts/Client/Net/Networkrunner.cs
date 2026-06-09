using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Shared.Messages.Core;
using Shared.Messages.Player;
using Client.Net.View;
using Client.Net.Prediction;
using Client.Gameplay.Input;     // PlayerControl (сгенерированный Input System)
using Client.Gameplay.Entities;  // Entity.Direction для спрайта своего игрока

namespace Client.Net
{
    /// <summary>
    /// Точка сборки сетевого клиента. Создаёт NetClient поверх транспорта,
    /// собирает input -> intent (раз в серверный тик), раздаёт снапшоты.
    ///
    /// Свой игрок (NetId == LocalNetId) предсказывается локально через
    /// PlayerPredictor + reconciliation — двигается без задержки ввода.
    /// Чужие сущности интерполируются через NetEntityView.
    /// </summary>
    public class NetworkRunner : MonoBehaviour
    {
        [SerializeField] private NetEntityView _entityViewPrefab;

        [Tooltip("Должно совпадать с TickRate сервера (SVars.TickRate).")]
        [SerializeField] private int _tickRate = 30;

        [Tooltip("Высота этажа в юнитах Unity по оси Y. Для плоского теста = 0.")]
        [SerializeField] private float _floorHeight = 0f;

        private NetClient _net;
        private PlayerControl _controls;
        private readonly Dictionary<int, NetEntityView> _views = new Dictionary<int, NetEntityView>();
        private string _address = "127.0.0.1";
        private int _port = 7777;

        // Предсказание своего игрока
        private readonly PlayerPredictor _predictor = new PlayerPredictor();
        private NetEntityView _localView;

        // Тик-аккумулятор: шлём intent раз в 1/_tickRate, а не каждый кадр.
        private float _tickAccumulator;
        private float TickInterval => 1f / _tickRate;

        /// <summary>NetId нашей сущности. -1, пока сервер не прислал LoginResponse.</summary>
        public int LocalNetId { get; private set; } = -1;

        private void Awake()
        {
            ITransport transport = new LiteNetLibTransport();

            _net = new NetClient(transport);
            _net.OnWorldSnapshot += OnSnapshot;
            _net.OnLoginResponse += OnLoginResponse;

            _controls = new PlayerControl();
        }

        private void OnEnable()
        {
            _controls?.Enable();
            _net?.Connect(_address, _port);
        }

        private void OnDisable()
        {
            _net?.Disconnect();
            _controls?.Disable();
        }

        private void Update()
        {
            _net.Poll();

            // Тик клиента: накапливаем реальное время, шлём intent с фиксированной
            // частотой. Это развязывает скорость движения от FPS.
            _tickAccumulator += Time.deltaTime;
            while (_tickAccumulator >= TickInterval)
            {
                _tickAccumulator -= TickInterval;
                Tick();
            }

            // Свой игрок рисуется из предсказанной позиции каждый кадр (без буфера).
            if (_localView != null && _predictor.IsInitialized)
            {
                _localView.SetPredicted(_predictor.X, _predictor.Y, 0, _predictor.Facing, _floorHeight);
            }
        }

        /// <summary>Один клиентский тик: ввод -> intent -> предсказание + отправка.</summary>
        private void Tick()
        {
            Vector2 move = _controls.Player.Move.ReadValue<Vector2>();
            bool sprint = _controls.Player.Sprint.IsPressed();

            IntentDirection dir = ToIntent(move);

            // Шлём intent даже при None? Нет — None не двигает и не нужен серверу.
            if (dir == IntentDirection.None)
                return;

            // Отправляем на сервер (Sequence проставит NetClient) и сразу
            // предсказываем локально с тем же Sequence.
            uint seq = _net.SendMove(dir, sprint);
            if (_predictor.IsInitialized)
                _predictor.ApplyLocal(seq, dir, sprint);
        }

        private static IntentDirection ToIntent(Vector2 move)
        {
            if (move == Vector2.zero) return IntentDirection.None;

            if (Mathf.Abs(move.x) > Mathf.Abs(move.y))
                return move.x > 0 ? IntentDirection.East : IntentDirection.West;
            return move.y > 0 ? IntentDirection.North : IntentDirection.South;
        }

        private void OnLoginResponse(LoginResponse login)
        {
            LocalNetId = login.NetId;
            Debug.Log($"[NetworkRunner] My NetId = {LocalNetId}");
        }

        private void OnSnapshot(WorldSnapshot snap)
        {
            if (snap.Entities == null) return;

            float now = Time.time;
            for (int i = 0; i < snap.Entities.Length; i++)
            {
                var e = snap.Entities[i];

                if (e.NetId == LocalNetId)
                {
                    // Своя сущность: reconciliation, без интерполяционного буфера.
                    _predictor.Reconcile(e.X, e.Y, e.Facing, snap.LastProcessedInput);

                    if (_localView == null)
                    {
                        _localView = Instantiate(_entityViewPrefab, transform);
                        _localView.Init(e.NetId);
                        _views[e.NetId] = _localView;
                    }
                    continue;
                }

                // Чужие: обычная интерполяция.
                if (!_views.TryGetValue(e.NetId, out var view))
                {
                    view = Instantiate(_entityViewPrefab, transform);
                    view.Init(e.NetId);
                    _views.Add(e.NetId, view);
                }
                view.Receive(e, now);
            }
            // TODO (этап 2): удалять вьюхи для пропавших из снапшота сущностей.
        }
    }
}