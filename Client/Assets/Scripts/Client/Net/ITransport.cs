using Shared.Messages;
using Shared.Messages.Core;
using Shared.Messages.Player;
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

        bool IsConnected { get; }

        void Connect(string address, int port);
        void Disconnect();
        void Send<T>(T message) where T : struct, INetMessage;
        void Poll();
    }
}