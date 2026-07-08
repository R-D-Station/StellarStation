using System;
using System.IO;
using Shared.Messages;

namespace Shared.Messages.Core
{
    /// <summary>Снапшот одной сущности: NetId, позиция (X/Y/Z), взгляд, FSM-состояние, причина лежания и скорость.</summary>
    public struct EntitySnapshot : INetMessage
    {
        public int NetId;
        public float X;
        public float Y;
        public float Z;
        public byte Facing;
        public byte State; // FSM-состояние, реплицируется как byte (Shared.Simulation.PlayerState)
        public byte Reason; // причина лежания (Shared.Simulation.LayingReason): Voluntary/KnockedDown
        public float Speed; // эффективная скорость/тик (= ClientConnection.Speed.CurrentValue); предиктор берёт baseStep отсюда

        public MessageType Type => MessageType.EntitySnapshot;

        /// <summary>Фиксированный размер сериализованной сущности в байтах (length-prefix в WorldSnapshot).</summary>
        public const int SerializedSize = 23;

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            WriteTo(writer);
            return ms.ToArray();
        }

        /// <summary>Пишет SerializedSize байт в writer (zero-alloc broadcast-путь). Байт-в-байт как Serialize().</summary>
        public void WriteTo(BinaryWriter writer)
        {
            writer.Write(NetId);
            writer.Write(X);
            writer.Write(Y);
            writer.Write(Z);
            writer.Write(Facing);
            writer.Write(State);
            writer.Write(Reason);
            writer.Write(Speed);
        }

        /// <summary>Прочитать SerializedSize байт сущности НАПРЯМУЮ из reader (zero-alloc: без byte[]/вложенного
        /// MemoryStream — для горячего клиентского пути WorldSnapshot.Deserialize). Тот же порядок/валидация, что
        /// Deserialize(byte[]); хвост-чек делает внешний вызывающий (WorldSnapshot валидирует блок разом).
        /// Shared/** — engine-agnostic (System.IO, без UnityEngine).</summary>
        public static EntitySnapshot ReadFrom(BinaryReader r)
        {
            if (r == null)
                throw new ArgumentNullException(nameof(r));

            var e = new EntitySnapshot { NetId = r.ReadInt32() };

            float x = r.ReadSingle();
            if (float.IsNaN(x) || float.IsInfinity(x))
                throw new InvalidOperationException("X coordinate is invalid (NaN or Infinity)");
            e.X = x;

            float y = r.ReadSingle();
            if (float.IsNaN(y) || float.IsInfinity(y))
                throw new InvalidOperationException("Y coordinate is invalid (NaN or Infinity)");
            e.Y = y;

            float z = r.ReadSingle();
            if (float.IsNaN(z) || float.IsInfinity(z))
                throw new InvalidOperationException("Z coordinate is invalid (NaN or Infinity)");
            e.Z = z;

            e.Facing = r.ReadByte();
            e.State = r.ReadByte();  // любой byte валиден (рендерится как PlayerState)
            e.Reason = r.ReadByte(); // любой byte валиден (рендерится как LayingReason)

            float speed = r.ReadSingle();
            if (float.IsNaN(speed) || float.IsInfinity(speed))
                throw new InvalidOperationException("Speed is invalid (NaN or Infinity)");
            e.Speed = speed;

            return e;
        }

        public void Deserialize(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data), "EntitySnapshot data cannot be null");

            // NetId(4) + X(4) + Y(4) + Z(4) + Facing(1) + State(1) + Reason(1) + Speed(4) = 23 байт
            const int expectedSize = 23;

            if (data.Length != expectedSize)
                throw new ArgumentException($"Invalid data size: expected {expectedSize} bytes, got {data.Length} bytes", nameof(data));

            try
            {
                using var ms = new MemoryStream(data);
                using var reader = new BinaryReader(ms);

                this = ReadFrom(reader); // единый источник чтения полей (нет дрейфа между byte[]- и reader-путём)

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