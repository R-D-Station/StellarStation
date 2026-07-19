using Shared.Messages;
using Shared.Messages.Core;
using Shared.Messages.Player;
using Shared.Messages.Interaction;
using System;

namespace Client.Net
{
    /// <summary>Транспортный слой клиента (отправка/приём сообщений сервера).</summary>
    public interface ITransport
    {
        event Action OnConnected;
        event Action OnDisconnected;
        event Action<WorldSnapshot> OnWorldSnapshot;
        event Action<LoginResponse> OnLoginResponse;

        /// <summary>Карта от сервера (реплика мира).</summary>
        event Action<MapDataMessage> OnMapData;

        /// <summary>Изменение одного тайла в рантайме (дверь и т.п.).</summary>
        event Action<TileUpdate> OnTileUpdate;

        /// <summary>Игрок вошёл / вышел (lifecycle сетевых сущностей).</summary>
        event Action<PlayerJoined> OnPlayerJoined;
        event Action<PlayerLeft> OnPlayerLeft;

        /// <summary>Стрим карты: пришёл чанк / выгрузить чанк (замена разовой MapData).</summary>
        event Action<ChunkData> OnChunkData;
        event Action<ChunkUnload> OnChunkUnload;

        /// <summary>Отдельный PVS-поток наземных предметов (server→client, 4.4).</summary>
        event Action<ItemSnapshot> OnItemSnapshot;

        /// <summary>Полный слепок инвентаря владельца (server→client, OWNER-ONLY, 4.5).</summary>
        event Action<InventorySync> OnInventorySync;

        /// <summary>Блочный стрим (фаза C): секция / секция ушла / пакет дельт (server→client).</summary>
        event Action<BlockChunkData> OnBlockChunkData;
        event Action<BlockSectionGone> OnBlockSectionGone;
        event Action<BlockUpdateBatch> OnBlockUpdateBatch;

        bool IsConnected { get; }

        void Connect(string address, int port);
        void Disconnect();
        void Send<T>(T message) where T : struct, INetMessage;
        void Poll();
    }
}