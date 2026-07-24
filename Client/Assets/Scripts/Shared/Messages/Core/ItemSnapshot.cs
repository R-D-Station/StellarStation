using System;
using System.IO;
using Shared.Messages;
using Shared.World.Items;

namespace Shared.Messages.Core
{
    /// <summary>Снапшот наземных предметов в интересе клиента — ОТДЕЛЬНЫЙ PVS-поток (НЕ смешан с player-WorldSnapshot).</summary>
    // Формат: [count i32][count × 20б]. Per-item: NetId(4)+ItemDefId(2)+X(f32)+Y(f32)+Z(f32)+StackCount(1)+Open(1). Placement на проводе НЕ идёт (MVP).
    public struct ItemSnapshot : INetMessage
    {
        public ItemInstance[] Items;

        /// <summary>Размер одного предмета на проводе: NetId(4)+ItemDefId(2)+X(f32)+Y(f32)+Z(f32)+StackCount(1)+Open(1).</summary>
        public const int PerItemSize = 20;

        private const int MaxItems = 10000; // анти-DoS: кап числа ДО аллокации (как WorldSnapshot)

        public MessageType Type => MessageType.ItemSnapshot;

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            WriteTo(writer, Items, Items.Length);
            return ms.ToArray();
        }

        /// <summary>Пишет предметы из ВНЕШНЕГО буфера (первые count) — горячий broadcast-путь, zero-alloc. Формат: [count i32][items].</summary>
        public void WriteTo(BinaryWriter writer, ItemInstance[] items, int count)
        {
            writer.Write(count);
            for (int i = 0; i < count; i++)
                WriteItem(writer, in items[i]);
        }

        private static void WriteItem(BinaryWriter writer, in ItemInstance it)
        {
            writer.Write(it.NetId);      // 4
            writer.Write(it.ItemDefId);  // 2
            writer.Write(it.X);          // 4
            writer.Write(it.Y);          // 4
            writer.Write(it.Z);          // 4
            writer.Write(it.StackCount); // 1
            writer.Write(it.Open);       // 1
        }

        public void Deserialize(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));
            if (data.Length < 4)
                throw new InvalidOperationException($"Data too short: {data.Length} bytes (minimum 4)");

            try
            {
                using var ms = new MemoryStream(data);
                using var reader = new BinaryReader(ms);

                int count = reader.ReadInt32();
                if (count < 0 || count > MaxItems)
                    throw new InvalidOperationException($"Invalid item count: {count}");

                // Валидируем весь блок ДО аллокации (кап уже применён; произведение влезает в long).
                long need = (long)count * PerItemSize;
                if (ms.Position + need > ms.Length)
                    throw new InvalidOperationException($"ItemSnapshot data too short for {count} items");

                Items = new ItemInstance[count];
                for (int i = 0; i < count; i++)
                    Items[i] = ReadItem(reader);

                if (ms.Position != ms.Length)
                    throw new InvalidOperationException($"Unexpected extra data: {ms.Length - ms.Position} bytes");
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to deserialize ItemSnapshot: {ex.Message}", ex);
            }
        }

        private static ItemInstance ReadItem(BinaryReader reader)
        {
            var it = new ItemInstance
            {
                NetId = reader.ReadInt32(),
                ItemDefId = reader.ReadUInt16()
            };

            float x = reader.ReadSingle();
            if (float.IsNaN(x) || float.IsInfinity(x))
                throw new InvalidOperationException("X coordinate is invalid (NaN or Infinity)");
            it.X = x;

            float y = reader.ReadSingle();
            if (float.IsNaN(y) || float.IsInfinity(y))
                throw new InvalidOperationException("Y coordinate is invalid (NaN or Infinity)");
            it.Y = y;

            float z = reader.ReadSingle();
            if (float.IsNaN(z) || float.IsInfinity(z))
                throw new InvalidOperationException("Z coordinate is invalid (NaN or Infinity)");
            it.Z = z;

            it.StackCount = reader.ReadByte();
            it.Open = reader.ReadByte();
            it.Placement = 0; // на проводе не идёт (MVP)
            return it;
        }
    }
}
