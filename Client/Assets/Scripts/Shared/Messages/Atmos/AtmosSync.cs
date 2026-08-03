using System;
using System.IO;

namespace Shared.Messages.Atmos
{
    public struct AtmosSync : INetMessage
    {
        public float PressureKpa;
        public float OxygenKpa;

        public MessageType Type => MessageType.AtmosSync;

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            writer.Write(PressureKpa);
            writer.Write(OxygenKpa);
            return ms.ToArray();
        }

        public void Deserialize(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data), "AtmosSync data cannot be null");

            const int expectedSize = 8;
            if (data.Length != expectedSize)
                throw new ArgumentException($"Invalid data size: expected {expectedSize} bytes, got {data.Length} bytes", nameof(data));

            using var ms = new MemoryStream(data);
            using var reader = new BinaryReader(ms);

            float pressure = reader.ReadSingle();
            if (float.IsNaN(pressure) || float.IsInfinity(pressure))
                throw new InvalidOperationException("PressureKpa is invalid (NaN or Infinity)");
            PressureKpa = pressure;

            float oxygen = reader.ReadSingle();
            if (float.IsNaN(oxygen) || float.IsInfinity(oxygen))
                throw new InvalidOperationException("OxygenKpa is invalid (NaN or Infinity)");
            OxygenKpa = oxygen;

            if (ms.Position != ms.Length)
                throw new InvalidOperationException($"Unexpected extra data: {ms.Length - ms.Position} bytes remaining");
        }
    }
}
