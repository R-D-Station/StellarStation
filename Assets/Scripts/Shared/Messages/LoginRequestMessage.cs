using System;
using System.Text;
using UnityEngine;
using Shared.Enums;

namespace Shared.Messages;

[Serializable]
public class LoginRequestMessage
{
    public string username;

    public byte[] Serialize()
    {
        MessageHeader header = new MessageHeader
        {
            messageType = MessageType.LoginRequest,
            playerId = 0
        };

        byte[] headerData = header.Serialize();
        string json = JsonUtility.ToJson(this);
        byte[] jsonData = Encoding.UTF8.GetBytes(json);

        byte[] fullMessage = new byte[headerData.Length + jsonData.Length];
        headerData.CopyTo(fullMessage, 0);
        jsonData.CopyTo(fullMessage, headerData.Length);

        return fullMessage;
    }
}