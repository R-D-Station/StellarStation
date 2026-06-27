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

            writer.Write(ServerTick);
            writer.Write(LastProcessedInput);
            writer.Write(Entities.Length);

            foreach (var entity in Entities)
            {
                byte[] entityData = entity.Serialize();
                writer.Write(entityData.Length);
                writer.Write(entityData);
            }

            return ms.ToArray();
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

                Entities = new EntitySnapshot[count];

                for (int i = 0; i < count; i++)
                {
                    if (ms.Position + 4 > ms.Length)
                        throw new InvalidOperationException($"Unexpected end of data while reading entity {i}");

                    int entitySize = reader.ReadInt32();

                    if (entitySize < 0 || entitySize > 1024 * 1024)
                        throw new InvalidOperationException($"Invalid entity size: {entitySize}");

                    if (ms.Position + entitySize > ms.Length)
                        throw new InvalidOperationException($"Entity {i} data exceeds available data");

                    byte[] entityData = reader.ReadBytes(entitySize);

                    var entity = new EntitySnapshot();
                    entity.Deserialize(entityData);
                    Entities[i] = entity;
                }
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