using System;
using System.IO;
using Shared.Messages;

namespace Shared.Messages.Player
{
    /// <summary>Сервер→клиент: в мир вошёл игрок NetId (клиент пред-создаёт вьюху до первого снапшота).</summary>
    public struct PlayerJoined : INetMessage
    {
        public int NetId;

        public MessageType Type => MessageType.PlayerJoined;

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            writer.Write(NetId);

            return ms.ToArray();
        }

        public void Deserialize(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data), "PlayerJoined data cannot be null");

            // NetId(4) = 4 байта
            const int expectedSize = 4;

            if (data.Length != expectedSize)
                throw new ArgumentException($"Invalid data size: expected {expectedSize} bytes, got {data.Length} bytes", nameof(data));

            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            NetId = reader.ReadInt32();

            if (ms.Position != ms.Length)
                throw new InvalidOperationException($"Unexpected extra data: {ms.Length - ms.Position} bytes remaining");
        }
    }
}
