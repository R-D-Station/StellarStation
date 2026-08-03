using System;
using System.IO;

namespace Shared.Messages.Lifts
{
    /// <summary>client→server: выбор этажа с панели в кабине. Request-only, без предсказания — лифты тикают
    /// ДО обработки интеракций, поэтому эффект не раньше следующего тика.</summary>
    public struct LiftFloorRequest : INetMessage
    {
        public int LiftId;
        public byte Floor;

        public MessageType Type => MessageType.LiftFloorRequest;

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            writer.Write(LiftId);
            writer.Write(Floor);
            return ms.ToArray();
        }

        public void Deserialize(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data), "LiftFloorRequest data cannot be null");

            const int expectedSize = 5;
            if (data.Length != expectedSize)
                throw new ArgumentException($"Invalid data size: expected {expectedSize} bytes, got {data.Length} bytes", nameof(data));

            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            LiftId = reader.ReadInt32();
            Floor = reader.ReadByte();

            if (ms.Position != ms.Length)
                throw new InvalidOperationException($"Unexpected extra data: {ms.Length - ms.Position} bytes remaining");
        }
    }
}
