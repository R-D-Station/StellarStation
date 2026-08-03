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
using Server.Network.Messages;
using Server.Items;

namespace Server.Network;

/// <summary>Сетевой сервер-оркестратор: подключения, game-loop, рассылка снапшотов; предметная логика делегирована подсистемам (_groundItems/_inventory/_containers/_pull).</summary>
public class GameServer
{
    private readonly SVars _config;
    private NetManager? _server;

    public int BoundPort => _server?.LocalPort ?? 0;
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

    // Адресные интеракции: реестр обработчиков (перебор по порядку, первый взявший цель — стоп).
    private readonly IInteractionHandler[] _interactionHandlers;
    private readonly MessageRouter _router = MessageRouter.CreateDefault(); // диспетч входящих по MessageType — см. [[MessageRouter]]
    private readonly GroundItemWorld _groundItems; // наземные предметы: спавн/деспавн/PVS — см. [[ServerItems]]
    private readonly ServerInventorySystem _inventory; // руки/подбор/дроп/слоты/экип — см. [[ServerItems]]
    private readonly ServerContainerSystem _containers; // контейнеры (открыт/закрыт, содержимое) — см. [[Containers]]
    private readonly ServerPullSystem _pull; // тяга сущностей/предметов — см. [[HeavyPull]]

    // Переиспользуемые буферы горячего broadcast-пути (ноль аллокаций при установившемся числе клиентов).
    private EntitySnapshot[] _broadcastEntities = Array.Empty<EntitySnapshot>();
    private bool[] _broadcastContained = Array.Empty<bool>();
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

    private readonly global::Server.Doors.DoorSystem _doors;

    /// <summary>Команды дверей для обработчиков интеракций (реестр + авторитет открытия живут в DoorSystem).</summary>
    public global::Server.Doors.IDoorCommands Doors => _doors;

    public float BlockSpawnX { get; private set; }
    public float BlockSpawnY { get; private set; }
    public float BlockSpawnZ { get; private set; }

    /// <summary>Таблица зон последнего флудфилла (in-memory, не сериализуется — ZoneId в блоках уезжает клиенту существующим v12).</summary>
    public Shared.World.Blocks.ZoneFloodResult? Zones { get; private set; }

    public Shared.World.Atmos.AtmosGrid Atmos { get; private set; } = new Shared.World.Atmos.AtmosGrid();
    private readonly Shared.World.Atmos.AtmosFlow _atmosFlow = new Shared.World.Atmos.AtmosFlow();
    private readonly global::Server.Atmos.AtmosExposure _atmosExposure;

    /// <summary>Лифты (L1: наполняется тестами/вручную; скан карты — L3). Боксы кабин доливаются в набор препятствий каждый тик.</summary>
    public List<global::Server.Lifts.LiftRuntime> Lifts { get; } = new();
    private readonly global::Server.Lifts.LiftSystem _lifts;
    private readonly global::Server.Lifts.LiftFloorSystem _liftFloors;

    /// <summary>Результат скана шахт (шахты + остановки + диагностика).</summary>
    public global::Server.Lifts.LiftScanResult LiftScan => _lifts.Result;

    /// <summary>Система лифтов: контроллеры шахт, вызовы этажей, командование шахтными дверьми.</summary>
    public global::Server.Lifts.LiftSystem LiftSystem => _lifts;

    public GameServer(SVars config, IInteractionHandler[]? interactionHandlers = null)
    {
        _config = config;
        _clients = new Dictionary<NetPeer, ClientConnection>();
        _mainThreadActions = new ConcurrentQueue<Action>();
        _broadcastPayloadWriter = new BinaryWriter(_broadcastPayload);
        // Composition root предметных систем: общие _entities/_clients, GameServer как фасад (this).
        _groundItems = new GroundItemWorld(_entities, _netIdAllocator, _clients);
        _inventory = new ServerInventorySystem(this, _groundItems, _clients, _entities);
        _containers = new ServerContainerSystem(this, _inventory, _entities, _clients);
        _pull = new ServerPullSystem(this, _inventory, _groundItems, _entities, _clients);
        (BlockWorld, bool fromFile) = Services.BlockWorldSource.Load(config.MapPath);
        BlockShapes = fromFile ? Shared.Simulation.Blocks.BlockCatalogShapes.Instance
                               : Shared.World.Blocks.DevBlockWorld.Shapes;
        BlockShapesMode = fromFile ? (byte)1 : (byte)0;

        int attachRemoved = Shared.World.Blocks.BlockAttach.ValidateAll(BlockWorld, Shared.World.Blocks.BlockAttach.DefaultIsSolid);
        Console.WriteLine($"[Attach] удалено неопёртых: {attachRemoved}");

        BlockWorld.BlockChanged += OnBlockWorldChanged; // подписка ПОСЛЕ построения мира — стартовые SetBlock не дельты

        (BlockSpawnX, BlockSpawnY, BlockSpawnZ) = Shared.World.Blocks.BlockWorldSpawn.Find(
            BlockWorld, t => Shared.World.Blocks.BlockCatalog.Get(t).IsSpawn,
            Shared.World.Blocks.DevBlockWorld.SpawnX,
            Shared.World.Blocks.DevBlockWorld.SpawnY,
            Shared.World.Blocks.DevBlockWorld.SpawnZ);
        Console.WriteLine($"[Map] Blocks: {BlockWorld.Sections.Count} sections, " +
                          $"spawn ({BlockSpawnX}, y{BlockSpawnY}, {BlockSpawnZ})");

        // Диагностика слоя структур: Shared молчит и копит данные, печатает потребитель (см. ZoneFloodResult.Leaks).
        var mig = Shared.World.Blocks.BlockMapSerializer.LastMigrationStats;
        if (mig.Moved > 0 || mig.Skipped > 0)
            Console.WriteLine($"[Struct] миграция карты: перенесено частей {mig.Moved}, пропущено {mig.Skipped} (неизвестный/одиночный тип)");
        if (BlockWorld.OrphanStructReads > 0 || Shared.Simulation.Blocks.BlockCatalogShapes.BadPartReads > 0)
            Console.WriteLine($"[Struct] ВНИМАНИЕ: орфан-записей {BlockWorld.OrphanStructReads}, " +
                              $"частей вне диапазона {Shared.Simulation.Blocks.BlockCatalogShapes.BadPartReads}");

        _doors = new global::Server.Doors.DoorSystem(BlockWorld, Atmos, _config, _clients);
        BuildAutoDoorRegistry();

        // ПОРЯДОК КРИТИЧЕН: строго ПОСЛЕ реестра дверей (иначе шахтной дверью нечем командовать) и строго
        // ДО AtmosInit.Classify (иначе сохранённая открытой шахтная дверь разметит помещение за ней как Space).
        _lifts = new global::Server.Lifts.LiftSystem(BlockWorld, _doors, _config);
        _lifts.Build();
        _lifts.PrintReport();
        Lifts.AddRange(_lifts.Runtimes);
        _liftFloors = new global::Server.Lifts.LiftFloorSystem(_lifts, _clients);

        // Пересчёт ПОСЛЕ авто-дверей: флуд классифицирует Openable-блоки как ворота вне зависимости от их состояния.
        Zones = Shared.World.Blocks.ZoneFlood.Recompute(BlockWorld, Shared.World.Blocks.CatalogZoneClassifier.Instance);
        Console.WriteLine($"[Zones] зон: {Zones.Zones.Count}, стыков: {Zones.Junctions.Count}, конфликтов: {Zones.Conflicts.Count}, утечек наружу: {Zones.Leaks.Count}");
        foreach (var leak in Zones.Leaks)
            Console.WriteLine($"[Zones] УТЕЧКА: зона {leak.ZoneId} открыта в космос у {leak.Cell}");
        if (config.DebugZones)
        {
            foreach (var zone in Zones.Zones)
                Console.WriteLine($"[Zones] зона {zone.Id}: «{zone.Name}» этаж {zone.Floor}, ранг {zone.Rank}, сидов {zone.Seeds.Count}");
            foreach (var junction in Zones.Junctions)
                Console.WriteLine($"[Zones] стык {junction.Cell}: зоны {string.Join(", ", junction.Zones)}");
            foreach (var conflict in Zones.Conflicts)
                Console.WriteLine($"[Zones] КОНФЛИКТ: зона {conflict.ZoneId} — номера этажей {string.Join(", ", conflict.Floors)}");
        }

        _groundItems.SpawnMapItems(BlockWorld, BlockShapes);

        Shared.World.Atmos.AtmosInit.Classify(BlockWorld, Atmos);
        _atmosFlow.FlowRate = config.AtmosFlowRate;
        _atmosFlow.Epsilon = config.AtmosEpsilon;
        _atmosExposure = new global::Server.Atmos.AtmosExposure(config);
        _interactionHandlers = interactionHandlers ?? InteractionRegistry.Default(this);
    }

    public void Start()
    {
        var listener = new EventBasedNetListener();
        listener.ConnectionRequestEvent += OnConnectionRequest;
        listener.PeerConnectedEvent += OnPeerConnected;
        listener.PeerDisconnectedEvent += OnPeerDisconnected;
        listener.NetworkReceiveEvent += OnNetworkReceive;

        _server = new NetManager(listener);
        if (!_server.Start(_config.Port))
        {
            _server = null;
            throw new InvalidOperationException($"NetManager.Start failed to bind {_config.Ip}:{_config.Port} (port busy?)");
        }
        _isRunning = true;

        Console.WriteLine($"[Server] Started on port {_server.LocalPort}");
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

    /// <summary>Подключение пира: завести клиента и поднять OnClientConnected + начальный InventorySync в main-потоке.</summary>
    private void OnPeerConnected(NetPeer peer)
    {
        var client = new ClientConnection(peer, _nextConnectionId++);
        client.PlayerNetId = _netIdAllocator.Allocate(); // NetId из единого пула (не =ConnectionId)
        _clients[peer] = client;
        _entities[client.PlayerNetId] = client;

        Console.WriteLine($"[Server] Client #{client.ConnectionId} connected from {peer.Address}");

        _mainThreadActions.Enqueue(() =>
        {
            OnClientConnected?.Invoke(client);
            SendInventorySyncToOwner(client); // ПОСЛЕ OnClientConnected — HUD видит начальный full-state со спавна
            // Реестр (статичная геометрия) СТРОГО до сегментов: иначе клиент получит движение лифта, чью кабину не знает.
            SendToClient(client, global::Server.Lifts.LiftRegistryBuilder.Build(
                Lifts, _lifts.Controllers, _lifts.Result.Shafts));
            for (int i = 0; i < _lifts.Controllers.Count; i++)
                SendToClient(client, LiftSyncOf(_lifts.Controllers[i]));
        });
    }

    /// <summary>Отключение пира: убрать клиента и поднять OnClientDisconnected в main-потоке.</summary>
    private void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        if (_clients.TryGetValue(peer, out var client))
        {
            // Held-предметы НЕ в _entities (физически вынесены на pickup) → это ЕДИНСТВЕННЫЙ путь их вернуть в мир,
            // иначе утечка NetId. СИНХРОННО, ДО _entities.Remove(client.PlayerNetId) — как в GameLoop, drop-on-floor.
            _inventory.DropAllHeldOnDisconnect(client);
            _containers.OnClientDisconnect(client);
            _pull.OnClientDisconnect(client);
            global::Server.Lifts.LiftFloorSystem.OnClientDisconnect(client);

            _clients.Remove(peer);
            _entities.Remove(client.PlayerNetId);
            Console.WriteLine($"[Server] Client #{client.ConnectionId} disconnected: {disconnectInfo.Reason}");

            _mainThreadActions.Enqueue(() => OnClientDisconnected?.Invoke(client));
        }
    }

    /// <summary>Приём сообщения от клиента: разбор типа и диспетч через <see cref="MessageRouter"/>.</summary>
    private void OnNetworkReceive(NetPeer peer, NetDataReader reader, byte channel, DeliveryMethod method)
    {
        try
        {
            if (!_clients.TryGetValue(peer, out var client))
                return;

            client.LastActivity = DateTime.UtcNow;

            ushort typeId = reader.GetUShort();
            byte[] data = reader.GetBytesWithLength();
            _router.Dispatch(client, typeId, data);
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

            _groundItems.RebuildObstacles(); // раз/тик, ДО движения — свежая коллизия по крейтам для этого тика
            _lifts.Tick(_currentTick);
            for (int i = 0; i < _lifts.Changed.Count; i++)
                BroadcastToAll(LiftSyncOf(_lifts.Changed[i]));
            // Контракт лифтов: (1) Y(tick), (2) боксы В НОВОЙ позиции с DeltaY в набор, (3) Step — клиент (L2) обязан повторить.
            for (int i = 0; i < Lifts.Count; i++)
                Lifts[i].Tick(_currentTick, (Shared.Simulation.Blocks.DynamicObstacleSet)_groundItems.Obstacles);
            ProcessIntents();
            ProcessInteractions();
            _liftFloors.Process(_currentTick);
            _inventory.ProcessPickups();
            _inventory.ProcessDrops();
            _inventory.ProcessSlotOps();
            _containers.ProcessContainerOps();
            _pull.ProcessPullOps();
            _pull.ProcessFollow();
            ProcessStatus();
            if (_config.AtmosIntervalTicks > 0 && _currentTick % (uint)_config.AtmosIntervalTicks == 0)
                _atmosFlow.Step(BlockWorld!, Atmos, _config.AtmosMaxCellsPerSubtick);
            ProcessCombat();

            _currentTick++;
            ProcessBlockStreaming();
            ProcessBlockDoors();
            BroadcastBlockUpdates();
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
    /// FsmLogic.Step → гейт MovementAllowed && !DisableMovement → BlockMovementLogic.Step → ToFacing —
    /// БАЙТ-В-БАЙТ как в клиентском PlayerPredictor (ApplyLocal/Reconcile). Менять — только синхронно с ним.</summary>
    private void ApplyClientIntent(ClientConnection client, MoveIntent intent, bool hasIntent)
    {
        // Внутри контейнера (шкаф/ящик) — движение заморожено: только консьюмим Sequence (реконсиляция не застревает).
        if (client.ContainedInNetId != 0)
        {
            if (hasIntent) client.LastProcessedSequence = intent.Sequence;
            return;
        }

        var dir = hasIntent ? intent.Direction : IntentDirection.None;

        // reason — текущая причина лежания: KnockDown() ставит KnockedDown заранее (prev уже Laying), свежий
        // toggle-вход в Laying помечаем Voluntary ниже. Stun/KnockedDown держатся таймерами (ProcessStatus).
        var prev = client.State;
        client.State = FsmLogic.Step(client.State, dir, layToggle: hasIntent && intent.LayToggle,
                                     client.CurrentLayingReason, ref client.Timers);
        if (client.State == PlayerState.Laying && prev != PlayerState.Laying)
            client.CurrentLayingReason = LayingReason.Voluntary;

        bool canMove = FsmLogic.MovementAllowed(client.State) && !client.DisableMovement;
        var input = new Shared.Simulation.Blocks.BlockMoveInput(
            canMove && hasIntent ? dir : IntentDirection.None,
            sprint: hasIntent && intent.Sprint,
            jump: canMove && hasIntent && intent.Jump,
            crawl: client.State == PlayerState.Laying);
        Shared.Simulation.Blocks.BlockMovementLogic.Step(BlockWorld, BlockShapes, ref client.Mover, in input, client.Speed.CurrentValue, _groundItems.Obstacles);
        client.X = client.Mover.X;
        client.Y = client.Mover.Z;
        client.Z = (int)MathF.Floor(client.Mover.Y);
        if (hasIntent)
        {
            client.LastProcessedSequence = intent.Sequence;
            if (canMove)
                client.Facing = MovementLogic.ToFacing(intent.Direction, client.Facing);
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

    /// <summary>Резолвер ItemProto по ItemDefId; фасад — прокидывает в _inventory и _groundItems разом.</summary>
    public Func<ushort, ItemProto?> ProtoLookup
    {
        get => _inventory.ProtoLookup;
        set
        {
            _inventory.ProtoLookup = value;
            _groundItems.ProtoLookup = value;
        }
    }

    internal bool Reachable(int px, int py, int pz, int tx, int ty, int tz)
        => InteractionRules.InReachBlocks(px, py, pz, tx, ty, tz);

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
        if (!Reachable(px, py, client.Z, tx, ty, tz))
            return; // вне дальности — тихий дроп (анти-telegrab)

        var ctx = new InteractContext(client, tx, ty, tz, intent.Verb, intent.HandIndex);
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
    /// Выход из Stun/KnockedDown — только здесь (не предсказывается).</summary>
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

    /// <summary>Урон/здоровье за тик: отсюда Kill/SetUnconscious/ApplyStun/KnockDown. Сейчас — экспозиция
    /// атмосферы (удушье); остальные источники урона подключаются сюда же своими системами.</summary>
    private void ProcessCombat()
    {
        _atmosExposure.Process(_clients, BlockWorld!, Atmos, this);
    }

    // ItemDefId надетой формы (слот Uniform) для визуального оверлея на NetEntityView; 0 = ничего не надето.
    private static ushort WornDefOf(ClientConnection c)
    {
        int u = (int)Shared.World.Items.SlotCategory.Uniform;
        return u < c.Slots.Length && c.Slots[u].Length > 0 ? c.Slots[u][0].ItemDefId : (ushort)0;
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
        if (_broadcastContained.Length < _entities.Count)
            _broadcastContained = new bool[_entities.Count];

        int count = 0;
        foreach (var entity in _entities.Values)
        {
            if (entity is not ClientConnection c) continue;
            _broadcastContained[count] = c.ContainedInNetId != 0;
            _broadcastEntities[count++] = new EntitySnapshot
            {
                NetId = c.PlayerNetId,
                X = c.X,
                Y = c.Mover.Y,
                Z = c.Mover.Z,
                Facing = c.Facing,
                State = (byte)c.State,
                Reason = (byte)c.CurrentLayingReason,
                Speed = c.Speed.CurrentValue,
                VY = c.Mover.VY,
                WornUniformDefId = WornDefOf(c)
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
                bool self = _broadcastEntities[e].NetId == client.PlayerNetId;
                if (!self && _broadcastContained[e]) continue; // спрятан в контейнере — виден только себе (свой снапшот)
                if (self
                    || InInterestBlocks(client.X, client.Y, client.Z, in _broadcastEntities[e], interestR, interestZ))
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

    // Тонкие фасады над _groundItems (совместимость вызывающих/тестов) — см. [[ServerItems]].

    /// <summary>Заспавнить наземный предмет с автовыдачей NetId.</summary>
    public int SpawnGroundItem(ushort itemDefId, byte stackCount, float cellX, float cellY, float z, byte placement = 0)
        => _groundItems.SpawnGroundItem(itemDefId, stackCount, cellX, cellY, z, placement);

    /// <summary>Заспавнить наземный предмет с заданным NetId (переиспользуется при drop — см. заметку памяти NetId reuse).</summary>
    public void SpawnGroundItemWithId(int netId, ushort itemDefId, byte stackCount, float cellX, float cellY, float z, byte placement = 0)
        => _groundItems.SpawnGroundItemWithId(netId, itemDefId, stackCount, cellX, cellY, z, placement);

    /// <summary>Убрать наземный предмет из мира по NetId.</summary>
    public bool DespawnGroundItem(int netId) => _groundItems.DespawnGroundItem(netId);

    /// <summary>Найти предмет по NetId в любом местоположении (земля/рука/слот/контейнер).</summary>
    public bool TryFindItemAnyLocation(int netId, out ItemLocationKind location, out ushort itemDefId, out byte stackCount)
        => _groundItems.TryFindItemAnyLocation(netId, out location, out itemDefId, out stackCount);

    /// <summary>Наземные предметы в радиусе интереса точки (cx,cy,cz).</summary>
    public List<ItemInstance> GroundItemsInInterest(float cx, float cy, int cz)
        => _groundItems.GroundItemsInInterest(cx, cy, cz);

    /// <summary>Рассылает наземные предметы в интересе каждого клиента отдельным ItemSnapshot-потоком (Sequenced), не смешивая с WorldSnapshot.</summary>
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
            {
                var inst = GroundItemWorld.ToInstance(gi);
                inst.Open = _containers.IsWorldOpen(inst.NetId) ? (byte)1 : (byte)0; // визуальный флаг открытой крышки — см. [[Containers]]
                _broadcastItems[count++] = inst;
            }

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

            if (k == 0 && !client.SawGroundItems)
                continue; // и до, и сейчас пусто — не шлём (без wire-шума)
            client.SawGroundItems = k > 0; // непусто→пусто: шлём РОВНО один пустой снапшот — клиентский backstop снесёт вью

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
        _atmosFlow.WakeAround(BlockWorld!, Atmos, x, y, z);
        long key = Shared.World.Blocks.BlockGrid.KeyOfBlock(x, y, z);
        if (!BlockWorld.Sections.ContainsKey(key))
            _blockEmptiedTick.Add(key);
    }

    // Скан карты + печать сводки: Shared/DoorRegistry молчат и копят счётчики, печатает потребитель.
    private void BuildAutoDoorRegistry()
    {
        var stats = _doors.Build();
        Console.WriteLine($"[Doors] дверей в реестре: {stats.Registered} (Interact: {stats.Interact}, External: {stats.External}); " +
                          $"дверей-якорей: {stats.Openable} (пропуск: Auto без триггеров {stats.SkippedNoTrigger}, " +
                          $"частей мульти-блока {stats.SkippedNotAnchor})");
    }

    private void ProcessBlockDoors() => _doors.Tick(_currentTick);

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
    {        int pcx = FloorDivInt((int)MathF.Floor(client.Mover.X), Shared.World.Blocks.ChunkSection.Size);
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

    private static Shared.Messages.Lifts.LiftSync LiftSyncOf(global::Server.Lifts.LiftController c)
    {
        var seg = c.Runtime.Segment;
        return new Shared.Messages.Lifts.LiftSync
        {
            LiftId = c.LiftId,
            FromY = seg.FromY,
            ToY = seg.ToY,
            StartTick = seg.StartTick,
            BlocksPerTick = seg.BlocksPerTick,
            DwellUntilTick = c.Plan.DwellUntilTick,
            Calls = c.Calls
        };
    }

    /// <summary>Отправить сообщение одному клиенту.</summary>
    public void SendToClient<T>(ClientConnection client, T message) where T : struct, INetMessage
    {
        var writer = new NetDataWriter();
        writer.Put((ushort)message.Type);
        writer.PutBytesWithLength(message.Serialize());
        client.Peer.Send(writer, DeliveryMethod.ReliableOrdered);
    }

    /// <summary>Фасад над _inventory: полный InventorySync владельцу (руки+слоты).</summary>
    public void SendInventorySyncToOwner(ClientConnection owner) => _inventory.SendInventorySyncToOwner(owner);

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