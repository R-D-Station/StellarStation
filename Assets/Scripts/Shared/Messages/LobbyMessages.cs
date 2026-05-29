using System;
using System.Text;
using UnityEngine;

namespace Shared.Messages;

[Serializable]
public class LobbyStateMessage
{
    public string[] players;

    public static LobbyStateMessage Deserialize(byte[] data)
    {
        string json = Encoding.UTF8.GetString(data);
        return JsonUtility.FromJson<LobbyStateMessage>(json);
    }
}