using System;
using System.Text;
using UnityEngine;
using Shared.Enums;

namespace Shared.Messages
{
    [Serializable]
    public class MessageHeader
    {
        public MessageType messageType;
        public int playerId;

        public byte[] Serialize()
        {
            string json = JsonUtility.ToJson(this);
            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
            byte[] data = new byte[4 + jsonBytes.Length];
            BitConverter.GetBytes(jsonBytes.Length).CopyTo(data, 0);
            jsonBytes.CopyTo(data, 4);
            return data;
        }

        public static MessageHeader Deserialize(byte[] data)
        {
            // Первые 4 байта — длина JSON
            int length = BitConverter.ToInt32(data, 0);

            // Проверяем, что длина корректная
            if (length <= 0 || length > data.Length - 4)
            {
                Debug.LogError($"Invalid header length: {length}, data length: {data.Length}");
                return null;
            }

            // Извлекаем JSON
            string json = Encoding.UTF8.GetString(data, 4, length);
            return JsonUtility.FromJson<MessageHeader>(json);
        }

        public static int GetHeaderSize(byte[] data)
        {
            int jsonLength = BitConverter.ToInt32(data, 0);
            return 4 + jsonLength;
        }
    }
}