using System.Collections.Concurrent;
using LiteNetLib;
using LiteNetLib.Utils;
using Shared.Configs;
using Shared.Messages;
using Shared.Messages.Core;
using Shared.Messages.Interaction;
using Shared.Simulation;
using Shared.World;
using Shared.World.Items;
using Server.Network.Interaction;

namespace Server.Network;

/// <summary>Сетевой сервер: приём подключений, game-loop, симуляция и рассылка снапшотов мира.</summary>
public class GameServer
{
    private readonly SVars _config;
    private readonly GridMap _map;
    private readonly float _spawnX;
    private readonly float _spawnY;
    private readonly int _spawnZ;

    // Открытые двери: ключ (x,y,z) → серверный тик автозакрытия. Обновляются в GameLoop.
    private readonly Dictionary<(int x, int y, int z), uint> _openDoors = new();
    private readonly List<(int x, int y, int z)> _doorsToClose = new();
    private uint DoorOpenTicks => (uint)(_config.TickRate * 5); // автозакрытие ~5 секунд
    private NetManager? _server;
    private readonly Dictionary<NetPeer, ClientConnection> _clients;
    private readonly ConcurrentQueue<Action> _mainThreadActions;
    private bool _isRunning;
    private int _nextConnectionId = 1;
    private uint _currentTick;

    // Общий монотонный аллокатор NetId + реестр всех сущностей мира (игроки; предметы — 4.4). NetId отвязан от
    // ConnectionId. Мутируются только на GameLoop-потоке (OnPeerConnected/Disconnected бегут синхронно в PollEvents).
    private readonly NetIdAllocator _netIdAllocator = new();
    private readonly Dictionary<int, IWorldEntity> _entities = new();

    /// <summary>Число сущностей в общем реестре (диагностика/тесты).</summary>
    public int EntityCount => _entities.Count;

    private readonly List<NetPeer> _connectedPeersCache = new();

    // Адресные интеракции: реестр обработчиков (перебор по порядку, первый взявший цель — стоп). StairHandler —
    // единый источник логики лестниц: и legacy E (TryUseTile), и адресный InteractIntent-клик идут через него.
    private readonly StairHandler _stairHandler = new();
    private readonly IInteractionHandler[] _interactionHandlers;

    // Переиспользуемые буферы горячего broadcast-пути (ноль аллокаций при установившемся числе клиентов).
    private EntitySnapshot[] _broadcastEntities = Array.Empty<EntitySnapshot>();
    private EntitySnapshot[] _perClientEntities = Array.Empty<EntitySnapshot>(); // entity-PVS: отфильтрованный набор recipient'а (ресайз только при росте)
    private readonly MemoryStream _broadcastPayload = new();
    private readonly BinaryWriter _broadcastPayloadWriter;
    private readonly NetDataWriter _broadcastWriter = new();

    // MTU-guard: снапшот на Sequenced (2.5-B) НЕ фрагментируется — при большом видимом наборе payload превысит MTU и
    // начнёт молча теряться. Логируем предупреждение (throttled раз/5с), консервативный порог ~1200 (типичный MTU ~1400+
    // минус заголовки; надёжной публичной константы в LiteNetLib 2.1 нет). Триггер под будущие дельты (план C).
    private const int SnapshotMtuWarnBytes = 1200;
    private uint _lastMtuWarnTick; // 0 = ещё не предупреждали
    private uint _lastItemMtuWarnTick; // 0 = ещё не предупреждали (свой троттл для ItemSnapshot-broadcast)

    // Переиспользуемые буферы ItemSnapshot-broadcast (наземные предметы; параллельно player-буферам, ресайз только при росте).
    private ItemInstance[] _broadcastItems = Array.Empty<ItemInstance>();
    private ItemInstance[] _perClientItems = Array.Empty<ItemInstance>();

    public event Action<ClientConnection>? OnClientConnected;
    public event Action<ClientConnection>? OnClientDisconnected;
    public event Action<ClientConnection, MoveIntent>? OnMoveIntentReceived;

    // Точка спавна игроков (центр проходимой области карты).
    public float SpawnX => _spawnX;
    public float SpawnY => _spawnY;
    public int SpawnZ => _spawnZ;

    /// <summary>TEMP (4.4b): на Start заспавнить тест-предмет у точки спавна — проверить ItemSnapshot в Play. Default OFF
    /// (тесты EntityCount не задевает); включает Program.cs. Убрать при появлении лута/размещения (4.5).</summary>
    public bool DevSpawnTestItems { get; set; }
    private bool _devItemsSpawned;

    /// <summary>Блок-мир (B2): карта v10 по MapPath либо дев-полигон. Клиент получает копию блобом при логине.</summary>
    public Shared.World.Blocks.BlockGrid? BlockWorld { get; private set; }

    /// <summary>Формы блоков движения: каталог (карта из редактора) или DevBlockWorld (полигон).</summary>
    public Shared.Simulation.Blocks.IBlockShapes BlockShapes { get; private set; } = Shared.World.Blocks.DevBlockWorld.Shapes;

    /// <summary>Режим форм для клиента (LoginResponse.ShapesMode: 0=Dev, 1=каталог).</summary>
    public byte BlockShapesMode { get; private set; }

    // Дельты блоков текущего тика (каскад/двери/стройка) + опустевшие секции; рассылка — держателям (в.44/112).
    private readonly List<BlockUpdateBatch.Entry> _blockTickUpdates = new();
    private readonly HashSet<long> _blockEmptiedTick = new();
    private readonly List<BlockUpdateBatch.Entry> _blockPerClient = new();
    private readonly List<long> _blockUnloadBuffer = new();

    // Авто-двери (фаза 2): реестр якорей авто-дверей (DoorOpening.Auto), построен при загрузке мира.
    private readonly Dictionary<long, AutoDoor> _autoDoors = new();

    private sealed class AutoDoor
    {
        public int Ax, Ay, Az;
        public ushort Type;
        public bool Open;
        public uint CloseAtTick; // uint.MaxValue = держать открытой (игрок в зоне)
    }

    public float BlockSpawnX { get; private set; }
    public float BlockSpawnY { get; private set; }
    public float BlockSpawnZ { get; private set; }

    public Shared.World.Blocks.ZoneFloodResult? Zones { get; private set; }

    public GameServer(SVars config, GridMap? map = null)
    {
        _config = config;
        _map = map ?? new GridMap(); // пустая карта = мир без коллизии
        (_spawnX, _spawnY, _spawnZ) = FindSpawn(_map);
        if (config.BlocksWorld)
        {
            (BlockWorld, bool fromFile) = Services.BlockWorldSource.Load(config.MapPath);
            BlockShapes = fromFile ? Shared.Simulation.Blocks.BlockCatalogShapes.Instance
                                   : Shared.World.Blocks.DevBlockWorld.Shapes;
            BlockShapesMode = fromFile ? (byte)1 : (byte)0;

            BlockWorld.BlockChanged += OnBlockWorldChanged; // подписка ПОСЛЕ построения мира — стартовые SetBlock не дельты

            (BlockSpawnX, BlockSpawnY, BlockSpawnZ) = Shared.World.Blocks.BlockWorldSpawn.Find(
                BlockWorld, t => Shared.World.Blocks.BlockCatalog.Get(t).IsMarker,
                Shared.World.Blocks.DevBlockWorld.SpawnX,
                Shared.World.Blocks.DevBlockWorld.SpawnY,
                Shared.World.Blocks.DevBlockWorld.SpawnZ);
            Console.WriteLine($"[Map] BlocksWorld: {BlockWorld.Sections.Count} sections, " +
                              $"spawn ({BlockSpawnX}, y{BlockSpawnY}, {BlockSpawnZ})");
            BuildAutoDoorRegistry();

            Zones = Shared.World.Blocks.ZoneFlood.Recompute(BlockWorld, Shared.World.Blocks.CatalogZoneClassifier.Instance);
            Console.WriteLine($"[Zones] зон: {Zones.Zones.Count}, стыков: {Zones.Junctions.Count}, конфликтов: {Zones.Conflicts.Count}");
            if (config.DebugZones)
            {
                foreach (var zone in Zones.Zones)
                    Console.WriteLine($"[Zones] зона {zone.Id}: «{zone.Name}» этаж {zone.Floor}, ранг {zone.Rank}, сидов {zone.Seeds.Count}");
                foreach (var junction in Zones.Junctions)
                    Console.WriteLine($"[Zones] стык {junction.Cell}: зоны {string.Join(", ", junction.Zones)}");
                foreach (var conflict in Zones.Conflicts)
                    Console.WriteLine($"[Zones] КОНФЛИКТ: зона {conflict.ZoneId} — номера этажей {string.Join(", ", conflict.Floors)}");
            }
        }
        Console.WriteLine($"[Map] Spawn at ({_spawnX}, {_spawnY}, z{_spawnZ})");
        _clients = new Dictionary<NetPeer, ClientConnection>();
        _mainThreadActions = new ConcurrentQueue<Action>();
        _broadcastPayloadWriter = new BinaryWriter(_broadcastPayload);
        _interactionHandlers = new IInteractionHandler[] { _stairHandler };
    }

    public void Start()
    {
        var listener = new EventBasedNetListener();
        listener.ConnectionRequestEvent += OnConnectionRequest;
        listener.PeerConnectedEvent += OnPeerConnected;
        listener.PeerDisconnectedEvent += OnPeerDisconnected;
        listener.NetworkReceiveEvent += OnNetworkReceive;

        _server = new NetManager(listener);
        _server.Start(_config.Port);
        _isRunning = true;

        // TEMP dev-спавн (4.4b, убрать при появлении лута/размещения 4.5): 1 тест-предмет у точки спавна. Через
        // _mainThreadActions → выполнится на GameLoop-потоке (инвариант «_entities мутируется только там»). За флагом (default OFF).
        if (DevSpawnTestItems && !_devItemsSpawned)
        {
            _devItemsSpawned = true;
            _mainThreadActions.Enqueue(() =>
                SpawnGroundItem(1, 1, (int)MathF.Floor(_spawnX), (int)MathF.Floor(_spawnY), _spawnZ));
        }

        Console.WriteLine($"[Server] Started on port {_config.Port}");
        Console.WriteLine($"[Server] Max players: {_config.MaxPlayers}");
        Console.WriteLine($"[Server] Tick rate: {_config.TickRate} TPS");

        Task.Run(GameLoop);
    }

    public void Stop()
    {
        _isRunning = false;
        _server?.Stop();
        Console.WriteLine("[Server] Stopped");
    }

    private void OnConnectionRequest(ConnectionRequest request)
    {
        _connectedPeersCache.Clear();
        _server?.GetConnectedPeers(_connectedPeersCache);

        if (_connectedPeersCache.Count >= _config.MaxPlayers)
        {
            request.Reject();
            Console.WriteLine($"[Server] Rejected: server full ({_connectedPeersCache.Count}/{_config.MaxPlayers})");
            return;
        }

        if (request.AcceptIfKey(_config.ConnectionKey) is null)
        {
            request.Reject();
            Console.WriteLine($"[Server] Rejected: invalid connection key");
            return;
        }

        Console.WriteLine($"[Server] Connection accepted from {request.RemoteEndPoint}");
    }

    private void OnPeerConnected(NetPeer peer)
    {
        var client = new ClientConnection(peer, _nextConnectionId++);
        client.PlayerNetId = _netIdAllocator.Allocate(); // NetId из единого пула (не =ConnectionId)
        _clients[peer] = client;
        _entities[client.PlayerNetId] = client;

        Console.WriteLine($"[Server] Client #{client.ConnectionId} connected from {peer.Address}");

        _mainThreadActions.Enqueue(() => OnClientConnected?.Invoke(client));
    }

    /// <summary>Отключение пира: убрать клиента и поднять OnClientDisconnected в main-потоке.</summary>
    private void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        if (_clients.TryGetValue(peer, out var client))
        {
            // Held-предметы НЕ в _entities (физически вынесены на pickup) → это ЕДИНСТВЕННЫЙ путь их вернуть в мир,
            // иначе утечка NetId. СИНХРОННО, ДО _entities.Remove(client.PlayerNetId) — как в GameLoop, drop-on-floor.
            DropAllHeldOnDisconnect(client);

            _clients.Remove(peer);
            _entities.Remove(client.PlayerNetId);
            Console.WriteLine($"[Server] Client #{client.ConnectionId} disconnected: {disconnectInfo.Reason}");

            _mainThreadActions.Enqueue(() => OnClientDisconnected?.Invoke(client));
        }
    }

    /// <summary>Роняет ВСЕ held-предметы клиента на floor(X/Y,Z) при дисконнекте (SS13 drop-on-floor). Тайл не
    /// проверяется на Walkable — роняем «под ноги» безусловно (иначе орфан NetId при дисконнекте над не-walkable).</summary>
    private void DropAllHeldOnDisconnect(ClientConnection client)
    {
        int cx = (int)MathF.Floor(client.X);
        int cy = (int)MathF.Floor(client.Y);
        int z = client.Z;

        for (int i = 0; i < client.Slots.Length; i++)
        {
            var h = client.Slots[i];
            if (h.NetId == 0) continue;
            SpawnGroundItemWithId(h.NetId, h.ItemDefId, h.StackCount, cx, cy, z);
            client.Slots[i] = default;
        }
    }

    /// <summary>Приём сообщения от клиента: разбор типа и постановка в очередь обработки.</summary>
    private void OnNetworkReceive(NetPeer peer, NetDataReader reader, byte channel, DeliveryMethod method)
    {
        try
        {
            if (!_clients.TryGetValue(peer, out var client))
                return;

            client.LastActivity = DateTime.UtcNow;

            MessageType type = (MessageType)reader.GetUShort();
            byte[] data = reader.GetBytesWithLength();

            switch (type)
            {
                case MessageType.UseIntent:
                    // Действуем по текущему тайлу игрока в game-loop.
                    client.UseRequested = true;
                    break;

                case MessageType.MoveIntent:
                    var intent = new MoveIntent();
                    intent.Deserialize(data);
                    client.IntentQueue.Enqueue(intent);
                    break;

                case MessageType.InteractIntent:
                    try
                    {
                        var interact = new InteractIntent();
                        interact.Deserialize(data);
                        client.InteractQueue.Enqueue(interact);
                    }
                    catch (Exception ex)
                    {
                        // Битый interact-пакет: лог, НЕ кикаем (request-only, не критично для сессии).
                        Console.WriteLine($"[Server] Bad InteractIntent from #{client.ConnectionId}: {ex.Message}");
                    }
                    break;

                case MessageType.PickupItem:
                    try
                    {
                        var pickup = new PickupItem();
                        pickup.Deserialize(data);
                        client.PickupQueue.Enqueue(pickup);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Server] Bad PickupItem from #{client.ConnectionId}: {ex.Message}");
                    }
                    break;

                case MessageType.DropItem:
                    try
                    {
                        var drop = new DropItem();
                        drop.Deserialize(data);
                        client.DropQueue.Enqueue(drop);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Server] Bad DropItem from #{client.ConnectionId}: {ex.Message}");
                    }
                    break;

                case MessageType.SwapHand:
                    try
                    {
                        var swap = new SwapHandRequest();
                        swap.Deserialize(data);
                        client.SwapQueue.Enqueue(swap);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Server] Bad SwapHandRequest from #{client.ConnectionId}: {ex.Message}");
                    }
                    break;

                case MessageType.MoveSlot:
                    try
                    {
                        var moveSlot = new MoveSlotRequest();
                        moveSlot.Deserialize(data);
                        client.MoveSlotQueue.Enqueue(moveSlot);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[Server] Bad MoveSlotRequest from #{client.ConnectionId}: {ex.Message}");
                    }
                    break;

                default:
                    Console.WriteLine($"[Server] Unknown message type from #{client.ConnectionId}: {type}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Server] Error processing message from #{peer.Address}: {ex.Message}");

            // TODO: возможно, стоит дисконнектить клиента при битых данных
        }
    }

    /// <summary>Главный цикл сервера: события, intent'ы, тик, рассылка снапшота с фиксированным шагом.</summary>
    private async Task GameLoop()
    {
        double tickMs = 1000.0 / _config.TickRate;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        double nextTick = sw.Elapsed.TotalMilliseconds;

        while (_isRunning)
        {
            // UnsyncedEvents остаётся false (дефолт) → все колбэки LiteNetLib (OnNetworkReceive/connect/
            // disconnect) бегут СИНХРОННО здесь, на GameLoop-потоке. Поэтому статус-поля (State/Timers/
            // DisableMovement/Facing/_clients/IntentQueue) одно-поточны и безопасны без локов. НЕ включать
            // UnsyncedEvents без явной синхронизации — иначе реальные гонки по этим полям.
            _server?.PollEvents();

            while (_mainThreadActions.TryDequeue(out var action))
            {
                action();
            }

            ProcessIntents();
            ProcessInteractions();
            ProcessPickups();
            ProcessDrops();
            ProcessSlotOps();
            ProcessUses();
            if (!_config.BlocksWorld) ProcessFalls(); // тайл-падения; в блок-мире гравитация внутри Step
            ProcessStatus();
            ProcessCombat();

            _currentTick++;
            if (!_config.BlocksWorld) UpdateDoors(); // тайл-двери
            if (_config.BlocksWorld)
            {
                ProcessBlockStreaming();   // окно секций вокруг игроков (фаза C)
                ProcessBlockDoors();       // авто-двери: тоггл Open → дельты соберутся ниже
                BroadcastBlockUpdates();   // дельты тика — держателям секций (после стрима: канал упорядочит)
            }
            else
            {
                ProcessStreaming(); // тайл-стрим
            }
            BroadcastWorldSnapshot();
            BroadcastItemSnapshot(); // отдельный PVS-поток наземных предметов (после player-снапшота)

            // Привязка к абсолютному времени, чтобы тик не плыл.
            nextTick += tickMs;

            double now = sw.Elapsed.TotalMilliseconds;
            double wait = nextTick - now;

            if (wait > 4)
            {
                // Грубое ожидание (Task.Delay неточен, оставляем запас).
                await Task.Delay((int)(wait - 2));
            }
            else if (wait < -tickMs)
            {
                // Сильно отстали — не нагоняем, стартуем счёт заново.
                nextTick = sw.Elapsed.TotalMilliseconds;
                continue;
            }

            // Точная доводка до nextTick спином.
            while (sw.Elapsed.TotalMilliseconds < nextTick)
            {
                System.Threading.Thread.SpinWait(50);
            }
        }
    }

    /// <summary>Дренирует ВСЮ накопленную очередь intent'ов каждого клиента за тик (catch-up после сетевого
    /// столла: ReliableOrdered выгружает пачку в один PollEvents). Сервер применяет ровно то, что клиент уже
    /// напредсказывал за те тики → позиции сходятся, ack = последний применённый seq → прун _pending корректен.
    /// Пустая очередь → None-тик (Step держит кадэнс). Замена дропу головы, который двигал ack через невыполненные
    /// seq → клиент прунил их как подтверждённые → одноразовый snap-back на восстановлении сети.</summary>
    private void ProcessIntents()
    {
        const int maxPerTick = 32; // предохранитель от флуда; за ним возможен редкий snap (логируем)

        foreach (var client in _clients.Values)
        {
            int applied = 0;
            while (applied < maxPerTick && client.IntentQueue.TryDequeue(out var intent))
            {
                ApplyClientIntent(client, intent, hasIntent: true);
                applied++;
            }

            if (applied == 0)
            {
                ApplyClientIntent(client, default, hasIntent: false); // None-тик: Step None → Stand, без движения
            }
            else if (!client.IntentQueue.IsEmpty)
            {
                int dropped = 0;
                while (client.IntentQueue.TryDequeue(out _)) dropped++;
                Console.WriteLine($"[Server] intent flood #{client.ConnectionId}: applied {applied}/тик, dropped {dropped}");
            }
        }
    }

    /// <summary>Один шаг авторитетной симуляции по intent'у (или None-тик при hasIntent=false). Пайплайн
    /// FsmLogic.Step → гейт MovementAllowed && !DisableMovement → MovementLogic.Apply → ToFacing → двери —
    /// БАЙТ-В-БАЙТ как в клиентском PlayerPredictor (ApplyLocal/Reconcile). Менять — только синхронно с ним.</summary>
    private void ApplyClientIntent(ClientConnection client, MoveIntent intent, bool hasIntent)
    {
        var dir = hasIntent ? intent.Direction : IntentDirection.None;

        // reason — текущая причина лежания: KnockDown() ставит KnockedDown заранее (prev уже Laying), свежий
        // toggle-вход в Laying помечаем Voluntary ниже. Stun/KnockedDown держатся таймерами (ProcessStatus).
        var prev = client.State;
        client.State = FsmLogic.Step(client.State, dir, layToggle: hasIntent && intent.LayToggle,
                                     client.CurrentLayingReason, ref client.Timers);
        if (client.State == PlayerState.Laying && prev != PlayerState.Laying)
            client.CurrentLayingReason = LayingReason.Voluntary;

        // Блок-мир (B2): физика тикает КАЖДЫЙ тик (None-тик = гравитация без ввода — серверный failsafe
        // варианта A). Ветка зеркалит PlayerPredictor.ApplyBlockStep байт-в-байт — менять только синхронно.
        if (_config.BlocksWorld)
        {
            bool canMove = FsmLogic.MovementAllowed(client.State) && !client.DisableMovement;
            var input = new Shared.Simulation.Blocks.BlockMoveInput(
                canMove && hasIntent ? dir : IntentDirection.None,
                sprint: hasIntent && intent.Sprint,
                jump: canMove && hasIntent && intent.Jump,
                crawl: client.State == PlayerState.Laying);
            Shared.Simulation.Blocks.BlockMovementLogic.Step(BlockWorld!, BlockShapes, ref client.Mover, in input);
            // Зеркало в legacy-поля (тайл-раскладка): X=X, Y=глубина плана (Mover.Z), Z=целый блок высоты (Mover.Y).
            client.X = client.Mover.X;
            client.Y = client.Mover.Z;
            client.Z = (int)MathF.Floor(client.Mover.Y);
            if (hasIntent)
            {
                client.LastProcessedSequence = intent.Sequence;
                if (canMove)
                    client.Facing = MovementLogic.ToFacing(intent.Direction, client.Facing);
            }
            return;
        }

        if (!hasIntent) return;

        client.LastProcessedSequence = intent.Sequence; // intent потреблён — для реконсиляции
        if (FsmLogic.MovementAllowed(client.State) && !client.DisableMovement)
        {
            float x = client.X, y = client.Y;
            MovementLogic.Apply(_map, client.Z, ref x, ref y, intent.Direction, intent.Sprint,
                                crawl: client.State == PlayerState.Laying, baseStep: client.Speed.CurrentValue);
            client.X = x;
            client.Y = y;
            client.Facing = MovementLogic.ToFacing(intent.Direction, client.Facing);
            // Бамп: упёрся в закрытую дверь по направлению ввода — открываем её.
            OpenBumpedDoors(client, intent.Direction, intent.Sprint);
        }
    }

    /// <summary>Дренирует очередь адресных интеракций каждого клиента (после ProcessIntents — дальность считается
    /// от свежеприменённой позиции). Каждая: resolve цели → range-check → диспетч через реестр обработчиков.</summary>
    private void ProcessInteractions()
    {
        const int maxPerTick = 32; // предохранитель от флуда кликами (за ним — редкий дроп хвоста, логируем)

        foreach (var client in _clients.Values)
        {
            int applied = 0;
            while (applied < maxPerTick && client.InteractQueue.TryDequeue(out var intent))
            {
                ResolveAndDispatchInteraction(client, in intent);
                applied++;
            }

            if (applied == maxPerTick && !client.InteractQueue.IsEmpty)
            {
                int dropped = 0;
                while (client.InteractQueue.TryDequeue(out _)) dropped++;
                Console.WriteLine($"[Server] interact flood #{client.ConnectionId}: applied {applied}, dropped {dropped}");
            }
        }
    }

    /// <summary>Дренирует очередь pickup-запросов каждого клиента (после ProcessIntents — дальность от свежей позиции).</summary>
    private void ProcessPickups()
    {
        const int maxPerTick = 32;
        foreach (var client in _clients.Values)
        {
            int applied = 0;
            while (applied < maxPerTick && client.PickupQueue.TryDequeue(out var msg))
            {
                HandlePickup(client, in msg);
                applied++;
            }
            if (applied == maxPerTick && !client.PickupQueue.IsEmpty)
            {
                int dropped = 0;
                while (client.PickupQueue.TryDequeue(out _)) dropped++;
                Console.WriteLine($"[Server] pickup flood #{client.ConnectionId}: applied {applied}, dropped {dropped}");
            }
        }
    }

    /// <summary>Дренирует очередь drop-запросов каждого клиента.</summary>
    private void ProcessDrops()
    {
        const int maxPerTick = 32;
        foreach (var client in _clients.Values)
        {
            int applied = 0;
            while (applied < maxPerTick && client.DropQueue.TryDequeue(out var msg))
            {
                HandleDrop(client, in msg);
                applied++;
            }
            if (applied == maxPerTick && !client.DropQueue.IsEmpty)
            {
                int dropped = 0;
                while (client.DropQueue.TryDequeue(out _)) dropped++;
                Console.WriteLine($"[Server] drop flood #{client.ConnectionId}: applied {applied}, dropped {dropped}");
            }
        }
    }

    /// <summary>Дренирует очереди SwapHand/MoveSlot каждого клиента (без range-check — операции над своим инвентарём).</summary>
    private void ProcessSlotOps()
    {
        const int maxPerTick = 32;
        foreach (var client in _clients.Values)
        {
            int applied = 0;
            while (applied < maxPerTick && client.SwapQueue.TryDequeue(out var msg))
            {
                HandleSwapHand(client, in msg);
                applied++;
            }
            if (applied == maxPerTick && !client.SwapQueue.IsEmpty)
            {
                int dropped = 0;
                while (client.SwapQueue.TryDequeue(out _)) dropped++;
                Console.WriteLine($"[Server] swap-hand flood #{client.ConnectionId}: applied {applied}, dropped {dropped}");
            }

            applied = 0;
            while (applied < maxPerTick && client.MoveSlotQueue.TryDequeue(out var msg))
            {
                HandleMoveSlot(client, in msg);
                applied++;
            }
            if (applied == maxPerTick && !client.MoveSlotQueue.IsEmpty)
            {
                int dropped = 0;
                while (client.MoveSlotQueue.TryDequeue(out _)) dropped++;
                Console.WriteLine($"[Server] move-slot flood #{client.ConnectionId}: applied {applied}, dropped {dropped}");
            }
        }
    }

    /// <summary>Whole-stack pickup: Ground→Held в активную руку клиента. Атомарный порядок гонок — см. commit note:
    /// цель найдена и это GroundItemEntity → InReach от ЯЧЕЙКИ ПРЕДМЕТА (анти-телеграб) → активная рука свободна
    /// (return ДО мутации, тихий no-op) → despawn (проигравший гонки за один NetId получает false → return, БЕЗ
    /// утечки) → только теперь в слот, ТЕМ ЖЕ NetId → InventorySync владельцу.</summary>
    private void HandlePickup(ClientConnection client, in PickupItem msg)
    {
        if (!_entities.TryGetValue(msg.TargetNetId, out var e)) return;
        if (e is not GroundItemEntity gi) return;

        int px = (int)MathF.Floor(client.X);
        int py = (int)MathF.Floor(client.Y);
        if (!InteractionRules.InReach(px, py, client.Z, gi.CellX, gi.CellY, gi.Z)) return;

        if (client.Slots[client.ActiveHand].NetId != 0) return; // рука занята — тихий no-op, БЕЗ мутации

        if (!DespawnGroundItem(msg.TargetNetId)) return; // проиграл гонку двух pickup за один NetId в один тик

        client.Slots[client.ActiveHand] = new HeldItem { NetId = gi.NetId, ItemDefId = gi.ItemDefId, StackCount = gi.StackCount };
        SendInventorySyncToOwner(client);
    }

    /// <summary>Drop-at-feet: Held→Ground на СЕРВЕРНОЙ позиции клиента (floor(X/Y), Z) — клиентские координаты
    /// не принимаются (анти-телепорт). Не-Walkable тайл под ногами — отклонить (нет висящих сирот).</summary>
    private void HandleDrop(ClientConnection client, in DropItem msg)
    {
        var h = client.Slots[msg.SlotIndex];
        if (h.NetId == 0) return; // пустой слот — тихо

        int cx = (int)MathF.Floor(client.X);
        int cy = (int)MathF.Floor(client.Y);
        int z = client.Z;
        if (!_map.GetTile(cx, cy, z).Walkable) return;

        SpawnGroundItemWithId(h.NetId, h.ItemDefId, h.StackCount, cx, cy, z); // ТОТ ЖЕ NetId
        client.Slots[msg.SlotIndex] = default;
        SendInventorySyncToOwner(client);
    }

    /// <summary>Смена активной руки — без перемещения предметов, чисто серверный флаг.</summary>
    private void HandleSwapHand(ClientConnection client, in SwapHandRequest msg)
    {
        if (client.ActiveHand == msg.Hand) return; // no-op: без bump version/send (как pickup/drop/move раньше мутации)
        client.ActiveHand = msg.Hand; // Deserialize уже гарантирует < 2
        SendInventorySyncToOwner(client);
    }

    /// <summary>Перемещение предмета между слотами: занятый dest — fail (без авто-swap в MVP). w_class-гейт
    /// (по типу слота) ОТЛОЖЕН — в 4.5a любой предмет идёт в любой слот.</summary>
    private void HandleMoveSlot(ClientConnection client, in MoveSlotRequest msg)
    {
        if (client.Slots[msg.FromSlot].NetId == 0) return; // пустой источник — тихо
        if (client.Slots[msg.ToSlot].NetId != 0) return;    // занят dest — fail, без авто-swap

        client.Slots[msg.ToSlot] = client.Slots[msg.FromSlot];
        client.Slots[msg.FromSlot] = default;
        SendInventorySyncToOwner(client);
    }

    /// <summary>Резолв цели интеракции в ЦЕЛЫЙ тайл, авторитетная проверка дальности и диспетч через реестр обработчиков.
    /// Tile-цель — координаты из intent; Entity-цель — floor СЕРВЕРНОЙ позиции сущности по TargetNetId (клиентские Tile*
    /// игнорируются — анти-чит). Вне дальности / нет сущности / нет обработчика — тихий дроп. public — юнит-тест напрямую.</summary>
    public void ResolveAndDispatchInteraction(ClientConnection client, in InteractIntent intent)
    {
        int tx, ty, tz;
        if (intent.TargetKind == (byte)InteractTargetKind.Entity)
        {
            if (!TryResolveEntityTile(intent.TargetNetId, out tx, out ty, out tz))
                return; // нет такой сущности — тихий дроп
        }
        else
        {
            tx = intent.TileX;
            ty = intent.TileY;
            tz = intent.TileZ;
        }

        int px = (int)MathF.Floor(client.X);
        int py = (int)MathF.Floor(client.Y);
        if (!InteractionRules.InReach(px, py, client.Z, tx, ty, tz))
            return; // вне дальности — тихий дроп (анти-telegrab)

        var ctx = new InteractContext(_map, client, tx, ty, tz, intent.Verb, intent.HandIndex);
        for (int i = 0; i < _interactionHandlers.Length; i++)
        {
            if (_interactionHandlers[i].TryHandle(in ctx))
                return;
        }
        // ни один обработчик не взял цель (нет Special и т.п.) — тихий дроп
    }

    /// <summary>Тайл сущности по её NetId: floor СЕРВЕРНОЙ позиции игрока (в 4.1 сущности = игроки; в 4.3 — общий
    /// _entities). false — сущность не найдена.</summary>
    private bool TryResolveEntityTile(int netId, out int tx, out int ty, out int tz)
    {
        if (_entities.TryGetValue(netId, out var e))
        {
            tx = (int)MathF.Floor(e.X);
            ty = (int)MathF.Floor(e.Y);
            tz = e.Z;
            return true;
        }
        tx = ty = tz = 0;
        return false;
    }

    /// <summary>Тик-таймеры server-only состояний: декремент Stun/KnockedDown, выход в Stand при истечении.
    /// Зовётся раз/тик между ProcessUses и UpdateDoors. Выход из Stun/KnockedDown — только здесь (не предсказывается).</summary>
    private void ProcessStatus()
    {
        foreach (var client in _clients.Values)
        {
            if (client.State == PlayerState.Stun)
            {
                if (--client.Timers.StunTicksRemaining <= 0)
                    client.State = PlayerState.Stand;
            }
            else if (client.State == PlayerState.Laying && client.CurrentLayingReason == LayingReason.KnockedDown)
            {
                if (--client.Timers.KnockdownTicksRemaining <= 0)
                {
                    client.State = PlayerState.Stand;
                    client.CurrentLayingReason = LayingReason.None;
                }
            }
        }
    }

    /// <summary>Hook для combat (будущие этапы): отсюда Kill/SetUnconscious/ApplyStun/KnockDown по урону/health.
    /// Сейчас no-op — combat/урон не реализованы (entry-API остаётся scaffolding, зовётся прямо/в тестах).</summary>
    private void ProcessCombat()
    {
    }

    /// <summary>Точка спавна: маркер Spawn, иначе ближайший к центру проходимый тайл. Нет карты → (0,0,0).</summary>
    private static (float x, float y, int z) FindSpawn(GridMap map)
    {
        if (map.Chunks.Count == 0)
            return (0f, 0f, 0);

        // 1) Явный маркер спавна (приоритет).
        foreach (var chunk in map.Chunks)
        {
            var raw = chunk.Raw;
            for (int i = 0; i < raw.Length; i++)
            {
                if (raw[i].Special != TileSpecial.Spawn) continue;
                int sx = chunk.ChunkX * Chunk.Size + (i % Chunk.Size);
                int sy = chunk.ChunkY * Chunk.Size + (i / Chunk.Size);
                return (sx + 0.5f, sy + 0.5f, chunk.Z);
            }
        }

        // 2) Фолбэк: этаж с наибольшим числом проходимых тайлов.
        var walkablePerZ = new Dictionary<int, int>();
        foreach (var chunk in map.Chunks)
        {
            int count = 0;
            var raw = chunk.Raw;
            for (int i = 0; i < raw.Length; i++)
                if (raw[i].Walkable) count++;
            if (count == 0) continue;
            walkablePerZ.TryGetValue(chunk.Z, out int cur);
            walkablePerZ[chunk.Z] = cur + count;
        }

        if (walkablePerZ.Count == 0)
            return (0f, 0f, 0);

        int spawnZ = 0;
        int best = -1;
        foreach (var kv in walkablePerZ)
            if (kv.Value > best) { best = kv.Value; spawnZ = kv.Key; }

        // Центр проходимой области выбранного этажа.
        long sumX = 0, sumY = 0;
        int total = 0;
        foreach (var chunk in map.Chunks)
        {
            if (chunk.Z != spawnZ) continue;
            for (int ly = 0; ly < Chunk.Size; ly++)
                for (int lx = 0; lx < Chunk.Size; lx++)
                {
                    if (!chunk[lx, ly].Walkable) continue;
                    sumX += chunk.ChunkX * Chunk.Size + lx;
                    sumY += chunk.ChunkY * Chunk.Size + ly;
                    total++;
                }
        }

        int cx = (int)(sumX / total);
        int cy = (int)(sumY / total);

        // Ближайший проходимый тайл к центру.
        int spawnTileX = cx, spawnTileY = cy;
        long bestDist = long.MaxValue;
        foreach (var chunk in map.Chunks)
        {
            if (chunk.Z != spawnZ) continue;
            for (int ly = 0; ly < Chunk.Size; ly++)
                for (int lx = 0; lx < Chunk.Size; lx++)
                {
                    if (!chunk[lx, ly].Walkable) continue;
                    int wx = chunk.ChunkX * Chunk.Size + lx;
                    int wy = chunk.ChunkY * Chunk.Size + ly;
                    long dx = wx - cx, dy = wy - cy;
                    long d = dx * dx + dy * dy;
                    if (d < bestDist) { bestDist = d; spawnTileX = wx; spawnTileY = wy; }
                }
        }

        // Центр тайла (+0.5), чтобы floor(x/y) попадал именно в этот тайл.
        return (spawnTileX + 0.5f, spawnTileY + 0.5f, spawnZ);
    }

    /// <summary>Рассылает снапшот мира каждому клиенту (со своим LastProcessedInput для reconciliation).</summary>
    private void BroadcastWorldSnapshot()
    {
        if (_clients.Count == 0)
            return;

        // Игроков в снапшот берём из общего реестра _entities (предметы 4.4 тоже в нём, но у них СВОЙ поток
        // ItemSnapshot → в player-снапшот НЕ попадают: фильтр is ClientConnection). Буфер вмещает _entities.Count
        // (верхняя граница), фактический count — число игроков. Ресайз — только при росте (без shrink-churn).
        if (_broadcastEntities.Length < _entities.Count)
            _broadcastEntities = new EntitySnapshot[_entities.Count];

        int count = 0;
        foreach (var entity in _entities.Values)
        {
            if (entity is not ClientConnection c) continue;
            _broadcastEntities[count++] = new EntitySnapshot
            {
                NetId = c.PlayerNetId,
                X = c.X,
                Y = _config.BlocksWorld ? c.Mover.Y : c.Y, // блок-мир: Y = непрерывная высота (оси Unity)
                Z = _config.BlocksWorld ? c.Mover.Z : c.Z, // блок-мир: Z = глубина плана
                Facing = c.Facing,
                State = (byte)c.State,
                Reason = (byte)c.CurrentLayingReason,
                Speed = c.Speed.CurrentValue,
                VY = _config.BlocksWorld ? c.Mover.VY : 0f
            };
        }

        // Ёмкость PVS-буфера: максимум — все сущности тика (когда все в интересе). Ресайз только при росте.
        if (_perClientEntities.Length < count)
            _perClientEntities = new EntitySnapshot[count];

        float interestR = SVars.Instance.EntityInterestRadius;
        int interestZ = SVars.Instance.EntityInterestZDepth;

        // Каждому клиенту — его LastProcessedInput + ТОЛЬКО сущности его интереса (entity-PVS): self всегда + spatial/Z.
        foreach (var client in _clients.Values)
        {
            // Фильтр общего набора в переиспользуемый _perClientEntities (zero-alloc: без LINQ/List). self — безусловно.
            int k = 0;
            for (int e = 0; e < count; e++)
            {
                if (_broadcastEntities[e].NetId == client.PlayerNetId
                    || (_config.BlocksWorld
                        ? InInterestBlocks(client.X, client.Y, client.Z, in _broadcastEntities[e], interestR, interestZ)
                        : InInterest(client.X, client.Y, client.Z, in _broadcastEntities[e], interestR, interestZ)))
                    _perClientEntities[k++] = _broadcastEntities[e];
            }

            var snapshot = new WorldSnapshot
            {
                ServerTick = _currentTick,
                LastProcessedInput = client.LastProcessedSequence
                // Entities не задаём: overload WriteTo(writer, entities, count) берёт набор из _perClientEntities.
            };

            // Payload — в переиспользуемый MemoryStream через PVS-overload WriteTo (без per-entity alloc).
            _broadcastPayload.SetLength(0);
            snapshot.WriteTo(_broadcastPayloadWriter, _perClientEntities, k);
            _broadcastPayloadWriter.Flush();

            // MTU-guard: Sequenced не фрагментирует → большой payload молча теряется. Предупреждаем throttled (раз/5с).
            if (_broadcastPayload.Length > SnapshotMtuWarnBytes
                && (_lastMtuWarnTick == 0 || _currentTick - _lastMtuWarnTick >= (uint)_config.TickRate * 5))
            {
                Console.WriteLine($"[Server] WARN: snapshot payload {_broadcastPayload.Length}B > {SnapshotMtuWarnBytes}B " +
                                  $"(client #{client.ConnectionId}, {k} сущностей) — Sequenced не фрагментирует, риск молчаливой потери; нужны дельты (план C)");
                _lastMtuWarnTick = _currentTick;
            }

            // Кадр tag + length-prefixed payload — в переиспользуемый NetDataWriter. КАНАЛ Sequenced (2.5-B): свежайший
            // full-state; потеря само-исцеляется следующим тиком (Reconcile пере-сидит безусловно). Интенты/двери/чанки —
            // на своих reliable-каналах (НЕ трогаем). ТОЛЬКО этот Send — Sequenced.
            _broadcastWriter.Reset();
            _broadcastWriter.Put((ushort)MessageType.WorldSnapshot);
            _broadcastWriter.PutBytesWithLength(_broadcastPayload.GetBuffer(), 0, (ushort)_broadcastPayload.Length);
            client.Peer.Send(_broadcastWriter, DeliveryMethod.Sequenced);
        }
    }

    /// <summary>Спавн наземного предмета: аллокация NetId + регистрация в общем реестре. ТОЛЬКО на GameLoop-потоке. Возвращает NetId.</summary>
    public int SpawnGroundItem(ushort itemDefId, byte stackCount, int cellX, int cellY, int z, byte placement = 0)
    {
        int id = _netIdAllocator.Allocate();
        _entities[id] = new GroundItemEntity(id, itemDefId, stackCount, cellX, cellY, z, placement);
        return id;
    }

    /// <summary>Спавн наземного предмета с ЗАДАННЫМ NetId (переиспользование identity при drop/disconnect) — НЕ
    /// аллоцирует новый id. ТОЛЬКО на GameLoop-потоке.</summary>
    public void SpawnGroundItemWithId(int netId, ushort itemDefId, byte stackCount, int cellX, int cellY, int z, byte placement = 0)
    {
        if (_entities.ContainsKey(netId))
            throw new InvalidOperationException($"NetId {netId} already lives in _entities — double drop/spawn?");
        _entities[netId] = new GroundItemEntity(netId, itemDefId, stackCount, cellX, cellY, z, placement);
    }

    /// <summary>Снять наземный предмет из реестра по NetId. ТОЛЬКО на GameLoop-потоке. false — нет такого предмета
    /// ИЛИ это не GroundItemEntity (игрока не трогаем).</summary>
    public bool DespawnGroundItem(int netId)
    {
        if (_entities.TryGetValue(netId, out var e) && e is GroundItemEntity)
            return _entities.Remove(netId);
        return false;
    }

    /// <summary>Ищет предмет по NetId в любом местоположении: сперва Ground (_entities), затем Held (Slots всех
    /// клиентов). Forward-compat к destroy-by-NetId/Inside (4.5 не эмитит/не принимает Inside). Аллоцирует (не горячий путь).</summary>
    public bool TryFindItemAnyLocation(int netId, out ItemLocationKind location, out ushort itemDefId, out byte stackCount)
    {
        if (_entities.TryGetValue(netId, out var e) && e is GroundItemEntity gi)
        {
            location = ItemLocationKind.Ground;
            itemDefId = gi.ItemDefId;
            stackCount = gi.StackCount;
            return true;
        }

        foreach (var client in _clients.Values)
        {
            for (int i = 0; i < client.Slots.Length; i++)
            {
                if (client.Slots[i].NetId != netId) continue;
                location = ItemLocationKind.Held;
                itemDefId = client.Slots[i].ItemDefId;
                stackCount = client.Slots[i].StackCount;
                return true;
            }
        }

        location = default;
        itemDefId = 0;
        stackCount = 0;
        return false;
    }

    /// <summary>Наземные предметы в зоне интереса позиции (cx,cy,cz) — та же выборка (is GroundItemEntity + InInterest),
    /// что рассылает BroadcastItemSnapshot. Аллоцирует (НЕ горячий путь): для тестов и запросов (range-check подбора 4.4b/4.5).</summary>
    public List<ItemInstance> GroundItemsInInterest(float cx, float cy, int cz)
    {
        float r = SVars.Instance.EntityInterestRadius;
        int zDepth = SVars.Instance.EntityInterestZDepth;
        var result = new List<ItemInstance>();
        foreach (var entity in _entities.Values)
        {
            if (entity is not GroundItemEntity gi) continue;
            var probe = new EntitySnapshot { X = gi.X, Y = gi.Y, Z = gi.Z };
            if (InInterest(cx, cy, cz, in probe, r, zDepth))
                result.Add(ToInstance(gi));
        }
        return result;
    }

    private static ItemInstance ToInstance(GroundItemEntity gi) => new ItemInstance
    {
        NetId = gi.NetId,
        ItemDefId = gi.ItemDefId,
        StackCount = gi.StackCount,
        X = gi.CellX,
        Y = gi.CellY,
        Z = gi.Z,
        Placement = gi.Placement
    };

    /// <summary>Рассылает наземные предметы в интересе каждого клиента ОТДЕЛЬНЫМ ItemSnapshot-потоком (Sequenced), НЕ смешивая
    /// с player-WorldSnapshot. Нет предметов / клиент их не видит — не шлём (без wire-шума). Zero-alloc: переиспользуемые буферы.</summary>
    private void BroadcastItemSnapshot()
    {
        if (_clients.Count == 0)
            return;

        // Все наземные предметы из общего реестра → буфер (вмещает _entities.Count; ресайз только при росте).
        if (_broadcastItems.Length < _entities.Count)
            _broadcastItems = new ItemInstance[_entities.Count];

        int count = 0;
        foreach (var entity in _entities.Values)
            if (entity is GroundItemEntity gi)
                _broadcastItems[count++] = ToInstance(gi);

        if (count == 0)
            return; // нет предметов — не шлём вовсе

        if (_perClientItems.Length < count)
            _perClientItems = new ItemInstance[count];

        float interestR = SVars.Instance.EntityInterestRadius;
        int interestZ = SVars.Instance.EntityInterestZDepth;

        foreach (var client in _clients.Values)
        {
            // PVS: тот же InInterest, что у игроков. Позиция предмета — лёгкий EntitySnapshot-пробник (struct, без heap).
            int k = 0;
            for (int e = 0; e < count; e++)
            {
                var probe = new EntitySnapshot { X = _broadcastItems[e].X, Y = _broadcastItems[e].Y, Z = _broadcastItems[e].Z };
                if (InInterest(client.X, client.Y, client.Z, in probe, interestR, interestZ))
                    _perClientItems[k++] = _broadcastItems[e];
            }

            if (k == 0)
                continue; // клиент не видит ни одного предмета — ему не шлём

            var snapshot = new ItemSnapshot();
            _broadcastPayload.SetLength(0);
            snapshot.WriteTo(_broadcastPayloadWriter, _perClientItems, k);
            _broadcastPayloadWriter.Flush();

            // MTU-guard: тот же порог, свой троттл-тик (Sequenced не фрагментирует → большой payload теряется молча).
            if (_broadcastPayload.Length > SnapshotMtuWarnBytes
                && (_lastItemMtuWarnTick == 0 || _currentTick - _lastItemMtuWarnTick >= (uint)_config.TickRate * 5))
            {
                Console.WriteLine($"[Server] WARN: ItemSnapshot payload {_broadcastPayload.Length}B > {SnapshotMtuWarnBytes}B " +
                                  $"(client #{client.ConnectionId}, {k} предметов) — Sequenced не фрагментирует, риск молчаливой потери");
                _lastItemMtuWarnTick = _currentTick;
            }

            _broadcastWriter.Reset();
            _broadcastWriter.Put((ushort)MessageType.ItemSnapshot);
            _broadcastWriter.PutBytesWithLength(_broadcastPayload.GetBuffer(), 0, (ushort)_broadcastPayload.Length);
            client.Peer.Send(_broadcastWriter, DeliveryMethod.Sequenced);
        }
    }

    /// <summary>Попадает ли сущность e в зону интереса recipient'а в (cx,cy,cz): тот же диапазон этажей (|dz| ≤ zDepth)
    /// И в радиусе (distance² ≤ R²). Self-случай (e.NetId==recipient) проверяет ВЫЗЫВАЮЩИЙ ДО этого — self включён
    /// всегда, безусловно. Z сущности — float (целый этаж), приводим `(int)e.Z` как в клиентском Reconcile. Граница
    /// радиуса включена (≤). public static — чистый предикат, юнит-тестируется напрямую (InternalsVisibleTo не настроен).</summary>
    public static bool InInterest(float cx, float cy, int cz, in EntitySnapshot e, float radius, int zDepth)
    {
        int dz = (int)e.Z - cz;
        if (Math.Abs(dz) > zDepth) return false;
        float ddx = e.X - cx, ddy = e.Y - cy;
        return ddx * ddx + ddy * ddy <= radius * radius;
    }

    /// <summary>Интерес в блок-мире (оси Unity в снапшоте: Y — высота, Z — план). Аргументы клиента —
    /// его legacy-поля (X, Y=глубина плана, Z=целый блок высоты) — зеркалятся из Mover каждый тик.</summary>
    public static bool InInterestBlocks(float cx, float czPlan, int cyBlock, in EntitySnapshot e, float radius, int yDepth)
    {
        int dy = (int)MathF.Floor(e.Y) - cyBlock;
        if (Math.Abs(dy) > yDepth) return false;
        float ddx = e.X - cx, ddz = e.Z - czPlan;
        return ddx * ddx + ddz * ddz <= radius * radius;
    }

    // Дельта тика: итоговое состояние позиции (читаем назад из мира) + учёт опустевших секций.
    // TODO(D-zones 3b): mark zone-dirty → re-flood cascade
    private void OnBlockWorldChanged(int x, int y, int z)
    {
        _blockTickUpdates.Add(new BlockUpdateBatch.Entry
        {
            X = x,
            Y = y,
            Z = z,
            BlockType = BlockWorld!.GetBlock(x, y, z),
            State = BlockWorld.GetState(x, y, z)
        });
        long key = Shared.World.Blocks.BlockGrid.KeyOfBlock(x, y, z);
        if (!BlockWorld.Sections.ContainsKey(key))
            _blockEmptiedTick.Add(key);
    }

    // Уникальный ключ БЛОК-позиции (21 бит/ось). НЕ BlockGrid.KeyOfBlock — та даёт ключ СЕКЦИИ (16³),
    // из-за чего несколько дверей в одной секции затирали друг друга в реестре.
    private static long DoorAnchorKey(int x, int y, int z)
        => ((long)(x & 0x1FFFFF)) | ((long)(y & 0x1FFFFF) << 21) | ((long)(z & 0x1FFFFF) << 42);

    // Разовый скан мира при загрузке: якоря авто-дверей (Openable + DoorOpening.Auto + есть триггер, part 0).
    // Рантайм-добавление/удаление (стройка/деконструкция) — будущий хук, сейчас двери приходят из карты.
    private void BuildAutoDoorRegistry()
    {
        _autoDoors.Clear();
        if (BlockWorld == null)
            return;
        int openable = 0, skipMode = 0, skipNoTrig = 0, skipNotAnchor = 0;
        foreach (var kv in BlockWorld.Sections)
        {
            Shared.World.Blocks.BlockGrid.UnpackKey(kv.Key, out int cx, out int cy, out int cz);
            var section = kv.Value;
            for (int ly = 0; ly < Shared.World.Blocks.ChunkSection.Size; ly++)
                for (int lz = 0; lz < Shared.World.Blocks.ChunkSection.Size; lz++)
                    for (int lx = 0; lx < Shared.World.Blocks.ChunkSection.Size; lx++)
                    {
                        ushort type = section.GetBlock(Shared.World.Blocks.ChunkSection.LocalIndex(lx, ly, lz));
                        if (type == 0)
                            continue;
                        var info = Shared.World.Blocks.BlockCatalog.Get(type);
                        if (!info.Openable)
                            continue;
                        openable++;
                        int wx = cx * 16 + lx, wy = cy * 16 + ly, wz = cz * 16 + lz;
                        byte st = BlockWorld.GetState(wx, wy, wz);
                        if (info.Opening != Shared.World.Blocks.DoorOpening.Auto) { skipMode++; continue; }
                        if (info.Triggers.Length == 0) { skipNoTrig++; continue; }
                        if (Shared.World.Blocks.BlockState.GetPart(st) != 0) { skipNotAnchor++; continue; }

                        bool open = Shared.World.Blocks.BlockState.GetOpen(st);
                        _autoDoors[DoorAnchorKey(wx, wy, wz)] = new AutoDoor
                        {
                            Ax = wx, Ay = wy, Az = wz, Type = type, Open = open,
                            CloseAtTick = open ? uint.MaxValue : 0u
                        };
                        if (_config.DebugAutoDoors)
                        {
                            int facing = Shared.World.Blocks.BlockState.GetFacing(st);
                            Shared.Simulation.Blocks.AutoDoorLogic.TriggerWorldBounds(wx, wy, wz, info.Triggers[0],
                                info.SizeX, info.SizeZ, facing,
                                out float x0, out float y0, out float z0, out float x1, out float y1, out float z1);
                            Console.WriteLine($"[Doors] анкор ({wx},{wy},{wz}) '{info.Name}' facing={facing} " +
                                $"size={info.SizeX}x{info.SizeY}x{info.SizeZ} триггеров={info.Triggers.Length} open={open}; " +
                                $"триггер[0] мир X[{x0:0.##}..{x1:0.##}] Y[{y0:0.##}..{y1:0.##}] Z[{z0:0.##}..{z1:0.##}]");
                        }
                    }
        }
        Console.WriteLine($"[Doors] авто-дверей: {_autoDoors.Count}; openable: {openable} " +
                          $"(пропуск: не-Auto {skipMode}, без триггеров {skipNoTrig}, не-якорь {skipNotAnchor})");
    }

    // Per-tick авторитет авто-дверей: игрок в триггере → Open сразу; вышел → закрыть по DoorCloseDelay.
    private void ProcessBlockDoors()
    {
        if (BlockWorld == null || _autoDoors.Count == 0)
            return;
        float hw = Shared.Simulation.Blocks.BlockMovementConfig.HalfWidth;
        float hh = Shared.Simulation.Blocks.BlockMovementConfig.StandHeight;

        // Троттл-диагностика раз в секунду: позиции игроков (сверить с триггером двери).
        bool dbg = _config.DebugAutoDoors && _currentTick % (uint)Math.Max(1, _config.TickRate) == 0;
        if (dbg)
            foreach (var c in _clients.Values)
                if (c.PlayerNetId != 0)
                    Console.WriteLine($"[Doors] player {c.PlayerNetId} pos ({c.Mover.X:0.##}, {c.Mover.Y:0.##}, {c.Mover.Z:0.##})");

        foreach (var door in _autoDoors.Values)
        {
            var info = Shared.World.Blocks.BlockCatalog.Get(door.Type);
            int facing = Shared.World.Blocks.BlockState.GetFacing(BlockWorld.GetState(door.Ax, door.Ay, door.Az));

            bool occupied = false;
            foreach (var client in _clients.Values)
            {
                if (client.PlayerNetId == 0)
                    continue; // не заспавнен
                if (Shared.Simulation.Blocks.AutoDoorLogic.PlayerInTrigger(
                        client.Mover.X, client.Mover.Y, client.Mover.Z, hw, hh,
                        door.Ax, door.Ay, door.Az, info.Triggers, info.SizeX, info.SizeZ, facing))
                {
                    occupied = true;
                    break;
                }
            }

            if (dbg)
            {
                Shared.Simulation.Blocks.AutoDoorLogic.TriggerWorldBounds(door.Ax, door.Ay, door.Az, info.Triggers[0],
                    info.SizeX, info.SizeZ, facing,
                    out float x0, out float y0, out float z0, out float x1, out float y1, out float z1);
                Console.WriteLine($"[Doors] ({door.Ax},{door.Ay},{door.Az}) facing={facing} occupied={occupied} " +
                    $"open={door.Open}; триггер[0] X[{x0:0.##}..{x1:0.##}] Y[{y0:0.##}..{y1:0.##}] Z[{z0:0.##}..{z1:0.##}]");
            }

            uint closeDelay = (uint)Math.Max(1, (int)(info.CloseDelay * _config.TickRate));
            Shared.Simulation.Blocks.AutoDoorLogic.Tick(occupied, door.Open, door.CloseAtTick, _currentTick,
                closeDelay, out bool newOpen, out uint newCloseAt);
            door.CloseAtTick = newCloseAt;
            if (newOpen != door.Open)
                SetDoorOpen(door, info, facing, newOpen);
        }
    }

    // Атомарный тоггл Open всех частей двери (SetState фиксит дельту тика через OnBlockWorldChanged).
    private void SetDoorOpen(AutoDoor door, Shared.World.Blocks.BlockInfo info, int facing, bool open)
    {
        int parts = Shared.World.Blocks.MultiBlock.PartCount(info.SizeX, info.SizeY, info.SizeZ);
        for (int p = 0; p < parts; p++)
        {
            Shared.World.Blocks.MultiBlock.PartWorldOffset(p, info.SizeX, info.SizeZ, facing,
                out int dx, out int dy, out int dz);
            int px = door.Ax + dx, py = door.Ay + dy, pz = door.Az + dz;
            byte st = BlockWorld!.GetState(px, py, pz);
            BlockWorld.SetState(px, py, pz, Shared.World.Blocks.BlockState.WithOpen(st, open));
        }
        door.Open = open;
    }

    /// <summary>Синхронный первичный стрим окна секций (логин) — до первого шага игрока (стопор фронтира).</summary>
    public void StreamBlockSectionsToClient(ClientConnection client) => StreamBlockWindow(client);

    // Окно секций вокруг каждого игрока: досылка новых + выгрузка по таймауту вне радиуса.
    private void ProcessBlockStreaming()
    {
        int timeoutTicks = Math.Max(1, _config.ChunkUnloadTimeoutSec * _config.TickRate);

        foreach (var client in _clients.Values)
        {
            StreamBlockWindow(client);

            _blockUnloadBuffer.Clear();
            foreach (var kv in client.BlockSectionLastInRange)
                if (_currentTick - kv.Value > timeoutTicks)
                    _blockUnloadBuffer.Add(kv.Key);

            foreach (long key in _blockUnloadBuffer)
            {
                client.SentBlockSections.Remove(key);
                client.BlockSectionLastInRange.Remove(key);
                Shared.World.Blocks.BlockGrid.UnpackKey(key, out int cx, out int cy, out int cz);
                SendToClient(client, new BlockSectionGone
                {
                    Cx = cx, Cy = cy, Cz = cz,
                    Reason = BlockSectionGone.OutOfRange
                });
            }
        }
    }

    private void StreamBlockWindow(ClientConnection client)
    {
        int pcx = FloorDivInt((int)MathF.Floor(client.Mover.X), Shared.World.Blocks.ChunkSection.Size);
        int pcy = FloorDivInt((int)MathF.Floor(client.Mover.Y), Shared.World.Blocks.ChunkSection.Size);
        int pcz = FloorDivInt((int)MathF.Floor(client.Mover.Z), Shared.World.Blocks.ChunkSection.Size);
        const int r = Shared.World.Blocks.BlockStreaming.RadiusSections;
        const int h = Shared.World.Blocks.BlockStreaming.HeightSections;

        for (int cx = pcx - r; cx <= pcx + r; cx++)
            for (int cz = pcz - r; cz <= pcz + r; cz++)
                for (int cy = pcy - h; cy <= pcy + h; cy++)
                {
                    long key = Shared.World.Blocks.BlockGrid.Key(cx, cy, cz);
                    client.BlockSectionLastInRange[key] = (int)_currentTick;

                    var section = BlockWorld!.GetSection(cx, cy, cz);
                    if (section == null || client.SentBlockSections.Contains(key))
                        continue; // пустые не шлём (клиент считает окно воздухом), отправленные не дублируем

                    client.SentBlockSections.Add(key);
                    SendToClient(client, new BlockChunkData { Cx = cx, Cy = cy, Cz = cz, Section = section });
                }
    }

    // Рассылка дельт тика держателям секций; опустевшие секции — явный Emptied (в.21).
    private void BroadcastBlockUpdates()
    {
        if (_blockTickUpdates.Count == 0 && _blockEmptiedTick.Count == 0)
            return;

        foreach (var client in _clients.Values)
        {
            _blockPerClient.Clear();
            for (int i = 0; i < _blockTickUpdates.Count; i++)
            {
                var e = _blockTickUpdates[i];
                if (client.SentBlockSections.Contains(Shared.World.Blocks.BlockGrid.KeyOfBlock(e.X, e.Y, e.Z)))
                    _blockPerClient.Add(e);
            }
            if (_blockPerClient.Count > 0)
                SendToClient(client, new BlockUpdateBatch { Entries = _blockPerClient.ToArray() });

            foreach (long key in _blockEmptiedTick)
            {
                if (!client.SentBlockSections.Contains(key))
                    continue;
                // Секция теперь чистый воздух: контент у клиента очищается, «знание» секции сохраняется.
                Shared.World.Blocks.BlockGrid.UnpackKey(key, out int cx, out int cy, out int cz);
                SendToClient(client, new BlockSectionGone { Cx = cx, Cy = cy, Cz = cz, Reason = BlockSectionGone.Emptied });
            }
        }

        _blockTickUpdates.Clear();
        _blockEmptiedTick.Clear();
    }

    private static int FloorDivInt(int a, int b)
    {
        int q = a / b;
        if ((a % b != 0) && ((a < 0) != (b < 0))) q--;
        return q;
    }

    public void UpdatePlayerPosition(ClientConnection client, float x, float y, int z, byte facing)
    {
        client.X = x;
        client.Y = y;
        client.Z = z;
        client.Facing = facing;
    }

    /// <summary>Отправить сообщение одному клиенту.</summary>
    public void SendToClient<T>(ClientConnection client, T message) where T : struct, INetMessage
    {
        var writer = new NetDataWriter();
        writer.Put((ushort)message.Type);
        writer.PutBytesWithLength(message.Serialize());
        client.Peer.Send(writer, DeliveryMethod.ReliableOrdered);
    }

    /// <summary>Отправить клиенту всю карту (при подключении; позже — стриминг чанков по PVS).</summary>
    public void SendMap(ClientConnection client)
    {
        SendToClient(client, new MapDataMessage { Map = _map });
    }

    /// <summary>Полный слепок инвентаря владельцу (OWNER-ONLY — НИКОГДА не в foreach(_clients)). Бампает
    /// InventoryVersion и шлёт ReliableOrdered через SendToClient.</summary>
    public void SendInventorySyncToOwner(ClientConnection owner)
    {
        int occupied = 0;
        for (int i = 0; i < owner.Slots.Length; i++)
            if (owner.Slots[i].NetId != 0) occupied++;

        var records = new SlotRecord[occupied];
        int k = 0;
        for (byte i = 0; i < owner.Slots.Length; i++)
        {
            var h = owner.Slots[i];
            if (h.NetId == 0) continue;
            records[k++] = new SlotRecord { SlotIndex = i, NetId = h.NetId, ItemDefId = h.ItemDefId, StackCount = h.StackCount };
        }

        owner.InventoryVersion++;
        SendToClient(owner, new InventorySync
        {
            InventoryVersion = owner.InventoryVersion,
            ActiveHand = owner.ActiveHand,
            Slots = records
        });
    }

    /// <summary>Догнать новоприбывшего клиента текущими открытыми дверями (карта статична, двери — рантайм).</summary>
    public void SendOpenDoors(ClientConnection client)
    {
        foreach (var key in _openDoors.Keys)
        {
            var tile = _map.GetTile(key.x, key.y, key.z);
            SendToClient(client, new TileUpdate { X = key.x, Y = key.y, Z = key.z, Tile = tile });
        }
    }

    /// <summary>Открыть закрытые двери, в которые игрок упёрся по направлению ввода (бамп).</summary>
    private void OpenBumpedDoors(ClientConnection client, IntentDirection dir, bool sprint)
    {
        MovementLogic.GetAxes(dir, out int dx, out int dy);
        if (dx == 0 && dy == 0) return;

        // Точка, куда игрок пытался шагнуть, + запас: дверь в AABB тела → открываем.
        float step = MovementLogic.StepPerTick * (sprint ? MovementLogic.SprintMultiplier : 1f) + 0.05f;
        float nx = client.X + dx * step;
        float ny = client.Y + dy * step;
        float r = MovementLogic.CollisionRadius;

        int minX = (int)MathF.Floor(nx - r), maxX = (int)MathF.Floor(nx + r);
        int minY = (int)MathF.Floor(ny - r), maxY = (int)MathF.Floor(ny + r);
        for (int tx = minX; tx <= maxX; tx++)
            for (int ty = minY; ty <= maxY; ty++)
            {
                var t = _map.GetTile(tx, ty, client.Z);
                if (t.Openable && !t.Open)
                    TryOpenDoor(tx, ty, client.Z);
            }
    }

    /// <summary>Открыть дверь и взвести таймер автозакрытия. Открывается вся связная группа
    /// дверных тайлов (2-широкая дверь = обе створки), иначе игрок упрётся в закрытую соседнюю.</summary>
    private void TryOpenDoor(int x, int y, int z)
    {
        if (!_map.GetTile(x, y, z).Openable) return; // не открываемый объект

        foreach (var (gx, gy) in DoorGroup(x, y, z))
        {
            var tile = _map.GetTile(gx, gy, z);
            if (!tile.Open)
            {
                tile.Open = true;
                _map.SetTile(gx, gy, z, in tile);
                BroadcastTileUpdate(gx, gy, z, in tile);
            }
            _openDoors[(gx, gy, z)] = _currentTick + DoorOpenTicks; // (пере)взвести автозакрытие
        }
    }

    /// <summary>Связная группа смежных дверных тайлов (по 4 направлениям) на этаже z.</summary>
    private List<(int x, int y)> DoorGroup(int sx, int sy, int z)
    {
        var group = new List<(int, int)>();
        var seen = new HashSet<(int, int)> { (sx, sy) };
        var stack = new Stack<(int, int)>();
        stack.Push((sx, sy));

        while (stack.Count > 0)
        {
            var (cx, cy) = stack.Pop();
            if (!_map.GetTile(cx, cy, z).Openable) continue;
            group.Add((cx, cy));

            DoorVisit(cx - 1, cy, z, seen, stack);
            DoorVisit(cx + 1, cy, z, seen, stack);
            DoorVisit(cx, cy - 1, z, seen, stack);
            DoorVisit(cx, cy + 1, z, seen, stack);
        }
        return group;
    }

    private void DoorVisit(int x, int y, int z, HashSet<(int, int)> seen, Stack<(int, int)> stack)
    {
        if (seen.Add((x, y)) && _map.GetTile(x, y, z).Openable)
            stack.Push((x, y));
    }

    /// <summary>Тик дверей: закрыть те, у кого истёк таймер и в проёме никого нет.</summary>
    private void UpdateDoors()
    {
        if (_openDoors.Count == 0) return;

        _doorsToClose.Clear();
        foreach (var kv in _openDoors)
            if (_currentTick >= kv.Value) _doorsToClose.Add(kv.Key);

        foreach (var key in _doorsToClose)
        {
            if (IsDoorBlocked(key.x, key.y, key.z))
            {
                _openDoors[key] = _currentTick + DoorOpenTicks; // кто-то в проёме — продлеваем
                continue;
            }

            var tile = _map.GetTile(key.x, key.y, key.z);
            if (tile.Openable && tile.Open)
            {
                tile.Open = false;
                _map.SetTile(key.x, key.y, key.z, in tile);
                BroadcastTileUpdate(key.x, key.y, key.z, in tile);
            }
            _openDoors.Remove(key);
        }
    }

    /// <summary>Стоит ли кто-то телом в проёме двери (нельзя закрывать).</summary>
    private bool IsDoorBlocked(int x, int y, int z)
    {
        float r = MovementLogic.CollisionRadius;
        foreach (var c in _clients.Values)
        {
            if (c.Z != z) continue;
            if (c.X + r > x && c.X - r < x + 1 && c.Y + r > y && c.Y - r < y + 1)
                return true;
        }
        return false;
    }

    private void BroadcastTileUpdate(int x, int y, int z, in Tile tile)
    {
        BroadcastToAll(new TileUpdate { X = x, Y = y, Z = z, Tile = tile });
    }

    /// <summary>Обработать запросы «использовать» (E) от клиентов за этот тик.</summary>
    private void ProcessUses()
    {
        foreach (var client in _clients.Values)
        {
            if (!client.UseRequested) continue;
            client.UseRequested = false;
            TryUseTile(client);
        }
    }

    /// <summary>Использовать тайл под игроком (legacy E): лестница под ногами через общий StairHandler
    /// (тело вынесено — единый источник логики лестниц с адресным InteractIntent).</summary>
    private void TryUseTile(ClientConnection client)
    {
        int px = (int)MathF.Floor(client.X);
        int py = (int)MathF.Floor(client.Y);
        var ctx = new InteractContext(_map, client, px, py, client.Z, (byte)InteractVerb.Primary, handIndex: 0);
        _stairHandler.TryHandle(in ctx);
    }

    /// <summary>Найти клетку высадки лестницы на targetZ: парный тайл (px,py) если walkable, иначе первый walkable-сосед
    /// в порядке N/E/S/W. false — высаживаться некуда (гард «лестница в никуда»). public static — юнит-тестируется напрямую.</summary>
    public static bool TryFindLanding(GridMap map, int px, int py, int targetZ, out int nx, out int ny)
    {
        if (map.GetTile(px, py, targetZ).Walkable) { nx = px; ny = py; return true; }        // парный тайл (как раньше)
        if (map.GetTile(px, py + 1, targetZ).Walkable) { nx = px; ny = py + 1; return true; } // N
        if (map.GetTile(px + 1, py, targetZ).Walkable) { nx = px + 1; ny = py; return true; } // E
        if (map.GetTile(px, py - 1, targetZ).Walkable) { nx = px; ny = py - 1; return true; } // S
        if (map.GetTile(px - 1, py, targetZ).Walkable) { nx = px - 1; ny = py; return true; } // W
        nx = px; ny = py;
        return false;
    }

    /// <summary>Гравитация: падаем на этаж ниже, только если СТРОГО больше 50% футпринта игрока (коллизионный AABB
    /// радиуса CollisionRadius) над IsFall-тайлами — т.е. частичное перекрытие края дыры держит. 1 этаж/тик (плавно;
    /// многоэтажная дыра → несколько тиков). Guard от космоса: падаем ТОЛЬКО если на Z-1 существует чанк (не
    /// GetTile→Space — сам этаж может быть, а тайл Space). Z серверный, не предсказывается. После Uses, до Status.</summary>
    private void ProcessFalls()
    {
        const float r = MovementLogic.CollisionRadius; // «модель» падения = коллизионный футпринт (допущение: тот же R)
        const float total = (2f * r) * (2f * r);       // полная площадь футпринта (2R)²
        const float halfEps = 1e-4f;                   // float-допуск: ровно-50/50 (граница) ДЕРЖИТ, не падает

        foreach (var client in _clients.Values)
        {
            float minX = client.X - r, maxX = client.X + r;
            float minY = client.Y - r, maxY = client.Y + r;

            // Доля футпринта над IsFall-тайлами: суб-тайловое перекрытие по перекрытым тайлам (максимум 2×2).
            float holeArea = 0f;
            int tx0 = (int)MathF.Floor(minX), tx1 = (int)MathF.Floor(maxX);
            int ty0 = (int)MathF.Floor(minY), ty1 = (int)MathF.Floor(maxY);
            for (int tx = tx0; tx <= tx1; tx++)
            {
                float ox = MathF.Max(0f, MathF.Min(maxX, tx + 1) - MathF.Max(minX, tx));
                if (ox <= 0f) continue;
                for (int ty = ty0; ty <= ty1; ty++)
                {
                    if (!_map.GetTile(tx, ty, client.Z).IsFall) continue;
                    float oy = MathF.Max(0f, MathF.Min(maxY, ty + 1) - MathF.Max(minY, ty));
                    holeArea += ox * oy;
                }
            }

            if (holeArea <= 0.5f * total + halfEps) continue; // ≤50% над дырами — край держит (ровно-50/50 держит)

            // Guard от космоса (по центру-тайлу): падаем только если этаж ниже существует.
            int px = (int)MathF.Floor(client.X);
            int py = (int)MathF.Floor(client.Y);
            if (_map.GetChunk(FloorDiv(px, Chunk.Size), FloorDiv(py, Chunk.Size), client.Z - 1) == null)
                continue;

            client.Z--; // 1 этаж за тик
        }
    }

    // Floor-деление тайл→чанк (корректно для отрицательных; GridMap.FloorDiv приватен — реплицируем локально).
    private static int FloorDiv(int a, int b)
    {
        int q = a / b;
        if ((a % b != 0) && ((a < 0) != (b < 0))) q--;
        return q;
    }

    // ── Стриминг карты по чанкам (2.3a) ────────────────────────────────────────────────────────────
    // Троттлинг: перебор in-range не каждый тик. Таймаут выгрузки ≫ интервала, поэтому чанк под ногами
    // (всегда in-range) рефрешится задолго до истечения — не «роняется».
    private const uint StreamIntervalTicks = 15;
    private readonly List<long> _streamUnloadScratch = new(); // буфер unload-ключей (мутировать set во время обхода нельзя)

    /// <summary>Стрим карты (троттлинг): раз в StreamIntervalTicks прогнать per-client проход по всем клиентам.</summary>
    private void ProcessStreaming()
    {
        if (_currentTick % StreamIntervalTicks != 0) return;
        foreach (var client in _clients.Values)
            StreamChunksToClient(client);
    }

    /// <summary>Один стрим-проход для клиента: слать in-range чанки (радиус × Z±depth) вокруг игрока (новые),
    /// рефрешить их таймер, выгружать давно вне радиуса. Данные, не симуляция — Z-авторитет/предикт не задеты.
    /// Зовётся из ProcessStreaming (троттлинг) И СИНХРОННО на логине (начальное окружение ДО первого шага игрока:
    /// незагруженный чанк = Space = проходим → без этого предикт шёл бы сквозь ещё-не-пришедшие стены у фронтира).</summary>
    public void StreamChunksToClient(ClientConnection client)
    {
        int radius = _config.StreamRadiusChunks;
        int depth = _config.StreamZDepth;
        // Секунды→тики, вверх (config сейчас целочислен; Ceiling — на случай дробной настройки в будущем).
        int timeoutTicks = (int)Math.Ceiling(_config.ChunkUnloadTimeoutSec * (double)_config.TickRate);
        int now = (int)_currentTick;

        // Чанк игрока: тайл под ногами = floor(X/Y), затем тайл→чанк (как GridMap.GetTile). Floor корректен и
        // при отрицательных координатах (в отличие от усечения (int)X).
        int pcx = FloorDiv((int)MathF.Floor(client.X), Chunk.Size);
        int pcy = FloorDiv((int)MathF.Floor(client.Y), Chunk.Size);
        int pz = client.Z;

        // In-range существующие чанки: новый — отправить целиком, любой — обновить last-in-range тик.
        for (int dz = -depth; dz <= depth; dz++)
        {
            int z = pz + dz;
            for (int dcx = -radius; dcx <= radius; dcx++)
            {
                int cx = pcx + dcx;
                for (int dcy = -radius; dcy <= radius; dcy++)
                {
                    int cy = pcy + dcy;
                    var chunk = _map.GetChunk(cx, cy, z);
                    if (chunk == null) continue; // пустой чанк не шлём

                    long key = GridMap.Key(cx, cy, z); // единый codec ключа (детерминизм с индексом карты)
                    if (client.SentChunks.Add(key)) // впервые в радиусе → отправить
                        SendToClient(client, new ChunkData { Chunk = chunk });
                    client.ChunkLastInRangeTick[key] = now; // рефреш (и новому, и уже отправленному)
                }
            }
        }

        // Выгрузка: отправленные, но вне радиуса дольше таймаута. Собираем в буфер — set нельзя менять на обходе.
        _streamUnloadScratch.Clear();
        foreach (long key in client.SentChunks)
        {
            if (client.ChunkLastInRangeTick.TryGetValue(key, out int last) && now - last > timeoutTicks)
                _streamUnloadScratch.Add(key);
        }
        foreach (long key in _streamUnloadScratch)
        {
            GridMap.UnpackKey(key, out int cx, out int cy, out int cz);
            SendToClient(client, new ChunkUnload { ChunkX = cx, ChunkY = cy, Z = cz });
            client.SentChunks.Remove(key);
            client.ChunkLastInRangeTick.Remove(key);
        }
    }

    /// <summary>Разослать сообщение всем клиентам (опционально по фильтру).</summary>
    public void BroadcastToAll<T>(T message, Func<ClientConnection, bool>? predicate = null) where T : struct, INetMessage
    {
        var writer = new NetDataWriter();
        writer.Put((ushort)message.Type);
        writer.PutBytesWithLength(message.Serialize());

        foreach (var client in _clients.Values)
        {
            if (predicate == null || predicate(client))
            {
                client.Peer.Send(writer, DeliveryMethod.ReliableOrdered);
            }
        }
    }
}