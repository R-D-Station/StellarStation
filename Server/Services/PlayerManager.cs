using Server.Network;
using Shared.Configs;
using Shared.Messages.Core;
using Shared.Messages.Player;

namespace Server.Services
{
    /// <summary>Управляет жизненным циклом игроков: спавн при входе, очистка при выходе.</summary>
    public class PlayerManager
    {
        private readonly GameServer _server;
        private readonly Dictionary<int, ClientConnection> _players;
        private readonly object _playersLock = new();

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
            client.Mover = new Shared.Simulation.Blocks.BlockMoverState(
                _server.BlockSpawnX, _server.BlockSpawnY, _server.BlockSpawnZ);
            client.X = client.Mover.X;
            client.Y = client.Mover.Z;
            client.Z = (int)MathF.Floor(client.Mover.Y);
            client.Facing = 0;

            lock (_playersLock)
                _players[client.ConnectionId] = client;

            Console.WriteLine($"[PlayerManager] Player #{client.ConnectionId} spawned at ({client.X}, {client.Y}, z{client.Z})");

            // NetId (узнать себя в WorldSnapshot) + серверный TickRate (клиент тикает на нём → инвариант
            // tickRate==TickRate по построению). SVars.Instance == _config GameLoop (Program.cs: GameServer(SVars.Instance)).
            _server.SendToClient(client, new LoginResponse
            {
                NetId = client.PlayerNetId,
                // Clamp к [1,255]: TickRate на проводе — byte. Sane-rate (≤60) проходит точно; абсурдный конфиг
                // не усечётся в мусор (напр. 256→0 → клиент тикал бы на 1 TPS). >255 TPS не поддерживается.
                TickRate = (byte)Math.Clamp(SVars.Instance.TickRate, 1, byte.MaxValue),
                ShapesMode = _server.BlockShapesMode,
                ZoneFadeDistance = SVars.Instance.ZoneFadeDistance,
                ZoneFadeVertical = SVars.Instance.ZoneFadeVertical
            });

            _server.StreamBlockSectionsToClient(client);

            // Catch-up новичку: кто уже в мире, кроме него самого (он уже в _players с L31).
            foreach (var p in _players.Values)
                if (p != client)
                    _server.SendToClient(client, new PlayerJoined { NetId = p.PlayerNetId });

            // Анонс новичка остальным (себе не шлём).
            _server.BroadcastToAll(new PlayerJoined { NetId = client.PlayerNetId }, c => c != client);
        }

        private void OnClientDisconnected(ClientConnection client)
        {
            // PlayerLeft ДО Remove. Пир уже снят из GameServer._clients → своё PlayerLeft не получит (корректно).
            _server.BroadcastToAll(new PlayerLeft { NetId = client.PlayerNetId });
            lock (_playersLock)
                _players.Remove(client.ConnectionId);
            Console.WriteLine($"[PlayerManager] Player #{client.ConnectionId} left");
        }

        public IReadOnlyCollection<ClientConnection> GetAllPlayers()
        {
            lock (_playersLock)
                return _players.Values.ToArray();
        }
    }
}