using System;
using System.IO;

namespace Shared.Messages.Lifts
{
    public struct LiftSync : INetMessage
    {
        public int LiftId;
        public float FromY;
        public float ToY;
        public uint StartTick;
        public float BlocksPerTick;
        public uint DwellUntilTick;
        public uint Calls;

        public MessageType Type => MessageType.LiftSync;

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            writer.Write(LiftId);
            writer.Write(FromY);
            writer.Write(ToY);
            writer.Write(StartTick);
            writer.Write(BlocksPerTick);
            writer.Write(DwellUntilTick);
            writer.Write(Calls);
            return ms.ToArray();
        }

        public void Deserialize(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data), "LiftSync data cannot be null");

            const int expectedSize = 28;
            if (data.Length != expectedSize)
                throw new ArgumentException($"Invalid data size: expected {expectedSize} bytes, got {data.Length} bytes", nameof(data));

            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            LiftId = reader.ReadInt32();
            FromY = ReadFinite(reader, "FromY");
            ToY = ReadFinite(reader, "ToY");
            StartTick = reader.ReadUInt32();
            BlocksPerTick = ReadFinite(reader, "BlocksPerTick");
            DwellUntilTick = reader.ReadUInt32();
            Calls = reader.ReadUInt32();

            if (ms.Position != ms.Length)
                throw new InvalidOperationException($"Unexpected extra data: {ms.Length - ms.Position} bytes remaining");
        }

        private static float ReadFinite(BinaryReader reader, string field)
        {
            float v = reader.ReadSingle();
            if (float.IsNaN(v) || float.IsInfinity(v))
                throw new InvalidOperationException($"{field} is invalid (NaN or Infinity)");
            return v;
        }
    }
}
