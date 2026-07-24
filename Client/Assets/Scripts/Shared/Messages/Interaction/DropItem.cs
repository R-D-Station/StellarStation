using System;
using System.IO;
using Shared.World.Items;

namespace Shared.Messages.Interaction
{
    /// <summary>Выбросить предмет из слота (client→server): адрес (Category, Index), drop-at-feet на серверных координатах.</summary>
    public struct DropItem : INetMessage
    {
        public SlotCategory Category;
        public byte Index;

        public MessageType Type => MessageType.DropItem;

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            writer.Write((byte)Category);
            writer.Write(Index);
            return ms.ToArray();
        }

        public void Deserialize(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data), "DropItem data cannot be null");

            const int expectedSize = 2;
            if (data.Length != expectedSize)
                throw new ArgumentException($"Invalid data size: expected {expectedSize} bytes, got {data.Length} bytes", nameof(data));

            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            byte cat = reader.ReadByte();
            byte index = reader.ReadByte();

            if (cat >= InventorySlot.CategoryCount)
                throw new InvalidOperationException($"Invalid SlotCategory value: {cat}");
            if (index >= InventorySlot.DefaultCount((SlotCategory)cat))
                throw new InvalidOperationException($"Invalid slot Index {index} for category {cat}");

            Category = (SlotCategory)cat;
            Index = index;

            if (ms.Position != ms.Length)
                throw new InvalidOperationException($"Unexpected extra data: {ms.Length - ms.Position} bytes remaining");
        }
    }
}
