using Server.Network;
using Shared.Messages.Core;
using Shared.Messages.Player;

namespace Server.Services
{
    /// <summary>Управляет жизненным циклом игроков: спавн при входе, очистка при выходе.</summary>
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
            // Движение обрабатывает GameServer.ProcessIntents; здесь — только вход/выход игроков.
        }

        private void OnClientConnected(ClientConnection client)
        {
            client.X = _server.SpawnX;
            client.Y = _server.SpawnY;
            client.Z = _server.SpawnZ;
            client.Facing = 0;

            _players[client.ConnectionId] = client;

            Console.WriteLine($"[PlayerManager] Player #{client.ConnectionId} spawned at ({client.X}, {client.Y}, z{client.Z})");

            // Сообщаем клиенту его NetId — чтобы он узнавал себя в WorldSnapshot.
            _server.SendToClient(client, new LoginResponse { NetId = client.PlayerNetId });

            // Карта целиком сразу после логина (позже — стриминг по PVS).
            _server.SendMap(client);
            // Текущее состояние открытых дверей (карта статична, двери — рантайм).
            _server.SendOpenDoors(client);

            // TODO: разослать новичка остальным игрокам (спавн)
        }

        private void OnClientDisconnected(ClientConnection client)
        {
            _players.Remove(client.ConnectionId);
            Console.WriteLine($"[PlayerManager] Player #{client.ConnectionId} left");
        }

        public IReadOnlyCollection<ClientConnection> GetAllPlayers() => _players.Values;
    }
}