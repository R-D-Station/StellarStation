using System;
using System.Text;
using UnityEngine;
using Shared.Enums;

namespace Shared.Messages;

[Serializable]
public class PlayerPositionMessage
{
    public int playerId;
    public float posX;
    public float posY;
    public float velX;
    public float velY;

    public byte[] Serialize()
    {
        MessageHeader header = new MessageHeader
        {
            messageType = MessageType.PlayerPosition,
            playerId = playerId
        };

        byte[] headerData = header.Serialize();
        string json = JsonUtility.ToJson(this);
        byte[] jsonData = Encoding.UTF8.GetBytes(json);

        byte[] fullMessage = new byte[headerData.Length + jsonData.Length];
        headerData.CopyTo(fullMessage, 0);
        jsonData.CopyTo(fullMessage, headerData.Length);

        return fullMessage;
    }

    public static PlayerPositionMessage Deserialize(byte[] data)
    {
        string json = Encoding.UTF8.GetString(data);
        return JsonUtility.FromJson<PlayerPositionMessage>(json);
    }
}