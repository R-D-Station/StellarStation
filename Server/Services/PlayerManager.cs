using Server.Network;
using Shared.Messages.Core;
using Shared.Messages.Player;

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
            // ƒвижение обрабатываетс€ в GameServer.ProcessIntents (один шаг за тик).
            // PlayerManager отвечает только за спавн/деспавн игроков.
        }

        private void OnClientConnected(ClientConnection client)
        {
            client.X = 0;
            client.Y = 0;
            client.Z = 0;
            client.Facing = 0;

            _players[client.ConnectionId] = client;

            Console.WriteLine($"[PlayerManager] Player #{client.ConnectionId} spawned at (0, 0, 0)");

            // —ообщаем клиенту его NetId, чтобы он мог отличить свою сущность
            // в WorldSnapshot от чужих (нужно дл€ предсказани€/reconciliation).
            _server.SendToClient(client, new LoginResponse { NetId = client.PlayerNetId });

            // TODO: отправл€ем всем остальным игрокам (администрации), что новый игрок присоединилс€
        }

        private void OnClientDisconnected(ClientConnection client)
        {
            _players.Remove(client.ConnectionId);
            Console.WriteLine($"[PlayerManager] Player #{client.ConnectionId} left");
        }

        /// <summary>
        /// ћетод дл€ получени€ всех игроков (дл€ отладки)
        /// </summary>
        public IReadOnlyCollection<ClientConnection> GetAllPlayers() => _players.Values;
    }
}