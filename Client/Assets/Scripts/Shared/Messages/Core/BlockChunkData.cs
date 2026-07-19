using System;
using System.IO;
using Shared.Messages;
using Shared.World.Blocks;

namespace Shared.Messages.Core
{
    /// <summary>Одна секция 16³ блок-мира (server→client, ReliableOrdered — «снапшот раньше первой дельты»
    /// гарантирует порядок канала). Payload — кодек BlockMapSerializer.WriteSection (диск = wire, в.17).</summary>
    public struct BlockChunkData : INetMessage
    {
        public byte GridId; // резерв фазы E (всегда 0)
        public int Cx;
        public int Cy; // вертикаль (оси Unity)
        public int Cz;
        public ChunkSection Section;

        public MessageType Type => MessageType.BlockChunkData;

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            writer.Write(GridId);
            writer.Write(Cx);
            writer.Write(Cy);
            writer.Write(Cz);
            BlockMapSerializer.WriteSection(writer, Section);

            return ms.ToArray();
        }

        public void Deserialize(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data), "BlockChunkData data cannot be null");
            if (data.Length < 14) // GridId(1) + Cx/Cy/Cz(12) + минимум кодека
                throw new ArgumentException($"Invalid data size: {data.Length}", nameof(data));

            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            GridId = reader.ReadByte();
            Cx = reader.ReadInt32();
            Cy = reader.ReadInt32();
            Cz = reader.ReadInt32();
            Section = BlockMapSerializer.ReadSection(reader);

            if (ms.Position != ms.Length)
                throw new InvalidOperationException($"Unexpected extra data: {ms.Length - ms.Position} bytes remaining");
        }
    }
}
