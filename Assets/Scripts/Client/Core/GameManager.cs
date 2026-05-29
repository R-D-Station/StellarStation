using System;
using UnityEngine;
using Client.Network;
using Client.Gameplay.Entities;
using Shared.Messages;
using Shared.Enums;
using Client.UI;

namespace Client.Core
{
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("UI")]
        [SerializeField] private GameObject _loginPanel;
        [SerializeField] private UI.LoginUI _loginUI;
        private Client.UI.LobbyUI _lobbyUI;

        [Header("Player")]
        [SerializeField] private Player _player;

        private GameClient _gameClient;
        private int _localPlayerId = -1;
        private string _playerName;

        public enum State { Login, Connecting, Lobby, InGame }
        private State _currentState;

        void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Destroy(gameObject);
                return;
            }

            if (_player == null)
                _player = FindFirstObjectByType<Player>();

            if (_player != null)
                _player.enabled = false;

            _lobbyUI = GetComponent<Client.UI.LobbyUI>();
            if (_lobbyUI == null)
                _lobbyUI = gameObject.AddComponent<Client.UI.LobbyUI>();

            gameObject.AddComponent<Client.UI.LobbyUICreator>();

            _gameClient = new GameClient();
            _gameClient.OnConnected += OnConnected;
            _gameClient.OnDisconnected += OnDisconnected;
            _gameClient.OnMessageReceived += OnMessageReceived;
            _gameClient.OnError += OnError;

            SetState(State.Login);
        }

        public void ConnectToServer(string ip, string login)
        {
            _playerName = login;
            SetState(State.Connecting);
            _loginUI?.ShowConnecting();
            _gameClient.Connect(ip, 7777);
        }

        void OnConnected()
        {
            Debug.Log("[GameManager] Connected, sending login...");
            var loginMsg = new LoginRequestMessage { username = _playerName };
            _gameClient.SendMessage(loginMsg.Serialize());
        }

        void OnMessageReceived(byte[] data)
        {
            try
            {
                int headerLength = BitConverter.ToInt32(data, 0);

                if (data.Length < 4 + headerLength)
                {
                    Debug.LogError($"[GameManager] Message too short: {data.Length} bytes, header says {headerLength}");
                    return;
                }

                byte[] headerData = new byte[4 + headerLength];
                Array.Copy(data, 0, headerData, 0, 4 + headerLength);
                MessageHeader header = MessageHeader.Deserialize(headerData);

                int headerSize = 4 + headerLength;
                int bodySize = data.Length - headerSize;
                byte[] body = new byte[bodySize];

                if (bodySize > 0)
                    Array.Copy(data, headerSize, body, 0, bodySize);

                Debug.Log($"[GameManager] Received message type: {header.messageType}");

                switch (header.messageType)
                {
                    case MessageType.LoginResponse:
                        HandleLoginResponse(body);
                        break;

                    case MessageType.LobbyState:
                        HandleLobbyState(body);
                        break;

                    case MessageType.ChatMessage:
                        HandleChatMessage(body);
                        break;

                    case MessageType.PlayerJoined:
                        HandlePlayerJoined(body);
                        break;

                    case MessageType.PlayerLeft:
                        HandlePlayerLeft(body);
                        break;

                    case MessageType.GameStart:
                        HandleGameStart(body);
                        break;

                    default:
                        Debug.LogWarning($"[GameManager] Unknown message type: {header.messageType}");
                        break;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameManager] Message error: {e.Message}\n{e.StackTrace}");
            }
        }

        void HandleLoginResponse(byte[] data)
        {
            var response = LoginResponseMessage.Deserialize(data);

            if (response.success)
            {
                _localPlayerId = response.playerId;
                Debug.Log($"[GameManager] Login successful! Player ID: {_localPlayerId}");
            }
            else
            {
                Debug.LogError($"[GameManager] Login failed: {response.errorMessage}");
                _loginUI?.ShowError(response.errorMessage ?? "Ошибка входа");
                SetState(State.Login);
            }
        }

        void HandleLobbyState(byte[] data)
        {
            var lobby = LobbyStateMessage.Deserialize(data);
            Debug.Log($"[GameManager] Lobby players: {string.Join(", ", lobby.players)}");
            _lobbyUI?.UpdatePlayerList(lobby.players);
            SetState(State.Lobby);
        }

        void HandleChatMessage(byte[] data)
        {
            var chat = ChatMessage.Deserialize(data);
            Debug.Log($"[GameManager] Chat received: {chat.playerName}: {chat.message}");
            _lobbyUI?.AddChatMessage(chat.playerName, chat.message);
        }

        void HandlePlayerJoined(byte[] data)
        {
            var msg = PlayerJoinedMessage.Deserialize(data);
            _lobbyUI?.AddChatMessage("", $"{msg.playerName} присоединился");
        }

        void HandlePlayerLeft(byte[] data)
        {
            var msg = PlayerLeftMessage.Deserialize(data);
            _lobbyUI?.AddChatMessage("", $"{msg.playerName} вышел");
        }

        void HandleGameStart(byte[] data)
        {
            var gameStart = GameStartMessage.Deserialize(data);
            Debug.Log($"[GameManager] Game starting! Players: {string.Join(", ", gameStart.playerIds)}");
            SetState(State.InGame);
        }

        void OnDisconnected()
        {
            Debug.Log("[GameManager] Disconnected");
            SetState(State.Login);
            _loginUI?.ShowError("Соединение потеряно");
        }

        void OnError(string error)
        {
            Debug.LogError($"[GameManager] Network error: {error}");
            SetState(State.Login);
            _loginUI?.ShowError($"Ошибка: {error}");
        }

        public void SendChatMessage(string message)
        {
            Debug.Log($"[GameManager] Sending chat: {message}");
            var chatMsg = new ChatMessage
            {
                playerId = _localPlayerId,
                playerName = _playerName,
                message = message
            };
            _gameClient.SendMessage(chatMsg.Serialize());
        }

        void SetState(State state)
        {
            _currentState = state;

            // Поиск панели логина
            if (_loginPanel == null)
            {
                GameObject canvas = GameObject.Find("Canvas");
                if (canvas != null)
                {
                    Transform panelTransform = canvas.transform.Find("LoginPanel");
                    if (panelTransform != null)
                        _loginPanel = panelTransform.gameObject;
                }
            }

            if (_loginPanel != null)
                _loginPanel.SetActive(state == State.Login || state == State.Connecting);

            // Управление лобби
            if (_lobbyUI == null)
            {
                _lobbyUI = GetComponent<Client.UI.LobbyUI>();
                if (_lobbyUI == null)
                    _lobbyUI = gameObject.AddComponent<Client.UI.LobbyUI>();
            }

            // Всегда инициализируем перед показом
            _lobbyUI.Initialize();

            switch (state)
            {
                case State.Lobby:
                    _lobbyUI.Show();
                    break;
                default:
                    _lobbyUI.Hide();
                    break;
            }

            if (_player != null)
                _player.enabled = (state == State.InGame);

            Debug.Log($"[GameManager] State: {state}");
        }

        void OnDestroy()
        {
            _gameClient?.Disconnect();
        }
    }
}