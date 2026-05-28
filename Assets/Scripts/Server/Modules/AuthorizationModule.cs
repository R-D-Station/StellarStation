using System;
using System.Text;
using UnityEngine;
using Server.Network;
using Shared.Enums;
using Shared.Messages;

namespace Server.Modules;

public class AuthorizationModule
{
    private LobbyModule _lobbyModule;

    public void SetLobby(LobbyModule lobby)
    {
        _lobbyModule = lobby;
    }

    public void HandleLogin(ServerClient client, byte[] data)
    {
        var request = LoginRequestMessage.Deserialize(data);

        // Простейшая проверка (можно добавить БД)
        if (string.IsNullOrEmpty(request.username) || request.username.Length < 2)
        {
            SendLoginResponse(client, false, "Имя слишком короткое");
            return;
        }

        // Устанавливаем имя игрока
        client.PlayerName = request.username;

        Debug.Log($"[Auth] Player logged in: {request.username} (ID: {client.PlayerId})");

        // Отправляем успешный ответ
        SendLoginResponse(client, true, null);

        // Добавляем в лобби
        _lobbyModule?.AddPlayer(client);

        // Отправляем GameStart (или позже, когда наберутся игроки)
        // Пока сразу кидаем в "игру" (на самом деле в лобби)
        SendLobbyState(client);
    }

    void SendLoginResponse(ServerClient client, bool success, string error)
    {
        var response = new LoginResponseMessage
        {
            success = success,
            playerId = success ? client.PlayerId : 0,
            errorMessage = error
        };

        SendMessage(client, MessageType.LoginResponse, response);
    }

    void SendLobbyState(ServerClient client)
    {
        var lobbyState = new LobbyStateMessage
        {
            players = _lobbyModule?.GetPlayerList() ?? new string[0]
        };

        SendMessage(client, MessageType.LobbyState, lobbyState);
    }

    void SendMessage(ServerClient client, MessageType type, object body)
    {
        string headerJson = $"{{\"messageType\":{(byte)type},\"playerId\":{client.PlayerId}}}";
        string bodyJson = JsonUtility.ToJson(body);

        byte[] headerJsonBytes = Encoding.UTF8.GetBytes(headerJson);
        byte[] header = new byte[4 + headerJsonBytes.Length];
        BitConverter.GetBytes(headerJsonBytes.Length).CopyTo(header, 0);
        headerJsonBytes.CopyTo(header, 4);

        byte[] bodyBytes = Encoding.UTF8.GetBytes(bodyJson);
        byte[] full = new byte[header.Length + bodyBytes.Length];
        header.CopyTo(full, 0);
        bodyBytes.CopyTo(full, header.Length);

        client.Send(full);
    }
}