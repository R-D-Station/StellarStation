using System;
using System.Text;
using UnityEngine;

namespace Shared.Messages;

[Serializable]
public class GameStartMessage
{
    public int[] playerIds;
    public float startTime;

    public static GameStartMessage Deserialize(byte[] data)
    {
        string json = Encoding.UTF8.GetString(data);
        return JsonUtility.FromJson<GameStartMessage>(json);
    }
}