using LiteNetLib;
using LiteNetLib.Utils;
using Shared.Messages;
using Shared.Messages.Core;
using Shared.Messages.Player;
using Shared.Messages.Interaction;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Client.Net
{
    /// <summary>Транспорт поверх LiteNetLib: подключение к серверу и обмен сообщениями.</summary>
    public class LiteNetLibTransport : ITransport
    {
        public event Action OnConnected;
        public event Action OnDisconnected;
        public event Action<WorldSnapshot> OnWorldSnapshot;
        public event Action<MoveIntent> OnMoveIntentReceived;
        public event Action<LoginResponse> OnLoginResponse;
        public event Action<MapDataMessage> OnMapData;
        public event Action<TileUpdate> OnTileUpdate;
        public event Action<PlayerJoined> OnPlayerJoined;
        public event Action<PlayerLeft> OnPlayerLeft;
        public event Action<ChunkData> OnChunkData;
        public event Action<ChunkUnload> OnChunkUnload;
        public event Action<ItemSnapshot> OnItemSnapshot;
        public event Action<InventorySync> OnInventorySync;


        public bool IsConnected { get; private set; }

        private NetManager _client;
        private EventBasedNetListener _listener;
        private NetPeer _server;
        private readonly string _connectionKey = "VGVzdF9zZXJ2ZXIx";

        /// <summary>Фабрики сообщений по MessageType.</summary>
        private static readonly Dictionary<MessageType, Func<INetMessage>> _messageFactories = new()
        {
            { MessageType.MoveIntent, () => new MoveIntent() },
            { MessageType.WorldSnapshot, () => new WorldSnapshot() },
            { MessageType.LoginResponse, () => new LoginResponse() },
            { MessageType.MapData, () => new MapDataMessage() },
            { MessageType.TileUpdate, () => new TileUpdate() },
            { MessageType.PlayerJoined, () => new PlayerJoined() },
            { MessageType.PlayerLeft, () => new PlayerLeft() },
            { MessageType.MapChunk, () => new ChunkData() },
            { MessageType.MapChunkUnload, () => new ChunkUnload() },
            { MessageType.ItemSnapshot, () => new ItemSnapshot() },
            { MessageType.InventorySync, () => new InventorySync() },
        };

        public void Connect(string address, int port)
        {
            _listener = new EventBasedNetListener();
            _listener.PeerConnectedEvent += OnPeerConnected;
            _listener.PeerDisconnectedEvent += OnPeerDisconnected;
            _listener.NetworkReceiveEvent += OnNetworkReceive;

            _client = new NetManager(_listener);
            _client.Start();

            _server = _client.Connect(address, port, _connectionKey);

            Debug.Log($"[Transport] Connecting to {address}:{port}");
        }

        public void Disconnect()
        {
            _client?.Stop();
            IsConnected = false;
        }

        public void Send<T>(T message) where T : struct, INetMessage
        {
            if (_server == null || _server.ConnectionState != ConnectionState.Connected)
                return;

            var writer = new NetDataWriter();

            writer.Put((ushort)message.Type);

            byte[] data = message.Serialize();
            writer.PutBytesWithLength(data);

            _server.Send(writer, DeliveryMethod.ReliableOrdered);
        }

        public void Poll()
        {
            _client?.PollEvents();
        }

        private void OnPeerConnected(NetPeer peer)
        {
            IsConnected = true;
            Debug.Log($"[Transport] Connected to server");
            OnConnected?.Invoke();
        }

        private void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            IsConnected = false;
            Debug.Log($"[Transport] Disconnected: {disconnectInfo.Reason}");
            OnDisconnected?.Invoke();
        }

        private void OnNetworkReceive(NetPeer peer, NetDataReader reader, byte channel, DeliveryMethod method)
        {
            MessageType type = (MessageType)reader.GetUShort();

            byte[] data = reader.GetBytesWithLength();
            if (!_messageFactories.TryGetValue(type, out var factory))
            {
                Debug.LogWarning($"[Transport] Unknown message type: {type}");
                return;
            }

            var message = factory();
            message.Deserialize(data);

            switch (message)
            {
                case WorldSnapshot snapshot:
                    OnWorldSnapshot?.Invoke(snapshot);
                    break;

                case LoginResponse login:
                    OnLoginResponse?.Invoke(login);
                    break;

                case MapDataMessage map:
                    OnMapData?.Invoke(map);
                    break;

                case TileUpdate tu:
                    OnTileUpdate?.Invoke(tu);
                    break;

                case PlayerJoined pj:
                    OnPlayerJoined?.Invoke(pj);
                    break;

                case PlayerLeft pl:
                    OnPlayerLeft?.Invoke(pl);
                    break;

                case ChunkData cd:
                    OnChunkData?.Invoke(cd);
                    break;

                case ChunkUnload cu:
                    OnChunkUnload?.Invoke(cu);
                    break;

                case ItemSnapshot s:
                    OnItemSnapshot?.Invoke(s);
                    break;

                case InventorySync inv:
                    OnInventorySync?.Invoke(inv);
                    break;

                default:
                    Debug.LogWarning($"[Transport] Unhandled message type: {type}");
                    break;
            }
        }
    }
}