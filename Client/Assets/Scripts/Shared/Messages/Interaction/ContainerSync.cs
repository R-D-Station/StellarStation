using System;
using System.IO;

namespace Shared.Messages.Interaction
{
    public struct ContainerItem
    {
        public ushort ItemDefId;
        public byte StackCount;
    }

    public struct ContainerSync : INetMessage
    {
        public int ContainerNetId;
        public ContainerItem[] Items;

        public const int PerItemSize = 3;
        private const int HeaderSize = 6;
        private const int MaxItems = 255;

        public MessageType Type => MessageType.ContainerSync;

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            writer.Write(ContainerNetId);
            ushort count = (ushort)(Items?.Length ?? 0);
            writer.Write(count);

            for (int i = 0; i < count; i++)
            {
                writer.Write(Items![i].ItemDefId);
                writer.Write(Items[i].StackCount);
            }

            return ms.ToArray();
        }

        public void Deserialize(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data), "ContainerSync data cannot be null");
            if (data.Length < HeaderSize)
                throw new ArgumentException($"Data too short: {data.Length} bytes (minimum {HeaderSize})", nameof(data));

            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            ContainerNetId = reader.ReadInt32();

            ushort count = reader.ReadUInt16();
            if (count > MaxItems)
                throw new InvalidOperationException($"Invalid item count: {count} (must be <= {MaxItems})");

            long need = (long)count * PerItemSize;
            if (ms.Position + need > ms.Length)
                throw new InvalidOperationException($"ContainerSync data too short for {count} items");

            var items = new ContainerItem[count];
            for (int i = 0; i < count; i++)
            {
                ushort defId = reader.ReadUInt16();
                byte stack = reader.ReadByte();
                if (defId == 0)
                    throw new InvalidOperationException("Invalid ItemDefId value: 0");
                items[i] = new ContainerItem { ItemDefId = defId, StackCount = stack };
            }
            Items = items;

            if (ms.Position != ms.Length)
                throw new InvalidOperationException($"Unexpected extra data: {ms.Length - ms.Position} bytes remaining");
        }
    }
}
