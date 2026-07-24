using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Shared.Messages.Core;
using Shared.Messages.Player;
using Shared.Messages.Interaction;
using Client.Config;
using Client.Net.View;
using Client.Items;
using Client.Net.Prediction;
using Client.Gameplay.Input;
using Client.Gameplay.Entities;
using Client.Gameplay.Camera;
using Client.UI.Inventory;
using Client.UI.Container;
using Client.UI.Labels;
using Shared.Simulation;
using Shared.World;
using Shared.World.Items;

namespace Client.Net
{
    /// <summary>Точка входа клиента: опрос транспорта, ввод -> intent, предсказание и отрисовка сущностей.</summary>
    public class NetworkRunner : MonoBehaviour
    {
        [SerializeField] private NetEntityView _entityViewPrefab;

        [Header("Предметы на земле (4.4)")]
        [Tooltip("Префаб визуала наземного предмета (world-объект со SpriteRenderer).")]
        [SerializeField] private ItemView _itemViewPrefab;
        [Tooltip("Каталог предметов: спрайт/метаданные по ItemDefId.")]
        [SerializeField] private ItemCatalog _itemCatalog;
        [Tooltip("Реестр слоёв отрисовки: sortingOrder по RenderLayer предмета. Пусто — sortingOrder не трогаем.")]
        [SerializeField] private RenderLayerCatalog _renderLayers;

        [Tooltip("Частота тиков. Дефолт до логина; затем перезаписывается серверным LoginResponse.TickRate.")]
        [SerializeField] private int _tickRate = 30;

        [Header("Карта")]
        [Tooltip("Опц.: камера, которая должна следовать за локальным игроком. Пусто — не трогаем.")]
        [SerializeField] private FollowCamera _camera;

        [Tooltip("Опц.: камера для перевода клика в тайл. Пусто — Camera.main.")]
        [SerializeField] private UnityEngine.Camera _clickCamera;


        [Header("Инвентарь (4.5)")]
        [Tooltip("HUD 6 слотов инвентаря. Пусто — приём InventorySync работает, но не отображается.")]
        [SerializeField] private InventoryHud _inventoryHud;
        [Tooltip("Сервис окон контейнеров")]
        [SerializeField] private ContainerWindows _containerWindows;
        [Tooltip("Пул экранных надписей — локальный хинт «Руки заняты» при попытке подбора без раунд-трипа.")]
        [SerializeField] private LabelManager _labels;
        [Tooltip("Материал обводки предмета под курсором (шейдер Station/SpriteOutline). Пусто — без обводки.")]
        [SerializeField] private Material _itemOutlineMaterial;

        private NetClient _net;
        private PlayerControl _controls;
        private readonly Dictionary<int, NetEntityView> _views = new Dictionary<int, NetEntityView>();
        private readonly Dictionary<int, ItemView> _itemViews = new Dictionary<int, ItemView>(); // наземные предметы (свой реестр, отдельно от player-_views)
        [SerializeField] private string _address = "85.239.58.193";
        private int _port = 7777;

        private readonly PlayerPredictor _predictor = new PlayerPredictor();
        private NetEntityView _localView;

        // LayToggle — one-shot ввод: латчится в Update (1×/кадр), потребляется в Tick (1×/тик).
        private bool _layTogglePending;

        // Backstop despawn: переиспользуемые буферы (без per-tick аллокаций — правило CLAUDE.md).
        private readonly HashSet<int> _seenIds = new HashSet<int>();
        private readonly List<int> _toRemove = new List<int>();

        // Backstop despawn для предметов: свои переиспользуемые буферы (аналогично player-набору).
        private readonly HashSet<int> _seenItemIds = new HashSet<int>();
        private readonly List<int> _itemsToRemove = new List<int>();

        // Коллизия от предметов с HasCollision для предиктора (F1b): полностью пересобирается каждый ItemSnapshot.
        private readonly Shared.Simulation.Blocks.DynamicObstacleSet _itemObstacles = new Shared.Simulation.Blocks.DynamicObstacleSet();

        private int _itemSpawnSeq;
        private int _hoveredItemNetId = -1;
        private Vector2 _lastHoverMouse = new Vector2(float.MinValue, float.MinValue);
        private float _lastHoverPlayerX = float.MinValue;
        private float _lastHoverPlayerY = float.MinValue;
        private readonly List<ItemView> _seqRenormBuffer = new List<ItemView>();

        private byte _activeHand;
        private readonly bool[] _handOccupied = new bool[InventorySlot.HandCount];
        private SlotRecord[] _lastSlots;

        // Хинт «Руки заняты»: ручной тайм-аут (CursorHint по умолчанию — Lifetime=∞, само-возврат не сработает).
        private PooledLabel _handsFullHandle;
        private float _handsFullExpire;
        private const float HandsFullHintSeconds = 1.5f;

        // ServerTick-guard (2.5-B): снапшот на Sequenced-канале → дропаем устаревший/переупорядоченный, чтобы Reconcile
        // не сел на СТАРЫЙ авторитет поверх свежего. _snapshotSeen пропускает ПЕРВЫЙ снапшот при любом стартовом тике.
        private uint _lastServerTick;
        private bool _snapshotSeen;


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
            _net.OnBlockChunkData += OnBlockChunkData;
            _net.OnBlockSectionGone += OnBlockSectionGone;
            _net.OnBlockUpdateBatch += OnBlockUpdateBatch;
            _net.OnPlayerJoined += OnPlayerJoined;
            _net.OnPlayerLeft += OnPlayerLeft;
            _net.OnItemSnapshot += OnItemSnapshot;
            _net.OnInventorySync += OnInventorySync;
            _net.OnContainerSync += OnContainerSync;
            _net.OnPullSync += OnPullSync;
            _net.OnContainSync += OnContainSync;

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

            // Латчим one-shot LayToggle здесь (1×/кадр); тик-луп потребит его ровно один раз.
            if (_controls.Player.ToggleLaying.WasPressedThisFrame())
                _layTogglePending = true;

            // Прыжок — тоже one-shot по НАЖАТИЮ (не по зажатию): латч на кадре, потребление одним тиком.
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
                _jumpPending = true;

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
                _localView.SetPredicted(_predictor.X, _predictor.Y, _predictor.ZF, _predictor.Facing, _predictor.State, _predictor.Reason);
            }

            // Cut-away (D2): гасим слои над головой в кольце вокруг предсказанной позиции. Прыжковый фриз:
            // от отрыва и пока не приземлились/не опустились ниже старта — срез заморожен (слои не дёргаются).
            // Сущности — «купол видимости»: r = базовый радиус − потеря за каждый блок разницы высот
            // (непрерывно, без floor — иначе мигание при прыжках; вниз симметрично). Себя не трогаем.
            if (_blockRenderer != null && _predictor.IsInitialized)
            {
                bool airborne = _predictor.Airborne;
                if (airborne && !_wasAirborne)
                    _airStartY = _predictor.Y;
                _wasAirborne = airborne;

                if (!(airborne && _predictor.Y >= _airStartY - 0.001f))
                    _blockRenderer.UpdateCutaway(_predictor.X, _predictor.Y, _predictor.ZF);
                foreach (var kv in _views)
                {
                    if (kv.Key == LocalNetId) continue;
                    Vector3 p = kv.Value.transform.position;
                    float r = RenderConfig.EntityViewRadius
                              - RenderConfig.EntityViewLossPerBlock * Mathf.Abs(p.y - _predictor.Y);
                    float dx = p.x - _predictor.X, dz = p.z - _predictor.ZF;
                    kv.Value.SetCulled(r <= 0f || dx * dx + dz * dz > r * r);
                }
            }

            if (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
                HandleUseOrContainer();

            // Инвентарь (4.5): Q — выбросить из активной руки; X — сменить руку. Request-only, без предсказания;
            // подсветка идёт по эхо сервера (InventorySync.ActiveHand), не по локальному флипу.
            if (Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame)
                _net.SendDrop(SlotCategory.Hand, _activeHand);
            if (Keyboard.current != null && Keyboard.current.xKey.wasPressedThisFrame)
                _net.SendSwapHand((byte)(_activeHand == 0 ? 1 : 0));
            if (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame)
                HandleEquipToggle();

            HandleInteractionInput();

            // Ручной тайм-аут хинта «Руки заняты» (см. ShowHandsFullHint — CursorHint-стиль по умолчанию не самовозвращается).
            if (_handsFullHandle != null && Time.unscaledTime >= _handsFullExpire)
            {
                _labels?.Dismiss(_handsFullHandle);
                _handsFullHandle = null;
            }

            if (Mouse.current != null && _predictor.IsInitialized)
            {
                Vector2 mp = Mouse.current.position.ReadValue();
                float ppx = _predictor.X, ppy = _predictor.Y;
                // Раскраска обводки — только на реальном изменении: мышь ИЛИ предсказанная позиция игрока (мир под курсором сместился).
                if ((mp - _lastHoverMouse).sqrMagnitude > 0.01f
                    || Mathf.Abs(ppx - _lastHoverPlayerX) > 0.001f
                    || Mathf.Abs(ppy - _lastHoverPlayerY) > 0.001f)
                {
                    _lastHoverMouse = mp;
                    _lastHoverPlayerX = ppx;
                    _lastHoverPlayerY = ppy;
                    int hovered = TryPickItemPixel(mp, out int hid) ? hid : -1;
                    if (hovered != _hoveredItemNetId)
                    {
                        if (_hoveredItemNetId != -1 && _itemViews.TryGetValue(_hoveredItemNetId, out var oldView) && oldView != null)
                            oldView.SetHovered(false);
                        if (hovered != -1 && _itemViews.TryGetValue(hovered, out var newView) && newView != null)
                            newView.SetHovered(true);
                        _hoveredItemNetId = hovered;
                    }
                }
            }

        }

        /// <summary>Один серверный тик: ввод -> intent -> предсказание + отправка.</summary>
        private void Tick()
        {
            Vector2 move = _controls.Player.Move.ReadValue<Vector2>();
            bool sprint = _controls.Player.Sprint.IsPressed();
            IntentDirection dir = ToIntent(move);

            bool layToggle = _layTogglePending;
            _layTogglePending = false;

            // Прыжок (блок-мир): one-shot по нажатию (латч в Update); прыжок в воздухе не буферится — бит уйдёт
            // ближайшим тиком и погаснет о гейт Grounded.
            bool jump = _jumpPending;
            _jumpPending = false;

            if (_containedNetId != 0) return; // C3: в контейнере — не двигаемся, интент/предсказание не шлём

            // Decouple send-vs-step: молчим только в полном покое (Stand + нет ввода/toggle) И на опоре — в воздухе
            // интент обязателен каждый тик (вариант A: серверная гравитация без вводов реконсилилась бы снапом).
            if (dir == IntentDirection.None && !layToggle && !jump && !_predictor.Airborne
                && _predictor.State == (byte)PlayerState.Stand)
                return;

            // Центр «известного» окна стрима — предсказанная позиция (стопор фронтира считается от неё).
            _streamWorld?.SetCenter(_predictor.X, _predictor.Y, _predictor.ZF);

            // Шлём на сервер и сразу предсказываем локально с тем же Sequence.
            uint seq = _net.SendMove(dir, sprint, layToggle, jump);
            if (_predictor.IsInitialized)
                _predictor.ApplyLocal(seq, dir, sprint, layToggle, jump);
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

        /// <summary>Камера обзора для world-anchored надписей (LabelManager): та же ленивая ClickCamera (резолв Camera.main).</summary>
        public UnityEngine.Camera ViewCamera => ClickCamera;

        private void HandleUseOrContainer()
        {
            if (_predictor.IsInitialized && Mouse.current != null
                && TryPickItemPixel(Mouse.current.position.ReadValue(), out int netId)
                && _itemViews.TryGetValue(netId, out var view) && view != null
                && _itemCatalog != null)
            {
                var def = _itemCatalog.For(view.ItemDefId);
                if (def != null && def.IsContainer)
                {
                    // SS14-режим: окно открывает сервер (ContainSync/визуал в мире); иначе — чисто клиентский Toggle-UI.
                    if (def.ContainerMode == Shared.World.Items.ContainerMode.SS14) { _net.SendOpenContainer(netId); return; }
                    if (_containerWindows != null) { _containerWindows.Toggle(netId, view.ItemDefId); return; }
                }
            }
            _net.SendUse();
        }

        /// <summary>Клик мыши → адресная интеракция (в Update, не в Tick): request-only, без предсказания. Экран→ЦЕЛЫЙ
        /// тайл по камере на плоскости этажа игрока (строго PlayerPredictor.Z); опц. пик сущности, иначе цель-тайл.</summary>
        private void HandleInteractionInput()
        {
            if (!_predictor.IsInitialized || Mouse.current == null) return;
            if (UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;

            bool primary = Mouse.current.leftButton.wasPressedThisFrame;
            bool alt = Mouse.current.rightButton.wasPressedThisFrame;
            if (!primary && !alt) return;

            Vector2 screen = Mouse.current.position.ReadValue();
            if (!TryResolveHoverTile(screen, out int tileX, out int tileY, out int pz)) return; // луч мимо плоскости этажа

            // Pickup (4.5): ЛКМ по наземному предмету — ПРЕДМЕТ приоритетнее игрока на тайле. PickupItem не несёт
            // хенда на проводе — сервер кладёт в СВОЙ ActiveHand, поэтому гейтим по занятости именно активной руки
            // (без раунд-трипа); иначе шлём PickupItem вместо обычной интеракции.
            if (primary && TryPickItemPixel(screen, out int itemNetId))
            {
                if (_itemViews.TryGetValue(itemNetId, out var itemView) && itemView != null
                    && _itemCatalog != null && _itemCatalog.For(itemView.ItemDefId)?.Pullable == true)
                {
                    _net.SendPullItem(itemNetId);
                    return;
                }
                if (IsHandOccupied(_activeHand) || _pulledNetId != 0)
                    ShowHandsFullHint(screen);
                else
                    _net.SendPickup(itemNetId);
                return;
            }

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

        // Короткий локальный хинт «Руки заняты» (без раунд-трипа): пул-надпись, ручной тайм-аут в Update
        // (CursorHint-стиль по умолчанию Lifetime=∞ — рассчитан на явный Dismiss, не на само-возврат).
        private void ShowHandsFullHint(Vector2 screenPos)
        {
            if (_labels == null) return;
            if (_handsFullHandle != null) _labels.Dismiss(_handsFullHandle);
            _handsFullHandle = _labels.ShowCursorHint(LabelKind.CursorHint, "Руки заняты", screenPos, follow: false);
            _handsFullExpire = Time.unscaledTime + HandsFullHintSeconds;
        }

        public bool TryResolveHoverTile(Vector2 screenPos, out int cellX, out int cellY, out int cellY2)
        {
            cellX = 0; cellY = 0;
            cellY2 = Mathf.FloorToInt(_predictor.Y);
            var cam = ClickCamera;
            if (cam == null) return false;

            Ray ray = cam.ScreenPointToRay(screenPos);
            var feetPlane = new Plane(Vector3.up, new Vector3(0f, _predictor.Y, 0f));
            if (!feetPlane.Raycast(ray, out float enter)) return false;
            Vector3 hit = ray.GetPoint(enter);
            cellX = Mathf.FloorToInt(hit.x);
            cellY = Mathf.FloorToInt(hit.z);
            return true;
        }

        public bool IsInitialized => _predictor.IsInitialized;

        /// <summary>Дистанция до наземного контейнера в пределах реча (для UI-гейта окна контейнера).</summary>
        public bool IsGroundContainerReachable(int netId)
        {
            if (!_predictor.IsInitialized) return true;
            if (!_itemViews.TryGetValue(netId, out var view) || view == null) return true;

            Vector3 p = view.transform.position;
            return Shared.Simulation.InteractionRules.InReachBlocks(
                Mathf.FloorToInt(_predictor.X), Mathf.FloorToInt(_predictor.ZF), Mathf.FloorToInt(_predictor.Y),
                Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.z), Mathf.FloorToInt(p.y));
        }

        /// <summary>Чужая сущность на тайле (tx,ty) этажа z по её вьюхе; первую подходящую — в pickedId (себя пропускаем).</summary>
        private bool TryPickEntity(int tx, int ty, int z, out int pickedId)
        {
            foreach (var kv in _views)
            {
                if (kv.Key == LocalNetId) continue;
                var view = kv.Value;
                if (view == null) continue;
                Vector3 p = view.transform.position;
                bool match = Mathf.FloorToInt(p.x) == tx && Mathf.FloorToInt(p.z) == ty && Mathf.Abs(Mathf.FloorToInt(p.y) - z) <= 1;
                if (match)
                {
                    pickedId = kv.Key;
                    return true;
                }
            }
            pickedId = -1;
            return false;
        }

        // Пиксель-пик по спрайту (не bbox): среди попаданий берём макс. SortingOrder (топ визуально), при равенстве — ближайший луч.
        private bool TryPickItemPixel(Vector2 screenPos, out int itemNetId)
        {
            itemNetId = -1;
            if (!_predictor.IsInitialized) return false;
            var cam = ClickCamera;
            if (cam == null) return false;
            Ray ray = cam.ScreenPointToRay(screenPos);
            int pz = Mathf.FloorToInt(_predictor.Y);
            int bestOrder = int.MinValue;
            float bestDist = float.MaxValue;
            foreach (var kv in _itemViews)
            {
                var view = kv.Value;
                if (view == null) continue;
                Vector3 p = view.transform.position;
                bool zOk = Mathf.Abs(Mathf.FloorToInt(p.y) - pz) <= 1;
                if (!zOk) continue;
                if (!view.HitTestPixel(ray, out float dist)) continue;
                int order = view.SortingOrder;
                if (order > bestOrder || (order == bestOrder && dist < bestDist))
                {
                    bestOrder = order;
                    bestDist = dist;
                    itemNetId = kv.Key;
                }
            }
            return itemNetId != -1;
        }

        private int NextItemSpawnSeq()
        {
            if (_itemSpawnSeq >= 1000)
            {
                _seqRenormBuffer.Clear();
                foreach (var kv in _itemViews)
                    if (kv.Value != null) _seqRenormBuffer.Add(kv.Value);
                _seqRenormBuffer.Sort((a, b) => a.SortingOrder.CompareTo(b.SortingOrder));
                for (int i = 0; i < _seqRenormBuffer.Count; i++)
                    _seqRenormBuffer[i].SetSpawnSeq(i);
                _itemSpawnSeq = _seqRenormBuffer.Count;
                _seqRenormBuffer.Clear();
            }
            return _itemSpawnSeq++;
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

            // Блок-мир (фаза C): мир пустой, секции стримятся следом (ReliableOrdered — порядок гарантирован);
            // нестримленный объём для предикта — solid-стопор (StreamedBlockWorld, в.20).
            if (_blockGrid == null)
            {
                _blockGrid = new Shared.World.Blocks.BlockGrid();
                _streamWorld = new Client.Map.StreamedBlockWorld(_blockGrid);
                var baseShapes = login.ShapesMode == 1
                    ? (Shared.Simulation.Blocks.IBlockShapes)Shared.Simulation.Blocks.BlockCatalogShapes.Instance
                    : Shared.World.Blocks.DevBlockWorld.Shapes;
                _blockShapes = baseShapes;
                _predictor.SetBlockWorld(_streamWorld, new Client.Map.ShapesWithUnknown(baseShapes));
                _predictor.SetDynamicObstacles(_itemObstacles);
                _blockRenderer = gameObject.AddComponent<Client.Map.BlockRenderer>();
                _blockRenderer.Init(_blockGrid, baseShapes);
                _blockRenderer.SetZoneFade(login.ZoneFadeDistance, login.ZoneFadeVertical);
                var itemGrid = _blockGrid;
                var itemShapes = baseShapes;
                // Один раз на весь клиент: даёт ItemView высоту опоры под предметом — верх САМОЙ высокой не-полноблочной коробки блока (0 — пустой блок/пол).
                Client.Net.View.ItemView.BlockSurface = (x, h, pz) =>
                {
                    var boxes = itemShapes.GetBoxes(itemGrid.GetBlock(x, h, pz), itemGrid.GetState(x, h, pz));
                    float top = 0f;
                    for (int i = 0; i < boxes.Length; i++)
                        if (boxes[i].MaxYf < 0.999f && boxes[i].MaxYf > top)
                            top = boxes[i].MaxYf;
                    return h + top;
                };
            }

            Debug.Log($"[NetworkRunner] My NetId = {LocalNetId}, tickRate = {_tickRate}");
        }


        private Shared.World.Blocks.BlockGrid _blockGrid;
        private Client.Map.StreamedBlockWorld _streamWorld;
        private Shared.Simulation.Blocks.IBlockShapes _blockShapes;
        private Client.Map.BlockRenderer _blockRenderer;
        private bool _jumpPending; // one-shot прыжок (латч в Update, потребление в Tick — как _layTogglePending)
        private bool _wasAirborne; // прыжковый фриз среза: детект отрыва
        private float _airStartY;  // высота в момент отрыва (фриз, пока не опустились ниже)

        private void OnBlockChunkData(BlockChunkData msg)
        {
            if (_blockGrid == null) return;
            _blockGrid.AddSection(msg.Cx, msg.Cy, msg.Cz, msg.Section);
            _streamWorld.MarkReceived(msg.Cx, msg.Cy, msg.Cz);
            _blockRenderer.ApplySection(msg.Cx, msg.Cy, msg.Cz);
            RefreshSectionNeighbors(msg.Cx, msg.Cy, msg.Cz);
        }

        private void OnBlockSectionGone(BlockSectionGone msg)
        {
            if (_blockGrid == null) return;
            _blockGrid.RemoveSection(msg.Cx, msg.Cy, msg.Cz);
            if (msg.Reason == BlockSectionGone.OutOfRange)
                _streamWorld.Forget(msg.Cx, msg.Cy, msg.Cz); // Emptied: «знание» сохраняем (секция = воздух)
            _blockRenderer.ApplySection(msg.Cx, msg.Cy, msg.Cz);
            RefreshSectionNeighbors(msg.Cx, msg.Cy, msg.Cz);
        }

        // Кромка стрима: у секций-соседей автотайл/маски углов/верх-гейты и классификация граней считались,
        // когда этой секции ещё/уже не было (GetBlock=0) — перечитываем. ApplySection отсутствующей = no-op.
        // 8 по плану (диагонали — маски углов) + верх/низ.
        private void RefreshSectionNeighbors(int cx, int cy, int cz)
        {
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                    if (dx != 0 || dz != 0)
                        _blockRenderer.ApplySection(cx + dx, cy, cz + dz);
            _blockRenderer.ApplySection(cx, cy + 1, cz);
            _blockRenderer.ApplySection(cx, cy - 1, cz);
        }

        private void OnBlockUpdateBatch(BlockUpdateBatch msg)
        {
            if (_blockGrid == null || msg.Entries == null) return;
            foreach (var e in msg.Entries)
            {
                ushort oldType = _blockGrid.GetBlock(e.X, e.Y, e.Z);
                byte oldState = _blockGrid.GetState(e.X, e.Y, e.Z);
                _blockGrid.SetBlock(e.X, e.Y, e.Z, e.BlockType);
                if (e.BlockType != 0)
                    _blockGrid.SetState(e.X, e.Y, e.Z, e.State);

                // Тоггл двери (тот же тип, изменился ТОЛЬКО бит Open) → анимация на живом инстансе, без пересборки
                // секции (иначе аниматор сбрасывается в дефолт). Коллизия уже верна — грид обновлён выше.
                bool doorOpenOnly = e.BlockType != 0 && e.BlockType == oldType
                    && (byte)(oldState ^ e.State) == Shared.World.Blocks.BlockState.Open
                    && Shared.World.Blocks.BlockCatalog.Get(e.BlockType).Openable;
                if (doorOpenOnly)
                    _blockRenderer.ApplyDoorState(e.X, e.Y, e.Z, e.BlockType, e.State);
                else
                    _blockRenderer.ApplyBlockChange(e.X, e.Y, e.Z);
            }
        }

        // Вьюха сущности: префаб из сцены; без него (пустая дев-сцена блок-мира) — рантайм-капсула,
        // чтобы дев-режим работал вообще без ручных привязок.
        private NetEntityView CreateEntityView()
        {
            if (_entityViewPrefab != null)
                return Instantiate(_entityViewPrefab, transform);

            var go = new GameObject("EntityView(dev)");
            go.transform.SetParent(transform, false);
            go.AddComponent<SpriteRenderer>(); // NetEntityView ждёт компонент; спрайтов нет — тело рисует капсула
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(go.transform, false);
            body.transform.localScale = new Vector3(0.8f, 0.7f, 0.8f); // примитив высотой 2 → 1.4 (рост игрока)
            body.transform.localPosition = new Vector3(0f, 0.7f, 0f);  // ноги — в позиции вьюхи
            return go.AddComponent<NetEntityView>();
        }

        /// <summary>Явный анонс входа: пред-создаём вьюху чужого (снапшот её потом наполнит). Себя — пропускаем.</summary>
        private void OnPlayerJoined(PlayerJoined msg)
        {
            if (msg.NetId == LocalNetId || _views.ContainsKey(msg.NetId)) return;

            var view = CreateEntityView();
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
                    _predictor.Reconcile(e.X, e.Y, e.Z, e.VY, e.Facing, e.State, e.Reason, e.Speed, snap.LastProcessedInput);

                    if (_localView == null)
                    {
                        _localView = CreateEntityView();
                        _localView.Init(e.NetId);
                        _views[e.NetId] = _localView;
                        if (_camera != null) _camera.SetTarget(_localView.transform);
                        if (_containedNetId != 0) _localView.SetCulled(true);
                    }
                    if (_localView.WornDefId != e.WornUniformDefId)
                        _localView.SetWorn(e.WornUniformDefId, WornSpritesOf(e.WornUniformDefId));
                    continue;
                }

                if (!_views.TryGetValue(e.NetId, out var view))
                {
                    view = CreateEntityView();
                    view.Init(e.NetId);
                    _views.Add(e.NetId, view);
                }
                view.Receive(e, now);
                if (view.WornDefId != e.WornUniformDefId)
                    view.SetWorn(e.WornUniformDefId, WornSpritesOf(e.WornUniformDefId));
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

        // Sequenced = свежий ПОЛНЫЙ набор наземных предметов в PVS: spawn/update по NetId + backstop-despawn
        // вью, чьего id нет в снапшоте (предмет вышел из PVS / подобран). Отдельный реестр — player-путь не трогаем.
        private void OnItemSnapshot(ItemSnapshot snap)
        {
            if (_itemViewPrefab == null || snap.Items == null) return; // префаб не назначен — тихо пропускаем (без NRE)

            _itemObstacles.Clear();
            _seenItemIds.Clear();
            for (int i = 0; i < snap.Items.Length; i++)
            {
                var item = snap.Items[i];
                _seenItemIds.Add(item.NetId);

                if (ItemCatalogData.TryGet(item.ItemDefId, out var proto) && proto.HasCollision)
                {
                    var box = proto.CollisionBox;
                    _itemObstacles.Add(item.X, item.Z + box.MinYf, item.Y, (box.MaxXf - box.MinXf) * 0.5f, box.MaxYf - box.MinYf);
                }

                if (!_itemViews.TryGetValue(item.NetId, out var view))
                {
                    view = Instantiate(_itemViewPrefab, transform);
                    view.Init(item.NetId);
                    view.SetRenderLayers(_renderLayers);
                    view.SetOutlineMaterial(_itemOutlineMaterial);
                    view.SetSpawnSeq(NextItemSpawnSeq());
                    _itemViews.Add(item.NetId, view);
                }
                view.Apply(in item, _itemCatalog);
            }

            // Backstop despawn ПОСЛЕ обхода (без мутации реестра в цикле); буферы переиспользуются.
            _itemsToRemove.Clear();
            foreach (var kv in _itemViews)
                if (!_seenItemIds.Contains(kv.Key))
                    _itemsToRemove.Add(kv.Key);

            for (int i = 0; i < _itemsToRemove.Count; i++)
            {
                if (_itemViews.TryGetValue(_itemsToRemove[i], out var view))
                {
                    Destroy(view.gameObject);
                    _itemViews.Remove(_itemsToRemove[i]);
                }
                if (_itemsToRemove[i] == _hoveredItemNetId) _hoveredItemNetId = -1;
                _containerWindows?.OnItemGone(_itemsToRemove[i]);
            }
        }

        private void OnInventorySync(InventorySync sync)
        {
            _activeHand = sync.ActiveHand;
            _lastSlots = sync.Slots;

            System.Array.Clear(_handOccupied, 0, _handOccupied.Length);
            var slots = sync.Slots;
            if (slots != null)
                for (int i = 0; i < slots.Length; i++)
                    if (slots[i].Category == SlotCategory.Hand && slots[i].Index < _handOccupied.Length)
                        _handOccupied[slots[i].Index] = true;

            if (_inventoryHud != null) _inventoryHud.Apply(in sync);
        }

        private void OnContainerSync(ContainerSync sync)
        {
            _containerWindows?.OnSync(in sync);
        }

        private int _pulledNetId;

        private void OnPullSync(PullSync sync)
        {
            _pulledNetId = sync.PulledNetId;
            if (_inventoryHud == null) return;
            if (sync.PulledNetId != 0) _inventoryHud.SetPullOverlay(sync.ItemDefId);
            else _inventoryHud.ClearPullOverlay();
        }

        private int _containedNetId;

        private void OnContainSync(ContainSync sync)
        {
            _containedNetId = sync.ContainerNetId;
            if (_localView != null) _localView.SetCulled(_containedNetId != 0);
        }

        // Клавиша F: надеть equippable-предмет из активной руки в его worn-категорию (снятие — ЛКМ по worn-слоту, Unequip).
        private void HandleEquipToggle()
        {
            if (_lastSlots == null) return;

            for (int i = 0; i < _lastSlots.Length; i++)
            {
                var rec = _lastSlots[i];
                if (rec.Category != SlotCategory.Hand || rec.Index != _activeHand) continue;
                if (ItemCatalogData.TryGet(rec.ItemDefId, out var proto)
                    && proto.Equippable && IsWornCategory(proto.EquipSlot))
                    _net.SendMoveSlot(SlotCategory.Hand, _activeHand, proto.EquipSlot, 0);
                return;
            }
        }

        /// <summary>Снять предмет из worn-слота в активную руку (сервер откажет, если рука занята).</summary>
        public void Unequip(SlotCategory cat, byte index) => _net.SendMoveSlot(cat, index, SlotCategory.Hand, _activeHand);

        private static bool IsWornCategory(SlotCategory c)
            => c != SlotCategory.None && c != SlotCategory.Hand && c != SlotCategory.Inherit;

        private Sprite[] WornSpritesOf(ushort defId)
        {
            if (defId == 0 || _itemCatalog == null) return null;
            var def = _itemCatalog.For(defId);
            return (def != null && def.WornVisible) ? def.WornSprites : null;
        }

        public byte ActiveHand => _activeHand;

        private bool IsHandOccupied(byte hand) => hand < _handOccupied.Length && _handOccupied[hand];

        /// <summary>Запрос drop-at-feet предмета из (category,index) — для UI/HUD, вне клавиш Q.</summary>
        public void SendDrop(SlotCategory category, byte index) => _net.SendDrop(category, index);

        /// <summary>Запрос положить предмет в контейнер (для ContainerWindows UI).</summary>
        public void SendPutInContainer(int netId) => _net.SendPutInContainer(netId);

        /// <summary>Запрос взять предмет из слота контейнера по индексу.</summary>
        public void SendTakeFromContainer(int netId, ushort index) => _net.SendTakeFromContainer(netId, index);

        /// <summary>Запрос открыть контейнер (SS14-режим).</summary>
        public void SendOpenContainer(int netId) => _net.SendOpenContainer(netId);

        /// <summary>Запрос закрыть окно контейнера.</summary>
        public void SendCloseContainer(int netId) => _net.SendCloseContainer(netId);
    }
}
