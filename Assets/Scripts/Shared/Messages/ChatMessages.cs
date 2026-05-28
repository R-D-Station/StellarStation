using System;
using System.Text;
using UnityEngine;
using Shared.Enums;

namespace Shared.Messages;

[Serializable]
public class ChatMessage
{
    public int playerId;
    public string playerName;
    public string message;

    public byte[] Serialize()
    {
        MessageHeader header = new MessageHeader
        {
            messageType = MessageType.ChatMessage,
            playerId = playerId
        };

        byte[] headerData = header.Serialize();
        string json = JsonUtility.ToJson(this);
        byte[] jsonData = Encoding.UTF8.GetBytes(json);

        byte[] full = new byte[headerData.Length + jsonData.Length];
        headerData.CopyTo(full, 0);
        jsonData.CopyTo(full, headerData.Length);
        return full;
    }

    public static ChatMessage Deserialize(byte[] data)
    {
        string json = Encoding.UTF8.GetString(data);
        return JsonUtility.FromJson<ChatMessage>(json);
    }
}