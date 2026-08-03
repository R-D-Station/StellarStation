using LiteNetLib;
using LiteNetLib.Utils;
using Shared.Messages;
using Shared.Messages.Core;
using Shared.Messages.Player;
using Shared.Messages.Interaction;
using Shared.Messages.Atmos;
using Shared.Messages.Lifts;
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
        public event Action<PlayerJoined> OnPlayerJoined;
        public event Action<PlayerLeft> OnPlayerLeft;
        public event Action<ItemSnapshot> OnItemSnapshot;
        public event Action<InventorySync> OnInventorySync;
        public event Action<ContainerSync> OnContainerSync;
        public event Action<PullSync> OnPullSync;
        public event Action<ContainSync> OnContainSync;
        public event Action<AtmosSync> OnAtmosSync;
        public event Action<LiftSync> OnLiftSync;
        public event Action<LiftRegistry> OnLiftRegistry;
        public event Action<BlockChunkData> OnBlockChunkData;
        public event Action<BlockSectionGone> OnBlockSectionGone;
        public event Action<BlockUpdateBatch> OnBlockUpdateBatch;


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
            { MessageType.PlayerJoined, () => new PlayerJoined() },
            { MessageType.PlayerLeft, () => new PlayerLeft() },
            { MessageType.ItemSnapshot, () => new ItemSnapshot() },
            { MessageType.InventorySync, () => new InventorySync() },
            { MessageType.ContainerSync, () => new ContainerSync() },
            { MessageType.PullSync, () => new PullSync() },
            { MessageType.ContainSync, () => new ContainSync() },
            { MessageType.AtmosSync, () => new AtmosSync() },
            { MessageType.LiftSync, () => new LiftSync() },
            { MessageType.LiftRegistry, () => new LiftRegistry() },
            { MessageType.BlockChunkData, () => new BlockChunkData() },
            { MessageType.BlockSectionGone, () => new BlockSectionGone() },
            { MessageType.BlockUpdateBatch, () => new BlockUpdateBatch() },
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

                case PlayerJoined pj:
                    OnPlayerJoined?.Invoke(pj);
                    break;

                case PlayerLeft pl:
                    OnPlayerLeft?.Invoke(pl);
                    break;

                case ItemSnapshot s:
                    OnItemSnapshot?.Invoke(s);
                    break;

                case InventorySync inv:
                    OnInventorySync?.Invoke(inv);
                    break;

                case ContainerSync cs:
                    OnContainerSync?.Invoke(cs);
                    break;

                case PullSync ps:
                    OnPullSync?.Invoke(ps);
                    break;

                case ContainSync cs:
                    OnContainSync?.Invoke(cs);
                    break;

                case AtmosSync a:
                    OnAtmosSync?.Invoke(a);
                    break;

                case LiftSync ls:
                    OnLiftSync?.Invoke(ls);
                    break;

                case LiftRegistry lr:
                    OnLiftRegistry?.Invoke(lr);
                    break;

                case BlockChunkData bc:
                    OnBlockChunkData?.Invoke(bc);
                    break;

                case BlockSectionGone bg:
                    OnBlockSectionGone?.Invoke(bg);
                    break;

                case BlockUpdateBatch bu:
                    OnBlockUpdateBatch?.Invoke(bu);
                    break;

                default:
                    Debug.LogWarning($"[Transport] Unhandled message type: {type}");
                    break;
            }
        }
    }
}