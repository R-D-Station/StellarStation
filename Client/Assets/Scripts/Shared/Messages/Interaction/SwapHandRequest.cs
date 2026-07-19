using System;
using System.IO;

namespace Shared.Messages.Interaction
{
    /// <summary>Сменить активную руку (client→server); ActiveHand серверно-авторитетен.</summary>
    public struct SwapHandRequest : INetMessage
    {
        public byte Hand;

        public MessageType Type => MessageType.SwapHand;

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            writer.Write(Hand);
            return ms.ToArray();
        }

        public void Deserialize(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data), "SwapHandRequest data cannot be null");

            const int expectedSize = 1; // Hand(1)
            if (data.Length != expectedSize)
                throw new ArgumentException($"Invalid data size: expected {expectedSize} bytes, got {data.Length} bytes", nameof(data));

            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            byte hand = reader.ReadByte();
            if (hand >= 2)
                throw new InvalidOperationException($"Invalid Hand value: {hand} (must be < 2)");
            Hand = hand;

            if (ms.Position != ms.Length)
                throw new InvalidOperationException($"Unexpected extra data: {ms.Length - ms.Position} bytes remaining");
        }
    }
}
