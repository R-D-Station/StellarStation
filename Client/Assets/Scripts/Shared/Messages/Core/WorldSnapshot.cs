using System;
using System.IO;
using System.Text;
using Shared.Messages;

namespace Shared.Messages.Core
{
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
            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            ServerTick = reader.ReadUInt32();
            LastProcessedInput = reader.ReadUInt32();

            int count = reader.ReadInt32();
            Entities = new EntitySnapshot[count];

            for (int i = 0; i < count; i++)
            {
                int entitySize = reader.ReadInt32();
                byte[] entityData = reader.ReadBytes(entitySize);

                var entity = new EntitySnapshot();
                entity.Deserialize(entityData);
                Entities[i] = entity;
            }
        }
    }
}