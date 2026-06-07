using System;
using System.IO;
using Shared.Messages;

namespace Shared.Messages.Core
{
    public struct EntitySnapshot : INetMessage
    {
        public int NetId;
        public float X;
        public float Y;
        public float Z;
        public byte Facing;

        public MessageType Type => MessageType.EntitySnapshot;

        /// <summary>
        /// —ериализует данные EntitySnapshot в компактный байтовый массив дл€ передачи по сети.
        /// </summary>
        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            writer.Write(NetId);
            writer.Write(X);
            writer.Write(Y);
            writer.Write(Z);
            writer.Write(Facing);

            return ms.ToArray();
        }

        /// <summary>
        /// Ѕезопасна€ реализаци€ десериализации, котора€ провер€ет размер данных, 
        /// обрабатывает исключени€ и гарантирует целостность данных.
        /// </summary>
        public void Deserialize(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data), "EntitySnapshot data cannot be null");

            // EntitySnapshot: NetId(4) + X(4) + Y(4) + Z(4) + Facing(1) = 17 байт
            const int expectedSize = 17;

            if (data.Length != expectedSize)
                throw new ArgumentException($"Invalid data size: expected {expectedSize} bytes, got {data.Length} bytes", nameof(data));

            try
            {
                using var ms = new MemoryStream(data);
                using var reader = new BinaryReader(ms);

                NetId = reader.ReadInt32();

                float x = reader.ReadSingle();
                if (float.IsNaN(x) || float.IsInfinity(x))
                    throw new InvalidOperationException($"X coordinate is invalid (NaN or Infinity)");
                X = x;

                float y = reader.ReadSingle();
                if (float.IsNaN(y) || float.IsInfinity(y))
                    throw new InvalidOperationException($"Y coordinate is invalid (NaN or Infinity)");
                Y = y;

                float z = reader.ReadSingle();
                if (float.IsNaN(z) || float.IsInfinity(z))
                    throw new InvalidOperationException($"Z coordinate is invalid (NaN or Infinity)");
                Z = z;

                Facing = reader.ReadByte();

                if (ms.Position != ms.Length)
                    throw new InvalidOperationException($"Unexpected extra data: {ms.Length - ms.Position} bytes remaining");

            }
            catch (EndOfStreamException ex)
            {
                throw new InvalidOperationException("Unexpected end of data while reading EntitySnapshot", ex);
            }
            catch (IOException ex)
            {
                throw new InvalidOperationException("IO error while reading EntitySnapshot", ex);
            }
        }

        public int TileX => (int)MathF.Floor(X);
        public int TileY => (int)MathF.Floor(Y);
    }
}