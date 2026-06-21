using LiteNetLib;
using System.Collections.Concurrent;
using Shared.Messages.Core;

namespace Server.Network
{
    public class ClientConnection
    {
        public NetPeer Peer { get; set; }
        public int ConnectionId { get; set; }
        public DateTime ConnectedAt { get; set; }
        public DateTime LastActivity { get; set; }

        // Данные игрока
        public int PlayerNetId { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public int Z { get; set; }
        public byte Facing { get; set; }

        // Для reconciliation
        public uint LastProcessedSequence { get; set; }

        // Входящие intent'ы, накопленные между тиками. Обрабатываются в GameLoop
        // (по одному за тик), а не сразу при приёме — так движение детерминировано
        // и совпадает с клиентским предсказанием.
        public readonly ConcurrentQueue<MoveIntent> IntentQueue = new();

        public ClientConnection(NetPeer peer, int connectionId)
        {
            Peer = peer;
            ConnectionId = connectionId;
            ConnectedAt = DateTime.UtcNow;
            LastActivity = DateTime.UtcNow;
            PlayerNetId = connectionId; // Просто для начала
        }
    }
}