using System;
using System.IO;
using Shared.Messages;

namespace Shared.Messages.Core
{
    public struct MoveIntent : INetMessage
    {
        public IntentDirection Direction;
        public bool Sprint;
        public uint Sequence;

        private const uint MaxSequence = 10_000_000; // ћаксимальный номер тика (защита от переполнени€)

        public MessageType Type => MessageType.MoveIntent;

        /// <summary>
        /// —ериализует данные EntitySnapshot в компактный байтовый массив дл€ передачи по сети.
        /// </summary>
        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            writer.Write((byte)Direction);
            writer.Write(Sprint);
            writer.Write(Sequence);

            return ms.ToArray();
        }

        /// <summary>
        /// Ѕезопасна€ реализаци€ десериализации, котора€ провер€ет размер данных, 
        /// обрабатывает исключени€ и гарантирует целостность данных.
        /// </summary>
        public void Deserialize(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data), "MoveIntent data cannot be null");

            // MoveIntent: Direction(1) + Sprint(1) + Sequence(4) = 6 байт
            const int expectedSize = 6;

            if (data.Length != expectedSize)
                throw new ArgumentException($"Invalid data size: expected {expectedSize} bytes, got {data.Length} bytes", nameof(data));

            try
            {
                using var ms = new MemoryStream(data);
                using var reader = new BinaryReader(ms);

                byte directionByte = reader.ReadByte();
                if (!Enum.IsDefined(typeof(IntentDirection), directionByte))
                    throw new InvalidOperationException($"Invalid Direction value: {directionByte}. Valid values: 0-4");
                Direction = (IntentDirection)directionByte;

                Sprint = reader.ReadBoolean();

                uint sequence = reader.ReadUInt32();
                if (sequence > MaxSequence)
                    throw new InvalidOperationException($"Sequence {sequence} exceeds maximum allowed value {MaxSequence}");
                Sequence = sequence;

                if (ms.Position != ms.Length)
                    throw new InvalidOperationException($"Unexpected extra data: {ms.Length - ms.Position} bytes remaining");
            }
            catch (EndOfStreamException ex)
            {
                throw new InvalidOperationException("Unexpected end of data while reading MoveIntent", ex);
            }
            catch (IOException ex)
            {
                throw new InvalidOperationException("IO error while reading MoveIntent", ex);
            }
        }
    }

    /// <summary>
    /// Ќаправление движени€ по тайлам (намерение, не позици€)
    /// None;
    /// North = +Y;
    /// South = -Y;
    /// East = +X;
    /// West = -X;
    /// </summary>
    public enum IntentDirection : byte
    {
        None = 0,
        North = 1,
        South = 2,
        East = 3,
        West = 4
    }
}