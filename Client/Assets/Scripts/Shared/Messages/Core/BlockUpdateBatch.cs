using System;
using System.IO;
using Shared.Messages;

namespace Shared.Messages.Core
{
    /// <summary>Пакет дельт блоков одного тика (server→client, ТОЛЬКО держателям секций — в.44/112).
    /// Каждая запись — итоговое состояние позиции: тип + state-байт.</summary>
    public struct BlockUpdateBatch : INetMessage
    {
        public struct Entry
        {
            public int X;
            public int Y; // высота (оси Unity)
            public int Z;
            public ushort BlockType;
            public byte State;
        }

        public byte GridId; // резерв фазы E (всегда 0)
        public Entry[] Entries;

        private const int MaxEntries = 4096; // защита от мусорного count (потолок — вся секция за тик)
        private const int EntrySize = 15;    // X(4)+Y(4)+Z(4)+Type(2)+State(1)

        public MessageType Type => MessageType.BlockUpdateBatch;

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            writer.Write(GridId);
            int count = Entries?.Length ?? 0;
            writer.Write((ushort)count);
            for (int i = 0; i < count; i++)
            {
                writer.Write(Entries[i].X);
                writer.Write(Entries[i].Y);
                writer.Write(Entries[i].Z);
                writer.Write(Entries[i].BlockType);
                writer.Write(Entries[i].State);
            }

            return ms.ToArray();
        }

        public void Deserialize(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data), "BlockUpdateBatch data cannot be null");
            if (data.Length < 3) // GridId(1) + count(2)
                throw new ArgumentException($"Invalid data size: {data.Length}", nameof(data));

            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            GridId = reader.ReadByte();
            int count = reader.ReadUInt16();
            if (count > MaxEntries)
                throw new InvalidOperationException($"BlockUpdateBatch count {count} exceeds {MaxEntries}");
            if (data.Length != 3 + count * EntrySize)
                throw new InvalidOperationException($"BlockUpdateBatch size mismatch: {data.Length} vs {3 + count * EntrySize}");

            Entries = new Entry[count];
            for (int i = 0; i < count; i++)
            {
                Entries[i].X = reader.ReadInt32();
                Entries[i].Y = reader.ReadInt32();
                Entries[i].Z = reader.ReadInt32();
                Entries[i].BlockType = reader.ReadUInt16();
                Entries[i].State = reader.ReadByte();
            }
        }
    }
}
