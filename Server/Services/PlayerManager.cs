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
            if (SVars.Instance.BlocksWorld)
            {
                client.Mover = new Shared.Simulation.Blocks.BlockMoverState(
                    _server.BlockSpawnX, _server.BlockSpawnY, _server.BlockSpawnZ); // Y — высота (оси Unity)
                client.X = client.Mover.X;
                client.Y = client.Mover.Z;                     // legacy-глубина плана
                client.Z = (int)MathF.Floor(client.Mover.Y);   // legacy-«этаж» = целый блок высоты
            }
            else
            {
                client.X = _server.SpawnX;
                client.Y = _server.SpawnY;
                client.Z = _server.SpawnZ;
            }
            client.Facing = 0;

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
                BlocksWorld = SVars.Instance.BlocksWorld,
                ShapesMode = _server.BlockShapesMode
            });

            if (!SVars.Instance.BlocksWorld)
            {
                // Стрим карты по чанкам (2.3b): вместо всей карты шлём окружение игрока. СИНХРОННО на логине — чтобы
                // чанки вокруг были у клиента ДО первого шага (иначе незагруженный чанк = Space = проходим → предикт
                // сквозь ещё-не-пришедшие стены). Дальше ProcessStreaming (GameLoop) досылает/выгружает по движению.
                _server.StreamChunksToClient(client);
                // Текущее состояние открытых дверей (карта статична, двери — рантайм).
                _server.SendOpenDoors(client);
            }
            else
            {
                // Блок-мир (фаза C): синхронный первичный стрим окна секций — до первого шага игрока.
                _server.StreamBlockSectionsToClient(client);
            }

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
            _players.Remove(client.ConnectionId);
            Console.WriteLine($"[PlayerManager] Player #{client.ConnectionId} left");
        }

        public IReadOnlyCollection<ClientConnection> GetAllPlayers() => _players.Values;
    }
}