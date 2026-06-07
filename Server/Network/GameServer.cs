using System.Collections.Concurrent;
using LiteNetLib;
using LiteNetLib.Utils;
using Shared.Configs;
using Shared.Messages;
using Shared.Messages.Core;

namespace Server.Network;

public class GameServer
{
    private readonly SVars _config;
    private NetManager? _server;
    private readonly Dictionary<NetPeer, ClientConnection> _clients;
    private readonly ConcurrentQueue<Action> _mainThreadActions;
    private bool _isRunning;
    private int _nextConnectionId = 1;
    private uint _currentTick;

    // Кеш для списка подключённых пиров
    private readonly List<NetPeer> _connectedPeersCache = new();

    public event Action<ClientConnection>? OnClientConnected;
    public event Action<ClientConnection>? OnClientDisconnected;
    public event Action<ClientConnection, MoveIntent>? OnMoveIntentReceived;

    public GameServer(SVars config)
    {
        _config = config;
        _clients = new Dictionary<NetPeer, ClientConnection>();
        _mainThreadActions = new ConcurrentQueue<Action>();
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

    /// <summary>
    /// Обработчик отключения клиента. Он удаляет клиента из словаря, 
    /// выводит сообщение в консоль и вызывает событие OnClientDisconnected на главном потоке.
    /// </summary>
    private void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
    {
        if (_clients.TryGetValue(peer, out var client))
        {
            _clients.Remove(peer);
            Console.WriteLine($"[Server] Client #{client.ConnectionId} disconnected: {disconnectInfo.Reason}");

            _mainThreadActions.Enqueue(() => OnClientDisconnected?.Invoke(client));
        }
    }

    /// <summary>
    /// Обработчик входящих сообщений от клиентов. 
    /// Сначала он пытается найти соответствующего клиента по пиру, чтобы обновить 
    /// его время активности, затем читает тип сообщения и данные.
    /// </summary>
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
                case MessageType.MoveIntent:
                    var intent = new MoveIntent();
                    intent.Deserialize(data);
                    _mainThreadActions.Enqueue(() => OnMoveIntentReceived?.Invoke(client, intent));
                    break;

                default:
                    Console.WriteLine($"[Server] Unknown message type from #{client.ConnectionId}: {type}");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Server] Error processing message from #{peer.Address}: {ex.Message}");

            // TODO: возможно, стоит отключать клиента, если он шлёт битые пакеты
        }
    }

    private async Task GameLoop()
    {
        int tickMs = 1000 / _config.TickRate;

        while (_isRunning)
        {
            var startTime = DateTime.UtcNow;

            _server?.PollEvents();

            while (_mainThreadActions.TryDequeue(out var action))
            {
                action();
            }

            _currentTick++;
            BroadcastWorldSnapshot();

            var elapsed = DateTime.UtcNow - startTime;
            int sleepMs = tickMs - (int)elapsed.TotalMilliseconds;

            if (sleepMs > 0)
                await Task.Delay(sleepMs);
        }
    }

    /// <summary>
    /// Метод для создания и отправки снимка мира всем подключённым клиентам. 
    /// Снимок содержит текущий тик сервера, список всех сущностей (игроков) с их позициями и направлениями. 
    /// Клиенты будут использовать эти данные для синхронизации своего состояния с сервером.
    /// </summary>
    private void BroadcastWorldSnapshot()
    {
        if (_clients.Count == 0)
            return;

        var snapshot = new WorldSnapshot
        {
            ServerTick = _currentTick,
            LastProcessedInput = 0,
            Entities = _clients.Values.Select(c => new EntitySnapshot
            {
                NetId = c.PlayerNetId,
                X = c.X,
                Y = c.Y,
                Z = c.Z,
                Facing = c.Facing
            }).ToArray()
        };

        byte[] snapshotData = snapshot.Serialize();

        foreach (var client in _clients.Values)
        {
            var writer = new NetDataWriter();
            writer.Put((ushort)MessageType.WorldSnapshot);
            writer.PutBytesWithLength(snapshotData);
            client.Peer.Send(writer, DeliveryMethod.ReliableOrdered);
        }
    }

    /// <summary>
    /// Метод для обновления позиции игрока на сервере.
    /// </summary>
    public void UpdatePlayerPosition(ClientConnection client, float x, float y, int z, byte facing)
    {
        client.X = x;
        client.Y = y;
        client.Z = z;
        client.Facing = facing;
    }

    /// <summary>
    /// Метод для отправки сообщения конкретному клиенту.
    /// </summary>
    public void SendToClient<T>(ClientConnection client, T message) where T : struct, INetMessage
    {
        var writer = new NetDataWriter();
        writer.Put((ushort)message.Type);
        writer.PutBytesWithLength(message.Serialize());
        client.Peer.Send(writer, DeliveryMethod.ReliableOrdered);
    }

    /// <summary>
    /// Метод для широковещательной отправки сообщения всем клиентам, с возможностью фильтрации по предикату.
    /// </summary>
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