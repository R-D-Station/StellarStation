using Server.Network;
using Shared.Messages.Core;

namespace Server.Services
{
    public class PlayerManager
    {
        private readonly GameServer _server;
        private readonly Dictionary<int, ClientConnection> _players;

        public PlayerManager(GameServer server)
        {
            _server = server;
            _players = new Dictionary<int, ClientConnection>();

            _server.OnClientConnected += OnClientConnected;
            _server.OnClientDisconnected += OnClientDisconnected;
            _server.OnMoveIntentReceived += OnMoveIntentReceived;
        }

        private void OnClientConnected(ClientConnection client)
        {
            client.X = 0;
            client.Y = 0;
            client.Z = 0;
            client.Facing = 0;

            _players[client.ConnectionId] = client;

            Console.WriteLine($"[PlayerManager] Player #{client.ConnectionId} spawned at (0, 0, 0)");

            // TODO: отправляем всем остальным игрокам (администрации), что новый игрок присоединился
        }

        private void OnClientDisconnected(ClientConnection client)
        {
            _players.Remove(client.ConnectionId);
            Console.WriteLine($"[PlayerManager] Player #{client.ConnectionId} left");
        }

        private void OnMoveIntentReceived(ClientConnection client, MoveIntent intent)
        {
            // Обновляем позицию игрока на основе намерения
            float speed = intent.Sprint ? 0.2f : 0.1f;
            float newX = client.X;
            float newY = client.Y;

            switch (intent.Direction)
            {
                case IntentDirection.North:
                    newY += speed;
                    break;
                case IntentDirection.South:
                    newY -= speed;
                    break;
                case IntentDirection.East:
                    newX += speed;
                    break;
                case IntentDirection.West:
                    newX -= speed;
                    break;
                default:
                    break;
            }

            // TODO: спорная валидация границ (простая)
            newX = Math.Clamp(newX, -20, 20);
            newY = Math.Clamp(newY, -20, 20);

            _server.UpdatePlayerPosition(client, newX, newY, client.Z, client.Facing);

            Console.WriteLine($"[PlayerManager] Player #{client.ConnectionId} moved to ({newX:F2}, {newY:F2})");

            // TODO: отправляем подтверждение движения обратно клиенту (для reconciliation)
        }

        /// <summary>
        /// Метод для получения всех игроков (для отладки)
        /// </summary>
        public IReadOnlyCollection<ClientConnection> GetAllPlayers() => _players.Values;
    }
}