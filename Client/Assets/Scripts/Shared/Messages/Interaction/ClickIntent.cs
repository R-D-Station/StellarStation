using System;
using System.IO;
using Shared.Messages;

namespace Shared.Messages.Interaction
{
    /// <summary>Тип цели адресной интеракции.</summary>
    public enum InteractTargetKind : byte
    {
        Tile = 0,
        Entity = 1
    }

    /// <summary>Вид взаимодействия: основной (ЛКМ) или альтернативный (ПКМ).</summary>
    public enum InteractVerb : byte
    {
        Primary = 0,
        Alt = 1
    }

    /// <summary>Адресная интеракция клиент→сервер (request-only, без Sequence/предсказания): цель (тайл|сущность),
    /// вид, рука и ЦЕЛЫЙ тайл-цель.</summary>
    public struct InteractIntent : INetMessage
    {
        public byte TargetKind; // InteractTargetKind
        public byte Verb;       // InteractVerb
        public byte HandIndex;  // активная рука (0/1)
        public int TileX;
        public int TileY;
        public int TileZ;
        public int TargetNetId; // -1 = цель-тайл (TargetKind=Tile)

        private const int MaxCoord = 10_000_000; // разумный предел координат (защита от мусора)

        public MessageType Type => MessageType.InteractIntent;

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            writer.Write(TargetKind);
            writer.Write(Verb);
            writer.Write(HandIndex);
            writer.Write(TileX);
            writer.Write(TileY);
            writer.Write(TileZ);
            writer.Write(TargetNetId);

            return ms.ToArray();
        }

        public void Deserialize(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data), "InteractIntent data cannot be null");

            // TargetKind(1) + Verb(1) + HandIndex(1) + TileX(4) + TileY(4) + TileZ(4) + TargetNetId(4) = 19 байт
            const int expectedSize = 19;

            if (data.Length != expectedSize)
                throw new ArgumentException($"Invalid data size: expected {expectedSize} bytes, got {data.Length} bytes", nameof(data));

            try
            {
                using var ms = new MemoryStream(data);
                using var reader = new BinaryReader(ms);

                byte targetKind = reader.ReadByte();
                if (!Enum.IsDefined(typeof(InteractTargetKind), targetKind))
                    throw new InvalidOperationException($"Invalid TargetKind value: {targetKind}");
                TargetKind = targetKind;

                byte verb = reader.ReadByte();
                if (!Enum.IsDefined(typeof(InteractVerb), verb))
                    throw new InvalidOperationException($"Invalid Verb value: {verb}");
                Verb = verb;

                byte handIndex = reader.ReadByte();
                if (handIndex >= 2)
                    throw new InvalidOperationException($"Invalid HandIndex value: {handIndex} (must be < 2)");
                HandIndex = handIndex;

                TileX = ReadCoord(reader, nameof(TileX));
                TileY = ReadCoord(reader, nameof(TileY));
                TileZ = ReadCoord(reader, nameof(TileZ));

                int targetNetId = reader.ReadInt32();
                if (targetNetId < -1)
                    throw new InvalidOperationException($"Invalid TargetNetId value: {targetNetId} (must be >= -1)");
                TargetNetId = targetNetId;

                if (ms.Position != ms.Length)
                    throw new InvalidOperationException($"Unexpected extra data: {ms.Length - ms.Position} bytes remaining");
            }
            catch (EndOfStreamException ex)
            {
                throw new InvalidOperationException("Unexpected end of data while reading InteractIntent", ex);
            }
            catch (IOException ex)
            {
                throw new InvalidOperationException("IO error while reading InteractIntent", ex);
            }
        }

        private static int ReadCoord(BinaryReader reader, string name)
        {
            int v = reader.ReadInt32();
            if (v < -MaxCoord || v > MaxCoord)
                throw new InvalidOperationException($"{name} {v} out of range [-{MaxCoord}, {MaxCoord}]");
            return v;
        }
    }
}
