using LiteNetLib;
using System.Collections.Concurrent;
using Shared.Messages.Core;
using Shared.Simulation;

namespace Server.Network
{
    /// <summary>Состояние подключённого клиента: пир, координаты игрока и очередь intent'ов.</summary>
    public class ClientConnection
    {
        public NetPeer Peer { get; set; }
        public int ConnectionId { get; set; }
        public DateTime ConnectedAt { get; set; }
        public DateTime LastActivity { get; set; }

        // Состояние игрока
        public int PlayerNetId { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public int Z { get; set; }
        public byte Facing { get; set; }
        public PlayerState State { get; set; } // дефолт Stand(0); стампится в ProcessIntents каждый тик

        // Для reconciliation
        public uint LastProcessedSequence { get; set; }

        // Запрос «использовать» (E): обрабатывается в game-loop.
        public bool UseRequested { get; set; }

        // Очередь intent'ов; обрабатывается в GameLoop (по одному на тик).
        public readonly ConcurrentQueue<MoveIntent> IntentQueue = new();

        public ClientConnection(NetPeer peer, int connectionId)
        {
            Peer = peer;
            ConnectionId = connectionId;
            ConnectedAt = DateTime.UtcNow;
            LastActivity = DateTime.UtcNow;
            PlayerNetId = connectionId;
        }
    }
}