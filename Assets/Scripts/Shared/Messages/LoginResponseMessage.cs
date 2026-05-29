using System;
using System.Text;
using UnityEngine;

namespace Shared.Messages
{
    [Serializable]
    public class LoginResponseMessage
    {
        public bool success;
        public int playerId;
        public string errorMessage;

        public static LoginResponseMessage Deserialize(byte[] data)
        {
            string json = Encoding.UTF8.GetString(data);
            return JsonUtility.FromJson<LoginResponseMessage>(json);
        }
    }
}