using System;
using System.IO;
using Shared.Messages;

namespace Shared.Messages.Core
{
    /// <summary>Секция ушла у клиента (server→client): OutOfRange — забыть (стопор «неизвестно», в.20);
    /// Emptied — секция стала чистым воздухом (явный сигнал опустошения, в.21).</summary>
    public struct BlockSectionGone : INetMessage
    {
        public const byte OutOfRange = 0;
        public const byte Emptied = 1;

        public byte GridId; // резерв фазы E (всегда 0)
        public int Cx;
        public int Cy; // вертикаль (оси Unity)
        public int Cz;
        public byte Reason;

        public MessageType Type => MessageType.BlockSectionGone;

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            writer.Write(GridId);
            writer.Write(Cx);
            writer.Write(Cy);
            writer.Write(Cz);
            writer.Write(Reason);

            return ms.ToArray();
        }

        public void Deserialize(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data), "BlockSectionGone data cannot be null");

            const int expectedSize = 14; // GridId(1) + Cx/Cy/Cz(12) + Reason(1)
            if (data.Length != expectedSize)
                throw new ArgumentException($"Invalid data size: expected {expectedSize}, got {data.Length}", nameof(data));

            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            GridId = reader.ReadByte();
            Cx = reader.ReadInt32();
            Cy = reader.ReadInt32();
            Cz = reader.ReadInt32();
            Reason = reader.ReadByte();
            if (Reason > Emptied)
                throw new InvalidOperationException($"Invalid Reason {Reason}");
        }
    }
}
