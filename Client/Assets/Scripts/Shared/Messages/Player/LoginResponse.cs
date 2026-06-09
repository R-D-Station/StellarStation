using System;
using System.IO;

namespace Shared.Messages.Player
{
    /// <summary>
    /// Ответ сервера новому клиенту при подключении. Несёт NetId, который
    /// сервер присвоил этому игроку. Клиент запоминает его, чтобы отличать
    /// свою сущность в WorldSnapshot от чужих (нужно для предсказания).
    /// </summary>
    public struct LoginResponse : INetMessage
    {
        public int NetId;

        public MessageType Type => MessageType.LoginResponse;

        public byte[] Serialize()
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);

            writer.Write(NetId);

            return ms.ToArray();
        }

        public void Deserialize(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data), "LoginResponse data cannot be null");

            // LoginResponse: NetId(4) = 4 байта
            const int expectedSize = 4;

            if (data.Length != expectedSize)
                throw new ArgumentException($"Invalid data size: expected {expectedSize} bytes, got {data.Length} bytes", nameof(data));

            try
            {
                using var ms = new MemoryStream(data);
                using var reader = new BinaryReader(ms);

                NetId = reader.ReadInt32();

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