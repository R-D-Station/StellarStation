using System;
using System.IO;

namespace Shared.Messages.Player
{
    /// <summary>Ответ сервера при подключении: NetId игрока, серверный TickRate и режим мира (B2).</summary>
    public struct LoginResponse : INetMessage
    {
        public int NetId;
        public byte TickRate; // серверный SVars.TickRate — клиент тикает на нём (enforcement инварианта tickRate==TickRate)
        public bool BlocksWorld; // сервер в блок-режиме: мир стримится секциями (фаза C), предикт — блочной логикой
        public byte ShapesMode;  // формы блоков: 0 = DevBlockWorld (полигон), 1 = каталог (карта из редактора)
        public float ZoneFadeDistance;
        public float ZoneFadeVertical;

        public MessageType Type => MessageType.LoginResponse;

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            writer.Write(NetId);
            writer.Write(TickRate);
            writer.Write(BlocksWorld);
            writer.Write(ShapesMode);
            writer.Write(ZoneFadeDistance);
            writer.Write(ZoneFadeVertical);

            return ms.ToArray();
        }

        public void Deserialize(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data), "LoginResponse data cannot be null");

            // NetId(4) + TickRate(1) + BlocksWorld(1) + ShapesMode(1) + ZoneFadeDistance(4) + ZoneFadeVertical(4) = 15 байт
            const int expectedSize = 15;

            if (data.Length != expectedSize)
                throw new ArgumentException($"Invalid data size: expected {expectedSize} bytes, got {data.Length} bytes", nameof(data));

            try
            {
                using var ms = new MemoryStream(data);
                using var reader = new BinaryReader(ms);

                NetId = reader.ReadInt32();
                TickRate = reader.ReadByte();
                BlocksWorld = reader.ReadBoolean();
                ShapesMode = reader.ReadByte();
                ZoneFadeDistance = reader.ReadSingle();
                ZoneFadeVertical = reader.ReadSingle();

                if (ms.Position != ms.Length)
                    throw new InvalidOperationException($"Unexpected extra data: {ms.Length - ms.Position} bytes remaining");
            }
            catch (EndOfStreamException ex)
            {
                throw new InvalidOperationException("Unexpected end of data while reading LoginResponse", ex);
            }
            catch (IOException ex)
            {
                throw new InvalidOperationException("IO error while reading LoginResponse", ex);
            }
        }
    }
}