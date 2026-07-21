using System;
using System.Threading;
using System.Net.Sockets;
using System.Net;
using Xunit;
using LiteNetLib;
using Shared.Configs;
using Shared.Messages.Core;
using Server.Network;

namespace ServerTests.Server.Network
{
    public class GameServerTests : IDisposable
    {
        private readonly SVars _config;
        private readonly GameServer _server;
        private readonly int _testPort;
        private static int _portCounter = 7778;
        private static readonly object _portLock = new object();

        public GameServerTests()
        {
            // Уникальный порт на каждый тест, чтобы не конфликтовали.
            lock (_portLock)
            {
                _testPort = _portCounter++;
                if (_testPort > 7900) _portCounter = 7778;
            }

            _config = new SVars
            {
                Ip = "127.0.0.1",
                Port = _testPort,
                MaxPlayers = 10,
                TickRate = 30,
                ConnectionKey = $"TestKey_{_testPort}"
            };

            _server = new GameServer(_config);
        }

        public void Dispose()
        {
            _server?.Stop();
            Thread.Sleep(100);
        }

        [Fact]
        public void Constructor_WithValidConfig_CreatesInstance()
        {
            Assert.NotNull(_server);
        }

        [Fact]
        public void Start_ValidConfig_StartsSuccessfully()
        {
            _server.Start();
            Thread.Sleep(50);

            Assert.True(IsUdpPortInUse(_testPort), $"Server should be listening on port {_testPort}");

            _server.Stop();
            Thread.Sleep(50);

            Assert.False(IsUdpPortInUse(_testPort), $"Port {_testPort} should be released after stop");
        }

        [Fact]
        public void StartAndStop_NoExceptions_WorksCorrectly()
        {
            var exception = Record.Exception(() =>
            {
                _server.Start();
                Thread.Sleep(50);
                Assert.True(IsUdpPortInUse(_testPort));

                _server.Stop();
                Thread.Sleep(50);
                Assert.False(IsUdpPortInUse(_testPort));
            });

            Assert.Null(exception);
        }

        [Fact]
        public void MultipleStartStop_WorksCorrectly()
        {
            var exception = Record.Exception(() =>
            {
                _server.Start();
                Thread.Sleep(50);
                Assert.True(IsUdpPortInUse(_testPort));
                _server.Stop();
                Thread.Sleep(100);
                Assert.False(IsUdpPortInUse(_testPort));

                _server.Start();
                Thread.Sleep(50);
                Assert.True(IsUdpPortInUse(_testPort));
                _server.Stop();
                Thread.Sleep(100);
                Assert.False(IsUdpPortInUse(_testPort));
            });

            Assert.Null(exception);
        }

        [Fact]
        public void Stop_BeforeStart_NoException()
        {
            var exception = Record.Exception(() => _server.Stop());
            Assert.Null(exception);
        }

        [Fact]
        public void Events_CanBeSubscribed()
        {
            var exception = Record.Exception(() =>
            {
                _server.OnClientConnected += (client) => { };
                _server.OnClientDisconnected += (client) => { };
                _server.OnMoveIntentReceived += (client, intent) => { };
            });

            Assert.Null(exception);
        }

        [Fact]
        public void UpdatePlayerPosition_ValidClient_UpdatesCoordinates()
        {
            _server.Start();
            Thread.Sleep(50);

            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1);

            _server.UpdatePlayerPosition(client, 10.5f, 20.3f, 5, 2);

            Assert.Equal(10.5f, client.X);
            Assert.Equal(20.3f, client.Y);
            Assert.Equal(5, client.Z);
            Assert.Equal(2, client.Facing);

            CleanupPeer(peer);
            _server.Stop();
            Thread.Sleep(50);
        }

        [Fact]
        public void UpdatePlayerPosition_NullClient_ThrowsNullReferenceException()
        {
            _server.Start();
            Thread.Sleep(50);

            Assert.Throws<NullReferenceException>(() =>
                _server.UpdatePlayerPosition(null!, 10, 20, 0, 0));

            _server.Stop();
            Thread.Sleep(50);
        }

        [Fact]
        public void SendToClient_ValidClientAndMessage_SendsWithoutError()
        {
            _server.Start();
            Thread.Sleep(50);

            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1);
            var intent = new MoveIntent
            {
                Direction = IntentDirection.North,
                Sprint = false,
                Sequence = 1
            };

            var exception = Record.Exception(() =>
                _server.SendToClient(client, intent));

            Assert.Null(exception);

            CleanupPeer(peer);
            _server.Stop();
            Thread.Sleep(50);
        }

        [Fact]
        public void SendToClient_NullClient_ThrowsNullReferenceException()
        {
            _server.Start();
            Thread.Sleep(50);

            var intent = new MoveIntent();

            Assert.Throws<NullReferenceException>(() =>
                _server.SendToClient(null!, intent));

            _server.Stop();
            Thread.Sleep(50);
        }

        [Fact]
        public void BroadcastToAll_WithPredicate_SendsToFilteredClients()
        {
            _server.Start();
            Thread.Sleep(50);

            var snapshot = new WorldSnapshot
            {
                ServerTick = 100,
                Entities = Array.Empty<EntitySnapshot>()
            };

            var exception = Record.Exception(() =>
                _server.BroadcastToAll(snapshot, client => client.ConnectionId == 1));

            Assert.Null(exception);

            _server.Stop();
            Thread.Sleep(50);
        }

        [Fact]
        public void BroadcastToAll_WithoutPredicate_SendsToAll()
        {
            _server.Start();
            Thread.Sleep(50);

            var snapshot = new WorldSnapshot
            {
                ServerTick = 100,
                Entities = Array.Empty<EntitySnapshot>()
            };

            var exception = Record.Exception(() =>
                _server.BroadcastToAll(snapshot));

            Assert.Null(exception);

            _server.Stop();
            Thread.Sleep(50);
        }

        [Fact]
        public void GameServer_WithCustomConfig_UsesConfigValues()
        {
            Assert.Equal(_testPort, _config.Port);
            Assert.Equal(10, _config.MaxPlayers);
            Assert.Equal(30, _config.TickRate);
            Assert.Equal($"TestKey_{_testPort}", _config.ConnectionKey);
        }

        [Fact]
        public void PlayerConnect_RegistersEntity_DisconnectRemoves()
        {
            _server.Start();
            Thread.Sleep(50);

            ClientConnection? spawned = null;
            _server.OnClientConnected += c => spawned = c;

            // Свой клиент-менеджер (держим и пампим сами) — чтобы connect/disconnect-пакеты реально ушли.
            var clientListener = new EventBasedNetListener();
            var clientManager = new NetManager(clientListener);
            clientManager.Start();
            var peer = clientManager.Connect("127.0.0.1", _testPort, _config.ConnectionKey);

            // Регистрация в _entities идёт в OnPeerConnected на серверном PollEvents (GameLoop) — ждём.
            // Ждём И spawned: колбэк OnClientConnected дренируется из _mainThreadActions ПОСЛЕ PollEvents того же
            // тика — окно, где EntityCount уже 1, а spawned ещё null (ловилось как флейк ~1/10).
            for (int i = 0; i < 200 && (spawned == null || _server.EntityCount == 0); i++) { clientManager.PollEvents(); Thread.Sleep(10); }

            Assert.Equal(1, _server.EntityCount);
            Assert.NotNull(spawned);
            Assert.True(spawned!.PlayerNetId > 0, "PlayerNetId выдан аллокатором (>0)");

            // Дисконнект: клиент шлёт disconnect-пакет, пампим до Remove из реестра на сервере.
            peer.Disconnect();
            for (int i = 0; i < 200 && _server.EntityCount > 0; i++) { clientManager.PollEvents(); Thread.Sleep(10); }
            Assert.Equal(0, _server.EntityCount);

            clientManager.Stop();
            _server.Stop();
            Thread.Sleep(50);
        }

        private bool IsUdpPortInUse(int port)
        {
            try
            {
                using var udp = new UdpClient();
                udp.Client.Bind(new IPEndPoint(IPAddress.Loopback, port));
                udp.Close();
                return false;
            }
            catch (SocketException)
            {
                return true; // bind упал — порт занят
            }
        }

        private NetPeer CreateConnectedPeer()
        {
            var clientListener = new EventBasedNetListener();
            var clientManager = new NetManager(clientListener);
            clientManager.Start();

            NetPeer? connectedPeer = null;
            bool connected = false;

            clientListener.PeerConnectedEvent += peer =>
            {
                connectedPeer = peer;
                connected = true;
            };

            clientManager.Connect("127.0.0.1", _testPort, _config.ConnectionKey);

            for (int i = 0; i < 100 && !connected; i++)
            {
                clientManager.PollEvents();
                Thread.Sleep(10);
            }

            if (connectedPeer == null)
            {
                clientManager.Stop();
                throw new Exception($"Failed to connect to server on port {_testPort}");
            }

            return connectedPeer;
        }

        private void CleanupPeer(NetPeer peer)
        {
            peer?.Disconnect();
            Thread.Sleep(50);
        }
    }
}