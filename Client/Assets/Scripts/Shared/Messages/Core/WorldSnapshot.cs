using System;
using System.IO;
using System.Text;
using Shared.Messages;

namespace Shared.Messages.Core
{
    /// <summary>Авторитетный снапшот мира на тик: список сущностей + номер обработанного ввода.</summary>
    public struct WorldSnapshot : INetMessage
    {
        public uint ServerTick;
        public uint LastProcessedInput;
        public EntitySnapshot[] Entities;

        public MessageType Type => MessageType.WorldSnapshot;

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            WriteTo(writer);
            return ms.ToArray();
        }

        /// <summary>Пишет снапшот в writer БЕЗ per-entity аллокаций (горячий broadcast-путь). Layout (2.5-A): хедер
        /// 12 байт (ServerTick u32 + LastProcessedInput u32 + count i32), затем count × EntitySnapshot.SerializedSize
        /// байт БЕЗ per-entity len-prefix (сущность фикс-размера → префикс избыточен). Итого 12 + count·23.</summary>
        public void WriteTo(BinaryWriter writer)
        {
            writer.Write(ServerTick);
            writer.Write(LastProcessedInput);
            writer.Write(Entities.Length);

            foreach (var entity in Entities)
                entity.WriteTo(writer);
        }

        /// <summary>Как WriteTo(writer), но сущности берутся из ВНЕШНЕГО буфера — пишутся только первые count
        /// (entity-PVS: per-client отфильтрованный набор в переиспользуемом массиве). Формат идентичен: хедер (Server-
        /// Tick/LastProcessedInput из этого снапшота) + count + count×SerializedSize. Не аллоцирует; Entities не читает.</summary>
        public void WriteTo(BinaryWriter writer, EntitySnapshot[] entities, int count)
        {
            writer.Write(ServerTick);
            writer.Write(LastProcessedInput);
            writer.Write(count);

            for (int i = 0; i < count; i++)
                entities[i].WriteTo(writer);
        }

        public void Deserialize(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            if (data.Length < 12)
                throw new InvalidOperationException($"Data too short: {data.Length} bytes (minimum 12)");

            try
            {
                using var ms = new MemoryStream(data);
                using var reader = new BinaryReader(ms);

                ServerTick = reader.ReadUInt32();
                LastProcessedInput = reader.ReadUInt32();

                int count = reader.ReadInt32();

                if (count < 0 || count > 10000)
                    throw new InvalidOperationException($"Invalid entity count: {count}");

                // Фикс-размер сущности → нет per-entity len-prefix. Валидируем весь блок разом (защита от битой длины
                // до чтения; count уже капнут ≤10000, произведение влезает в long).
                long need = (long)count * EntitySnapshot.SerializedSize;
                if (ms.Position + need > ms.Length)
                    throw new InvalidOperationException($"Snapshot data too short for {count} entities");

                Entities = new EntitySnapshot[count];

                // Читаем сущности НАПРЯМУЮ из reader — без per-entity byte[]/вложенного MemoryStream (zero-alloc).
                for (int i = 0; i < count; i++)
                    Entities[i] = EntitySnapshot.ReadFrom(reader);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to deserialize WorldSnapshot: {ex.Message}", ex);
            }
        }
    }
}