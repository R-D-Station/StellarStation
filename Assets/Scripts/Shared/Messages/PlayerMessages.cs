using System;
using System.Text;
using UnityEngine;

namespace Shared.Messages
{
    [Serializable]
    public class PlayerJoinedMessage
    {
        public int playerId;
        public string playerName;

        public static PlayerJoinedMessage Deserialize(byte[] data)
        {
            string json = Encoding.UTF8.GetString(data);
            return JsonUtility.FromJson<PlayerJoinedMessage>(json);
        }
    }

    [Serializable]
    public class PlayerLeftMessage
    {
        public int playerId;
        public string playerName;

        public static PlayerLeftMessage Deserialize(byte[] data)
        {
            string json = Encoding.UTF8.GetString(data);
            return JsonUtility.FromJson<PlayerLeftMessage>(json);
        }
    }
}