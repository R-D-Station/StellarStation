using System.Collections.Concurrent;
using LiteNetLib;
using LiteNetLib.Utils;
using Shared.Configs;
using Shared.Messages;
using Shared.Messages.Core;
using Shared.Simulation;
using Shared.World;
using Shared.Messages.Auth;
using Server.Services;

namespace Server.Network;

/// <summary>
/// Сетевой сервер: приём подключений, game-loop, симуляция и рассылка снапшотов мира.
/// </summary>
public class GameServer
{
    private readonly SVars _config;
    private readonly GridMap _map;
    private readonly AuthService _authService;
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

    private readonly List<NetPeer> _connectedPeersCache = new();

    public event Action<ClientConnection>? OnClientConnected;
    public event Action<ClientConnection>? OnClientDisconnected;
    public event Action<ClientConnection, MoveIntent>? OnMoveIntentReceived;

    // Точка спавна игроков (центр проходимой области карты).
    public float SpawnX => _spawnX;
    public float SpawnY => _spawnY;
    public int SpawnZ => _spawnZ;

    public GameServer(SVars config, GridMap? map = null)
    {
        _config = config;
        _map = map ?? new GridMap(); // пустая карта = мир без коллизии
        (_spawnX, _spawnY, _spawnZ) = FindSpawn(_map);
        _clients = new Dictionary<NetPeer, ClientConnection>();
        _mainThreadActions = new ConcurrentQueue<Action>();

        _authService = new AuthService(config);
        _authService.OnAuthSuccess += OnAuthSuccess;
        _authService.OnAuthFailed += OnAuthFailed;

        Console.WriteLine($"[Map] Spawn at ({_spawnX}, {_spawnY}, z{_spawnZ})");
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

        Console.WriteLine($"[Server] Started on port {_config.Port}");
        Console.WriteLine($"[Server] Max players: {_config.MaxPlayers}");
        Console.WriteLine($"[Server] Soft max players: {_config.SoftMaxPlayers}");
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
        _clients[peer] = client;

        Console.WriteLine($"[Server] Client #{client.ConnectionId} connected from {peer.Address}");

        _mainThreadActions.Enqueue(() => OnClientConnected?.Invoke(client));
    }

    /// <summary>Отключение пира: убрать клиента и поднять OnClientDisconnected в main-потоке.</summary>
    private void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        if (_clients.TryGetValue(peer, out var client))
        {
            _clients.Remove(peer);
            Console.WriteLine($"[Server] Client #{client.ConnectionId} disconnected: {disconnectInfo.Reason}");

            _mainThreadActions.Enqueue(() => OnClientDisconnected?.Invoke(client));
        }
    }

    /// <summary>Приём сообщения от клиента.</summary>
    private void OnNetworkReceive(NetPeer peer, NetDataReader reader, byte channel, DeliveryMethod method)
    {
        try
        {
            bool isAuthorized = _clients.ContainsKey(peer);
            MessageType type = (MessageType)reader.GetUShort();
            byte[] data = reader.GetBytesWithLength();

            // Если клиент не авторизован - разрешаем только AuthRequest
            if (!isAuthorized && type != MessageType.AuthRequest)
            {
                Console.WriteLine($"[Server] Unauthorized message from {peer.Address}: {type}");
                peer.Disconnect();
                return;
            }

            switch (type)
            {
                case MessageType.AuthRequest:
                    HandleAuthRequest(peer, data);
                    break;

                case MessageType.MoveIntent:
                    if (_clients.TryGetValue(peer, out var moveClient))
                    {
                        var intent = new MoveIntent();
                        intent.Deserialize(data);
                        moveClient.IntentQueue.Enqueue(intent);
                        moveClient.LastActivity = DateTime.UtcNow;
                    }
                    break;

                case MessageType.UseIntent:
                    if (_clients.TryGetValue(peer, out var useClient))
                    {
                        useClient.UseRequested = true;
                        useClient.LastActivity = DateTime.UtcNow;
                    }
                    break;

                default:
                    Console.WriteLine($"[Server] Unknown message type: {type}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Server] Error processing message: {ex.Message}");
        }
    }

    /// <summary>Обработка запроса на авторизацию.</summary>
    private void HandleAuthRequest(NetPeer peer, byte[] data)
    {
        try
        {
            var request = new ClientAuthRequest();
            request.Deserialize(data);

            Console.WriteLine($"[Auth] Received auth request from {peer.Address} for '{request.Login}'");

            // Проверяем, не авторизован ли уже этот пользователь
            if (_clients.Values.Any(c => c.Peer == peer))
            {
                SendAuthResponse(peer, new AuthResponse
                {
                    Status = AuthResponseStatus.Pending,
                    Message = "Already authenticated"
                });
                return;
            }

            // Проверяем soft лимит
            if (_clients.Count >= _config.SoftMaxPlayers)
            {
                int queuePosition = _clients.Count - _config.SoftMaxPlayers + 1;
                SendAuthResponse(peer, new AuthResponse
                {
                    Status = AuthResponseStatus.Queued,
                    Message = $"Server is full. Position in queue: {queuePosition}",
                    QueuePosition = queuePosition
                });
                return;
            }

            // Запускаем процесс авторизации
            _ = ProcessAuthAsync(peer, request);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Auth] Error handling auth request: {ex.Message}");
            SendAuthResponse(peer, new AuthResponse
            {
                Status = AuthResponseStatus.Error,
                Message = "Invalid request format"
            });
        }
    }

    /// <summary>Асинхронный процесс авторизации.</summary>
    private async Task ProcessAuthAsync(NetPeer peer, ClientAuthRequest request)
    {
        try
        {
            var session = await _authService.AuthenticateAsync(request, peer);

            // Если авторизация успешна - сессия будет обработана в OnAuthSuccess
            // Если нет - в OnAuthFailed
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Auth] Error in auth process: {ex.Message}");
            SendAuthResponse(peer, new AuthResponse
            {
                Status = AuthResponseStatus.Error,
                Message = "Internal server error"
            });
        }
    }

    /// <summary>Обработчик успешной авторизации.</summary>
    private void OnAuthSuccess(AuthSession session)
    {
        try
        {
            var peer = session.Peer;
            if (peer == null || peer.ConnectionState != ConnectionState.Connected)
            {
                Console.WriteLine($"[Auth] Peer disconnected during auth");
                return;
            }

            // Проверяем жесткий лимит
            if (_clients.Count >= _config.MaxPlayers)
            {
                SendAuthResponse(peer, new AuthResponse
                {
                    Status = AuthResponseStatus.Rejected,
                    Message = "Server is full"
                });
                return;
            }

            // Создаем клиента
            var client = new ClientConnection(peer, _nextConnectionId++);
            client.X = _spawnX;
            client.Y = _spawnY;
            client.Z = _spawnZ;
            client.Facing = 0;

            _clients[peer] = client;

            // Отправляем успешный ответ
            SendAuthResponse(peer, new AuthResponse
            {
                Status = AuthResponseStatus.Success,
                Message = "Authenticated successfully",
                PlayerNetId = client.PlayerNetId,
                SpawnX = _spawnX,
                SpawnY = _spawnY,
                SpawnZ = _spawnZ
            });

            // Вызываем событие подключения
            _mainThreadActions.Enqueue(() => OnClientConnected?.Invoke(client));

            Console.WriteLine($"[Auth] Player '{session.Login}' authenticated as #{client.ConnectionId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Auth] Error in OnAuthSuccess: {ex.Message}");
        }
    }

    /// <summary>Обработчик неудачной авторизации.</summary>
    private void OnAuthFailed(AuthSession session)
    {
        try
        {
            var peer = session.Peer;
            if (peer == null || peer.ConnectionState != ConnectionState.Connected)
                return;

            AuthResponseStatus status;
            string message;

            switch (session.Status)
            {
                case AuthSessionStatus.Rejected:
                    status = AuthResponseStatus.Rejected;
                    message = "Authentication rejected";
                    break;
                case AuthSessionStatus.Expired:
                    status = AuthResponseStatus.Timeout;
                    message = "Authentication timed out";
                    break;
                case AuthSessionStatus.Error:
                    status = AuthResponseStatus.Error;
                    message = "Authentication error";
                    break;
                default:
                    status = AuthResponseStatus.Rejected;
                    message = "Authentication failed";
                    break;
            }

            SendAuthResponse(peer, new AuthResponse
            {
                Status = status,
                Message = message
            });

            Console.WriteLine($"[Auth] Auth failed for '{session.Login}': {session.Status}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Auth] Error in OnAuthFailed: {ex.Message}");
        }
    }

    /// <summary>Отправка ответа авторизации.</summary>
    private void SendAuthResponse(NetPeer peer, AuthResponse response)
    {
        try
        {
            var writer = new NetDataWriter();
            writer.Put((ushort)MessageType.AuthResponse);
            writer.PutBytesWithLength(response.Serialize());
            peer.Send(writer, DeliveryMethod.ReliableOrdered);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Auth] Error sending response: {ex.Message}");
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
            _server?.PollEvents();

            while (_mainThreadActions.TryDequeue(out var action))
            {
                action();
            }

            ProcessIntents();
            ProcessUses();

            _currentTick++;
            UpdateDoors();
            BroadcastWorldSnapshot();

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

    /// <summary>Применяет по одному intent'у на клиента за тик; сбрасывает накопившийся хвост.</summary>
    private void ProcessIntents()
    {
        const int maxQueued = 4; // защита от разрастания очереди

        foreach (var client in _clients.Values)
        {
            // Сбрасываем накопившееся, оставляя только свежий intent.
            while (client.IntentQueue.Count > maxQueued && client.IntentQueue.TryDequeue(out _)) { }

            // Дефолт тика: нет ввода → Stand. Ниже перебьётся на Move, если позиция изменилась.
            client.State = PlayerState.Stand;

            if (client.IntentQueue.TryDequeue(out var intent))
            {
                float x = client.X;
                float y = client.Y;

                MovementLogic.Apply(_map, client.Z, ref x, ref y, intent.Direction, intent.Sprint);

                // Сравнение new vs old (client.X/Y ещё не обновлены): Apply либо двигает на
                // фиксированный StepPerTick, либо нет (детерминизм) — упор в стену даёт Stand.
                bool moved = x != client.X || y != client.Y;

                // Границы держит коллизия тайлов (Walkable); жёсткий clamp убран.

                client.X = x;
                client.Y = y;
                client.Facing = MovementLogic.ToFacing(intent.Direction, client.Facing);
                client.LastProcessedSequence = intent.Sequence;
                client.State = moved ? PlayerState.Move : PlayerState.Stand;

                // Бамп: упёрся в закрытую дверь по направлению ввода — открываем её.
                OpenBumpedDoors(client, intent.Direction, intent.Sprint);
            }
        }
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

        // Общий список сущностей; собираем один раз.
        var entities = _clients.Values.Select(c => new EntitySnapshot
        {
            NetId = c.PlayerNetId,
            X = c.X,
            Y = c.Y,
            Z = c.Z,
            Facing = c.Facing,
            State = (byte)c.State
        }).ToArray();

        // Каждому клиенту — его LastProcessedInput для reconciliation.
        foreach (var client in _clients.Values)
        {
            var snapshot = new WorldSnapshot
            {
                ServerTick = _currentTick,
                LastProcessedInput = client.LastProcessedSequence,
                Entities = entities
            };

            byte[] snapshotData = snapshot.Serialize();

            var writer = new NetDataWriter();
            writer.Put((ushort)MessageType.WorldSnapshot);
            writer.PutBytesWithLength(snapshotData);
            client.Peer.Send(writer, DeliveryMethod.ReliableOrdered);
        }
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

    /// <summary>Использовать тайл, на котором стоит игрок. Сейчас — лестницы (переход по Z).</summary>
    private void TryUseTile(ClientConnection client)
    {
        int px = (int)MathF.Floor(client.X);
        int py = (int)MathF.Floor(client.Y);
        int fromZ = client.Z;
        var tile = _map.GetTile(px, py, fromZ);

        int targetZ;
        if (tile.Special == TileSpecial.StairUp) targetZ = fromZ + 1;
        else if (tile.Special == TileSpecial.StairDown) targetZ = fromZ - 1;
        else return; // не лестница — ничего не делаем

        // Назначение — парная лестница в той же колонке. Переходим, только если там
        // можно стоять (редактор ставит пару с полом; защита от «лестницы в никуда»).
        var dest = _map.GetTile(px, py, targetZ);
        if (!dest.Walkable) return;

        client.X = px + 0.5f;   // в центр парной лестницы на другом этаже
        client.Y = py + 0.5f;
        client.Z = targetZ;
        Console.WriteLine($"[Stairs] #{client.ConnectionId}: z{fromZ} -> z{targetZ} at ({px},{py})");
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