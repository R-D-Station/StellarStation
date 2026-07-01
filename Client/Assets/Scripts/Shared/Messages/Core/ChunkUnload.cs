using System;
using System.IO;
using Shared.Messages;

namespace Shared.Messages.Core
{
    /// <summary>Сервер просит клиента выгрузить чанк (игрок давно вне радиуса стрима). Клиент (2.3b) удаляет чанк
    /// из GridMap и сносит его рендер; при возврате в радиус чанк будет пере-стримлен. Тело: [cx][cy][z], 12 байт.</summary>
    public struct ChunkUnload : INetMessage
    {
        public int ChunkX;
        public int ChunkY;
        public int Z;

        public MessageType Type => MessageType.MapChunkUnload;

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write(ChunkX);
            w.Write(ChunkY);
            w.Write(Z);
            return ms.ToArray();
        }

        public void Deserialize(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            const int expectedSize = 12; // int×3
            if (data.Length != expectedSize)
                throw new ArgumentException($"Invalid ChunkUnload size: expected {expectedSize}, got {data.Length}", nameof(data));

            using var ms = new MemoryStream(data);
            using var r = new BinaryReader(ms);
            ChunkX = r.ReadInt32();
            ChunkY = r.ReadInt32();
            Z = r.ReadInt32();

            if (ms.Position != ms.Length)
                throw new InvalidOperationException("Unexpected extra data in ChunkUnload");
        }
    }
}
