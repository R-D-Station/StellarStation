using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using Server.Modules;
using Server.Network;
using Shared.Enums;
using Shared.Messages;

namespace Server.Core
{

    public class ServerBootstrap : MonoBehaviour
    {
        [SerializeField] private int _port = 7777;
        [SerializeField] private int _maxPlayers = 32;

        private TcpListener _listener;
        private bool _isRunning;
        private List<ServerClient> _clients = new List<ServerClient>();
        private int _nextPlayerId = 1;

        private AuthorizationModule _authModule;
        private LobbyModule _lobbyModule;

        void Start()
        {
            _authModule = new AuthorizationModule();
            _lobbyModule = new LobbyModule();

            StartServer();
        }

        async void StartServer()
        {
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            _isRunning = true;

            Debug.Log($"[Server] Started on port {_port}");

            while (_isRunning)
            {
                var tcpClient = await _listener.AcceptTcpClientAsync();

                if (_clients.Count >= _maxPlayers)
                {
                    // Отправляем отказ и закрываем
                    tcpClient.Close();
                    continue;
                }

                var client = new ServerClient(tcpClient, _nextPlayerId++);
                client.OnDisconnected += HandleDisconnect;
                client.OnMessageReceived += HandleMessage;
                _clients.Add(client);

                Debug.Log($"[Server] Client {client.PlayerId} connected. Total: {_clients.Count}");
            }
        }

        void HandleMessage(ServerClient client, byte[] data)
        {
            // Парсим заголовок
            int headerLength = BitConverter.ToInt32(data, 0);
            byte[] headerData = new byte[4 + headerLength];
            Array.Copy(data, 0, headerData, 0, 4 + headerLength);

            var header = Shared.Messages.MessageHeader.Deserialize(headerData);

            int bodySize = data.Length - 4 - headerLength;
            byte[] body = new byte[bodySize];
            if (bodySize > 0)
                Array.Copy(data, 4 + headerLength, body, 0, bodySize);

            switch (header.messageType)
            {
                case Shared.Enums.MessageType.LoginRequest:
                    _authModule.HandleLogin(client, body);
                    break;
                case MessageType.ChatMessage:
                    HandleChatMessage(client, body);
                    break;
            }
        }

        void HandleChatMessage(ServerClient sender, byte[] data)
        {
            var chatMsg = ChatMessage.Deserialize(data);
            chatMsg.playerName = sender.PlayerName;

            // Рассылаем всем клиентам
            byte[] msgData = chatMsg.Serialize();
            foreach (var client in _clients)
            {
                client.Send(msgData);
            }
        }

        void HandleDisconnect(ServerClient client)
        {
            _clients.Remove(client);
            _lobbyModule.RemovePlayer(client);
            Debug.Log($"[Server] Client {client.PlayerId} disconnected. Total: {_clients.Count}");
        }

        void OnDestroy()
        {
            _isRunning = false;
            _listener?.Stop();

            foreach (var client in _clients)
                client.Disconnect();
        }
    }
}