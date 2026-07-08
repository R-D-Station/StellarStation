using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Shared.Messages.Core;
using Shared.Messages.Player;
using Shared.Messages.Interaction;
using Client.Config;
using Client.Net.View;
using Client.Net.Prediction;
using Client.Gameplay.Input;
using Client.Gameplay.Entities;
using Client.Gameplay.Camera;
using Client.Gameplay.Interaction;
using Shared.Simulation;
using Shared.World;

namespace Client.Net
{
    /// <summary>Точка входа клиента: опрос транспорта, ввод -> intent, предсказание и отрисовка сущностей.</summary>
    public class NetworkRunner : MonoBehaviour
    {
        [SerializeField] private NetEntityView _entityViewPrefab;

        [Tooltip("Частота тиков. Дефолт до логина; затем перезаписывается серверным LoginResponse.TickRate.")]
        [SerializeField] private int _tickRate = 30;

        [Header("Карта")]
        [Tooltip("Рендерер, которому отдать карту, полученную от сервера.")]
        [SerializeField] private View.MapRenderer _mapRenderer;
        [Tooltip("Опц.: камера, которая должна следовать за локальным игроком. Пусто — не трогаем.")]
        [SerializeField] private FollowCamera _camera;
        [Tooltip("Опц.: туман войны (FOV). Пусто — без тумана.")]
        [SerializeField] private View.FovRenderer _fov;

        [Tooltip("Опц.: камера для перевода клика в тайл. Пусто — Camera.main.")]
        [SerializeField] private UnityEngine.Camera _clickCamera;

        [Tooltip("Опц.: контроллер hover-подсказок (IMGUI). Пусто — без подсказок.")]
        [SerializeField] private HoverHintController _hoverHint;

        private NetClient _net;
        private PlayerControl _controls;
        private readonly Dictionary<int, NetEntityView> _views = new Dictionary<int, NetEntityView>();
        private string _address = "127.0.0.1";
        private int _port = 7777;

        private readonly PlayerPredictor _predictor = new PlayerPredictor();
        private NetEntityView _localView;

        // LayToggle — one-shot ввод: латчится в Update (1×/кадр), потребляется в Tick (1×/тик).
        private bool _layTogglePending;

        // Backstop despawn: переиспользуемые буферы (без per-tick аллокаций — правило CLAUDE.md).
        private readonly HashSet<int> _seenIds = new HashSet<int>();
        private readonly List<int> _toRemove = new List<int>();

        // ServerTick-guard (2.5-B): снапшот на Sequenced-канале → дропаем устаревший/переупорядоченный, чтобы Reconcile
        // не сел на СТАРЫЙ авторитет поверх свежего. _snapshotSeen пропускает ПЕРВЫЙ снапшот при любом стартовом тике.
        private uint _lastServerTick;
        private bool _snapshotSeen;

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
            _net.OnPlayerJoined += OnPlayerJoined;
            _net.OnPlayerLeft += OnPlayerLeft;
            _net.OnChunkData += OnChunkData;
            _net.OnChunkUnload += OnChunkUnload;

            _controls = new PlayerControl();

            // Стрим-режим: сервер шлёт карту по чанкам, не целиком. Заводим ПУСТУЮ карту сразу и отдаём её
            // предиктору/рендереру/FOV — коллизия/рендер/FOV работают до прихода чанков и наполняются ими (держим
            // ОДИН общий экземпляр: OnChunkData мутирует его → все видят). Начальное окружение сервер шлёт на логине.
            _map = new GridMap();
            _predictor.SetMap(_map);
            if (_mapRenderer != null) _mapRenderer.SetMap(_map);
            if (_fov != null) _fov.SetMap(_map);
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

        /// <summary>Приём чанка стрима: положить в общий GridMap (overwrite) + отрисовать + FOV грязный.
        /// _map — общий экземпляр (предиктор/рендерер/FOV держат ссылку) → AddChunk виден всем без пере-SetMap.</summary>
        private void OnChunkData(ChunkData msg)
        {
            _map.AddChunk(msg.Chunk); // overwrite: полный чанк заменит частичный (TileUpdate-дверь мог создать раньше)
            if (_mapRenderer != null)
            {
                // Порядок: новый чанк уже в _map → ApplyChunk (его края резолвятся по загруженным соседям) →
                // пере-резолв краёв соседей с новым чанком (стыки стен/пола «соединяются» СРАЗУ, без рефреша).
                _mapRenderer.ApplyChunk(msg.Chunk);
                _mapRenderer.ReapplyLoadedNeighbors(msg.Chunk.ChunkX, msg.Chunk.ChunkY, msg.Chunk.Z);
                _mapRenderer.MarkRevealDirty(); // набор чанков сменился → reveal-кандидаты пересчитать (раз/кадр)
            }
            if (_fov != null) _fov.MarkDirty();
        }

        /// <summary>Выгрузка чанка стрима (давно вне радиуса): убрать из GridMap + снести его рендер + FOV грязный.</summary>
        private void OnChunkUnload(ChunkUnload msg)
        {
            _map?.RemoveChunk(msg.ChunkX, msg.ChunkY, msg.Z);
            if (_mapRenderer != null)
            {
                _mapRenderer.RemoveChunk(msg.ChunkX, msg.ChunkY, msg.Z);
                // Край соседа, смотревший на удалённый чанк, теперь резолвится «нет соседа» → пере-резолв.
                _mapRenderer.ReapplyLoadedNeighbors(msg.ChunkX, msg.ChunkY, msg.Z);
                _mapRenderer.MarkRevealDirty(); // набор чанков сменился → reveal-кандидаты пересчитать (раз/кадр)
            }
            if (_fov != null) _fov.MarkDirty();
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

            // После дренажа пачки чанков за кадр: пересчитать reveal-кандидаты, если стрим сменил набор (раз/кадр —
            // естественный троттл). НЕ гейтим на IsInitialized: нужен только _map/_activeZ, карта может стримиться раньше.
            if (_mapRenderer != null) _mapRenderer.RefreshRevealIfDirty();

            // Латчим one-shot LayToggle здесь (1×/кадр); тик-луп потребит его ровно один раз.
            if (_controls.Player.ToggleLaying.WasPressedThisFrame())
                _layTogglePending = true;

            // Тик-луп фиксированной частоты: предсказание не зависит от FPS. Стартует только ПОСЛЕ логина —
            // к этому моменту _tickRate выставлен серверным TickRate (LoginResponse), интервал корректен.
            if (LocalNetId >= 0)
            {
                _tickAccumulator += Time.deltaTime;
                while (_tickAccumulator >= TickInterval)
                {
                    _tickAccumulator -= TickInterval;
                    Tick();
                }
            }

            if (_localView != null && _predictor.IsInitialized)
            {
                _localView.SetPredicted(_predictor.X, _predictor.Y, _predictor.Z, _predictor.Facing, _predictor.State, _predictor.Reason);
            }

            if (_fov != null && _predictor.IsInitialized)
                _fov.UpdateFov(_predictor.X, _predictor.Y, _predictor.Z);

            // Динамический потолочный просвет (R1): радиус кольца у проёма растёт по близости игрока.
            if (_mapRenderer != null && _predictor.IsInitialized)
                _mapRenderer.UpdateCeilingReveal(_predictor.X, _predictor.Y);

            if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
                _net.SendUse();

            HandleInteractionInput();

            _hoverHint?.Tick(this);
        }

        /// <summary>Один серверный тик: ввод -> intent -> предсказание + отправка.</summary>
        private void Tick()
        {
            Vector2 move = _controls.Player.Move.ReadValue<Vector2>();
            bool sprint = _controls.Player.Sprint.IsPressed();
            IntentDirection dir = ToIntent(move);

            bool layToggle = _layTogglePending;
            _layTogglePending = false;

            // Decouple send-vs-step: молчим только в полном покое (Stand + нет ввода/toggle). Иначе шлём единый
            // MoveIntent/тик — в т.ч. «стоп»-None, чтобы переход Move/Laying→Stand доехал до сервера.
            if (dir == IntentDirection.None && !layToggle && _predictor.State == (byte)PlayerState.Stand)
                return;

            // Шлём на сервер и сразу предсказываем локально с тем же Sequence.
            uint seq = _net.SendMove(dir, sprint, layToggle);
            if (_predictor.IsInitialized)
                _predictor.ApplyLocal(seq, dir, sprint, layToggle);
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

        // Камера для перевода клика в тайл; кэшируется на первом клике (Camera.main = tagged Find — только при нужде).
        private UnityEngine.Camera ClickCamera
        {
            get
            {
                if (_clickCamera == null) _clickCamera = UnityEngine.Camera.main;
                return _clickCamera;
            }
        }

        /// <summary>Клик мыши → адресная интеракция (в Update, не в Tick): request-only, без предсказания. Экран→ЦЕЛЫЙ
        /// тайл по камере на плоскости этажа игрока (строго PlayerPredictor.Z); опц. пик сущности, иначе цель-тайл.</summary>
        private void HandleInteractionInput()
        {
            if (!_predictor.IsInitialized || Mouse.current == null) return;

            bool primary = Mouse.current.leftButton.wasPressedThisFrame;
            bool alt = Mouse.current.rightButton.wasPressedThisFrame;
            if (!primary && !alt) return;

            Vector2 screen = Mouse.current.position.ReadValue();
            if (!TryResolveHoverTile(screen, out int tileX, out int tileY, out int pz)) return; // луч мимо плоскости этажа

            byte verb = (byte)(alt && !primary ? InteractVerb.Alt : InteractVerb.Primary);

            // Опц. пик чужой сущности на том же тайле этажа игрока; иначе — цель-тайл (TargetNetId=-1).
            byte kind = (byte)InteractTargetKind.Tile;
            int targetNetId = -1;
            if (TryPickEntity(tileX, tileY, pz, out int pickedId))
            {
                kind = (byte)InteractTargetKind.Entity;
                targetNetId = pickedId;
            }

            _net.SendInteract(kind, verb, 0, tileX, tileY, pz, targetNetId);
        }

        /// <summary>Экран→ЦЕЛЫЙ тайл по камере на плоскости ЭТАЖА игрока (строго PlayerPredictor.Z). ЕДИНЫЙ impl клика и
        /// hover — чтобы они не расходились. tileZ = этаж игрока всегда; false — луч мимо плоскости (нет цели).</summary>
        public bool TryResolveHoverTile(Vector2 screenPos, out int tileX, out int tileY, out int tileZ)
        {
            tileX = 0; tileY = 0;
            tileZ = _predictor.Z;
            var cam = ClickCamera;
            if (cam == null) return false;

            // Плоскость этажа игрока (Unity-Y = z*FloorHeight): тайл на СВОЁМ Z (2.5D — курсор может визуально попасть на др. этаж).
            Ray ray = cam.ScreenPointToRay(screenPos);
            var floorPlane = new Plane(Vector3.up, new Vector3(0f, tileZ * RenderConfig.FloorHeight, 0f));
            if (!floorPlane.Raycast(ray, out float enter)) return false;

            Vector3 hit = ray.GetPoint(enter);
            tileX = Mathf.FloorToInt(hit.x); // Unity-X = серверный X
            tileY = Mathf.FloorToInt(hit.z); // Unity-Z (глубина) = серверный Y
            return true;
        }

        // Read-only поверхность для hover-hint: тайл игрока (floor(_predictor.X/Y), Z предиктора — reach-паритет в ОДНОМ месте),
        // инициализация и доступ к тайлу карты (инкапсуляция _map).
        public int PlayerTileX => Mathf.FloorToInt(_predictor.X);
        public int PlayerTileY => Mathf.FloorToInt(_predictor.Y);
        public int PlayerTileZ => _predictor.Z;
        public bool IsInitialized => _predictor.IsInitialized;
        public Tile GetTileAt(int x, int y, int z) => _map.GetTile(x, y, z);

        /// <summary>Чужая сущность на тайле (tx,ty) этажа z по её вьюхе; первую подходящую — в pickedId (себя пропускаем).</summary>
        private bool TryPickEntity(int tx, int ty, int z, out int pickedId)
        {
            foreach (var kv in _views)
            {
                if (kv.Key == LocalNetId) continue;
                var view = kv.Value;
                if (view == null) continue;
                Vector3 p = view.transform.position;
                if (Mathf.FloorToInt(p.x) == tx
                    && Mathf.FloorToInt(p.z) == ty
                    && Mathf.RoundToInt(p.y / RenderConfig.FloorHeight) == z)
                {
                    pickedId = kv.Key;
                    return true;
                }
            }
            pickedId = -1;
            return false;
        }

        private void OnLoginResponse(LoginResponse login)
        {
            // Клиент тикает на СЕРВЕРНОМ rate (enforcement tickRate==TickRate по построению). Тик-луп гейтится
            // LocalNetId>=0 в Update → стартует отсюда с корректным интервалом; сбрасываем накопленный за хендшейк хвост.
            // Колбэк бежит на main-потоке (из _net.Poll() в Update), там же читается _tickRate (TickInterval) —
            // гонок нет, пока клиентский транспорт не включает UnsyncedEvents.
            _tickRate = Mathf.Max(1, login.TickRate);
            _tickAccumulator = 0f;
            LocalNetId = login.NetId;
            Debug.Log($"[NetworkRunner] My NetId = {LocalNetId}, tickRate = {_tickRate}");
        }

        /// <summary>Явный анонс входа: пред-создаём вьюху чужого (снапшот её потом наполнит). Себя — пропускаем.</summary>
        private void OnPlayerJoined(PlayerJoined msg)
        {
            if (msg.NetId == LocalNetId || _views.ContainsKey(msg.NetId)) return;

            var view = Instantiate(_entityViewPrefab, transform);
            view.Init(msg.NetId);
            _views.Add(msg.NetId, view);
        }

        /// <summary>Явный анонс выхода: снимаем вьюху чужого. Локального игрока не трогаем.</summary>
        private void OnPlayerLeft(PlayerLeft msg)
        {
            if (msg.NetId == LocalNetId) return;
            if (_views.TryGetValue(msg.NetId, out var view))
            {
                Destroy(view.gameObject);
                _views.Remove(msg.NetId);
            }
        }

        private void OnSnapshot(WorldSnapshot snap)
        {
            if (snap.Entities == null) return;

            // Дропаем устаревший/переупорядоченный снапшот (Sequenced обычно уже отсеивает, но страхуемся независимо от
            // семантики этой сборки LiteNetLib) — иначе Reconcile сядет на СТАРЫЙ авторитет поверх свежего. ack
            // (LastProcessedInput) монотонен → prune-до-последнего от свежайшего снапшота корректен.
            if (_snapshotSeen && snap.ServerTick <= _lastServerTick) return;
            _snapshotSeen = true;
            _lastServerTick = snap.ServerTick;

            _seenIds.Clear();
            float now = Time.time;
            for (int i = 0; i < snap.Entities.Length; i++)
            {
                var e = snap.Entities[i];
                _seenIds.Add(e.NetId);

                if (e.NetId == LocalNetId)
                {
                    // Свой игрок: reconciliation, без интерполяционного буфера. State сидируется в предиктор
                    // (seed для running-state нити), а вьюха берёт ПРЕДСКАЗАННЫЙ _predictor.State.
                    _predictor.Reconcile(e.X, e.Y, (int)e.Z, e.Facing, e.State, e.Reason, e.Speed, snap.LastProcessedInput);
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

            // Backstop despawn: чужая сущность пропала из снапшота → снять её вьюху. «Нет в снапшоте» теперь означает
            // PlayerLeft (потерян/опоздал) ИЛИ выход из зоны интереса recipient'а (entity-PVS 2.5-D: сервер шлёт только
            // близких на своём этаже). Оба случая = despawn корректен; при возврате в интерес вьюха re-spawn'ится из
            // снапшота. Удаление ПОСЛЕ обхода snap.Entities (без мутации _views в обходе); буферы переиспользуются.
            _toRemove.Clear();
            foreach (var kv in _views)
                if (kv.Key != LocalNetId && !_seenIds.Contains(kv.Key))
                    _toRemove.Add(kv.Key);

            for (int i = 0; i < _toRemove.Count; i++)
            {
                if (_views.TryGetValue(_toRemove[i], out var view))
                {
                    Destroy(view.gameObject);
                    _views.Remove(_toRemove[i]);
                }
            }
        }
    }
}
