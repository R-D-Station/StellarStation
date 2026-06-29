using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Shared.Messages.Core;
using Shared.Messages.Player;
using Client.Net.View;
using Client.Net.Prediction;
using Client.Gameplay.Input;
using Client.Gameplay.Entities;
using Client.Gameplay.Camera;
using Shared.World;

namespace Client.Net
{
    /// <summary>Точка входа клиента: опрос транспорта, ввод -> intent, предсказание и отрисовка сущностей.</summary>
    public class NetworkRunner : MonoBehaviour
    {
        [SerializeField] private NetEntityView _entityViewPrefab;

        [Tooltip("Частота тиков — совпадает с серверным TickRate (SVars.TickRate).")]
        [SerializeField] private int _tickRate = 30;

        [Header("Карта (приходит с сервера)")]
        [Tooltip("Рендерер, которому отдать карту, полученную от сервера.")]
        [SerializeField] private View.MapRenderer _mapRenderer;
        [Tooltip("Опц.: камера, которая должна следовать за локальным игроком. Пусто — не трогаем.")]
        [SerializeField] private FollowCamera _camera;
        [Tooltip("Опц.: туман войны (FOV). Пусто — без тумана.")]
        [SerializeField] private View.FovRenderer _fov;

        private NetClient _net;
        private PlayerControl _controls;
        private readonly Dictionary<int, NetEntityView> _views = new Dictionary<int, NetEntityView>();
        private string _address = "127.0.0.1";
        private int _port = 7777;

        private readonly PlayerPredictor _predictor = new PlayerPredictor();
        private NetEntityView _localView;

        // State локального игрока: авторитетный из снапшота (не предсказываем до Этапа 4).
        private byte _localState;

        // Реплика карты: один объект на предиктор и рендер, TileUpdate применяем один раз.
        private GridMap _map;

        private float _tickAccumulator;
        private float TickInterval => 1f / _tickRate;

        /// <summary>NetId этого клиента. -1, пока сервер не прислал LoginResponse.</summary>
        public int LocalNetId { get; private set; } = -1;

        private void Awake()
        {
            ITransport transport = new LiteNetLibTransport();

            _net = new NetClient(transport);
            _net.OnWorldSnapshot += OnSnapshot;
            _net.OnLoginResponse += OnLoginResponse;
            _net.OnMapData += OnMapData;
            _net.OnTileUpdate += OnTileUpdate;

            _controls = new PlayerControl();
        }

        /// <summary>Принять карту с сервера: отдать предиктору (коллизия) и рендереру.</summary>
        private void OnMapData(MapDataMessage msg)
        {
            _map = msg.Map;
            _predictor.SetMap(msg.Map);
            if (_mapRenderer != null) _mapRenderer.SetMap(msg.Map);
            if (_fov != null) _fov.SetMap(msg.Map);
            Debug.Log($"[NetworkRunner] Map received: {msg.Map.Chunks.Count} chunks");
        }

        /// <summary>Рантайм-изменение тайла (дверь): обновить GridMap и перерисовать.</summary>
        private void OnTileUpdate(TileUpdate u)
        {
            _map?.SetTile(u.X, u.Y, u.Z, u.Tile);
            if (_mapRenderer != null) _mapRenderer.RefreshTileAt(u.X, u.Y, u.Z);
            if (_fov != null) _fov.MarkDirty(); // видимость изменилась
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

            // Тик-луп фиксированной частоты: предсказание не зависит от FPS.
            _tickAccumulator += Time.deltaTime;
            while (_tickAccumulator >= TickInterval)
            {
                _tickAccumulator -= TickInterval;
                Tick();
            }

            if (_localView != null && _predictor.IsInitialized)
            {
                _localView.SetPredicted(_predictor.X, _predictor.Y, _predictor.Z, _predictor.Facing, _localState);
            }

            if (_fov != null && _predictor.IsInitialized)
                _fov.UpdateFov(_predictor.X, _predictor.Y, _predictor.Z);

            // Динамический потолочный просвет (R1): радиус кольца у проёма растёт по близости игрока.
            if (_mapRenderer != null && _predictor.IsInitialized)
                _mapRenderer.UpdateCeilingReveal(_predictor.X, _predictor.Y);

            if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
                _net.SendUse();
        }

        /// <summary>Один серверный тик: ввод -> intent -> предсказание + отправка.</summary>
        private void Tick()
        {
            Vector2 move = _controls.Player.Move.ReadValue<Vector2>();
            bool sprint = _controls.Player.Sprint.IsPressed();

            IntentDirection dir = ToIntent(move);

            if (dir == IntentDirection.None)
                return;

            // Шлём на сервер и сразу предсказываем локально с тем же Sequence.
            uint seq = _net.SendMove(dir, sprint);
            if (_predictor.IsInitialized)
                _predictor.ApplyLocal(seq, dir, sprint);
        }

        private static IntentDirection ToIntent(Vector2 move)
        {
            const float dead = 0.5f; // мёртвая зона стика
            int ix = move.x > dead ? 1 : (move.x < -dead ? -1 : 0);
            int iy = move.y > dead ? 1 : (move.y < -dead ? -1 : 0);

            if (ix > 0) return iy > 0 ? IntentDirection.NorthEast
                              : iy < 0 ? IntentDirection.SouthEast
                              : IntentDirection.East;
            if (ix < 0) return iy > 0 ? IntentDirection.NorthWest
                              : iy < 0 ? IntentDirection.SouthWest
                              : IntentDirection.West;
            return iy > 0 ? IntentDirection.North
                 : iy < 0 ? IntentDirection.South
                 : IntentDirection.None;
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
                    // Свой игрок: reconciliation, без интерполяционного буфера.
                    _predictor.Reconcile(e.X, e.Y, (int)e.Z, e.Facing, snap.LastProcessedInput);
                    _localState = e.State; // State авторитетен из снапшота (позиция — из предиктора)
                    if (_mapRenderer != null) _mapRenderer.SetActiveZ(_predictor.Z);

                    if (_localView == null)
                    {
                        _localView = Instantiate(_entityViewPrefab, transform);
                        _localView.Init(e.NetId);
                        _views[e.NetId] = _localView;
                        if (_camera != null) _camera.SetTarget(_localView.transform);
                    }
                    continue;
                }

                if (!_views.TryGetValue(e.NetId, out var view))
                {
                    view = Instantiate(_entityViewPrefab, transform);
                    view.Init(e.NetId);
                    _views.Add(e.NetId, view);
                }
                view.Receive(e, now);
            }
            // TODO (этап 2): удалять views для сущностей, пропавших из снапшота.
        }
    }
}
