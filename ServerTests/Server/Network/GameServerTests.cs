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

        public GameServerTests()
        {
            _config = new SVars
            {
                Ip = "127.0.0.1",
                Port = 0,
                MaxPlayers = 10,
                TickRate = 30,
                ConnectionKey = "TestKey",
                MapPath = ""
            };

            _server = new GameServer(_config);
        }

        public void Dispose()
        {
            _server?.Stop();
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
            int port = _server.BoundPort;
            TestWait.Until(() => IsUdpPortInUse(port), what: "port bound after Start");

            Assert.True(IsUdpPortInUse(port), $"Server should be listening on port {port}");

            _server.Stop();
            TestWait.Until(() => !IsUdpPortInUse(port), what: "port released after Stop");

            Assert.False(IsUdpPortInUse(port), $"Port {port} should be released after stop");
        }

        [Fact]
        public void StartAndStop_NoExceptions_WorksCorrectly()
        {
            var exception = Record.Exception(() =>
            {
                _server.Start();
                int port = _server.BoundPort;
                TestWait.Until(() => IsUdpPortInUse(port), what: "port bound");
                Assert.True(IsUdpPortInUse(port));

                _server.Stop();
                TestWait.Until(() => !IsUdpPortInUse(port), what: "port released");
                Assert.False(IsUdpPortInUse(port));
            });

            Assert.Null(exception);
        }

        [Fact]
        public void MultipleStartStop_WorksCorrectly()
        {
            var exception = Record.Exception(() =>
            {
                _server.Start();
                int port1 = _server.BoundPort;
                TestWait.Until(() => IsUdpPortInUse(port1), what: "port1 bound");
                Assert.True(IsUdpPortInUse(port1));
                _server.Stop();
                TestWait.Until(() => !IsUdpPortInUse(port1), what: "port1 released");
                Assert.False(IsUdpPortInUse(port1));

                _server.Start();
                int port2 = _server.BoundPort;
                TestWait.Until(() => IsUdpPortInUse(port2), what: "port2 bound");
                Assert.True(IsUdpPortInUse(port2));
                _server.Stop();
                TestWait.Until(() => !IsUdpPortInUse(port2), what: "port2 released");
                Assert.False(IsUdpPortInUse(port2));
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

            var peer = CreateConnectedPeer();
            var client = new ClientConnection(peer, 1);

            _server.UpdatePlayerPosition(client, 10.5f, 20.3f, 5, 2);

            Assert.Equal(10.5f, client.X);
            Assert.Equal(20.3f, client.Y);
            Assert.Equal(5, client.Z);
            Assert.Equal(2, client.Facing);

            CleanupPeer(peer);
            _server.Stop();
        }

        [Fact]
        public void UpdatePlayerPosition_NullClient_ThrowsNullReferenceException()
        {
            _server.Start();

            Assert.Throws<NullReferenceException>(() =>
                _server.UpdatePlayerPosition(null!, 10, 20, 0, 0));

            _server.Stop();
        }

        [Fact]
        public void SendToClient_ValidClientAndMessage_SendsWithoutError()
        {
            _server.Start();

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
        }

        [Fact]
        public void SendToClient_NullClient_ThrowsNullReferenceException()
        {
            _server.Start();

            var intent = new MoveIntent();

            Assert.Throws<NullReferenceException>(() =>
                _server.SendToClient(null!, intent));

            _server.Stop();
        }

        [Fact]
        public void BroadcastToAll_WithPredicate_SendsToFilteredClients()
        {
            _server.Start();

            var snapshot = new WorldSnapshot
            {
                ServerTick = 100,
                Entities = Array.Empty<EntitySnapshot>()
            };

            var exception = Record.Exception(() =>
                _server.BroadcastToAll(snapshot, client => client.ConnectionId == 1));

            Assert.Null(exception);

            _server.Stop();
        }

        [Fact]
        public void BroadcastToAll_WithoutPredicate_SendsToAll()
        {
            _server.Start();

            var snapshot = new WorldSnapshot
            {
                ServerTick = 100,
                Entities = Array.Empty<EntitySnapshot>()
            };

            var exception = Record.Exception(() =>
                _server.BroadcastToAll(snapshot));

            Assert.Null(exception);

            _server.Stop();
        }

        [Fact]
        public void GameServer_WithCustomConfig_UsesConfigValues()
        {
            Assert.Equal(0, _config.Port);
            Assert.Equal(10, _config.MaxPlayers);
            Assert.Equal(30, _config.TickRate);
            Assert.Equal("TestKey", _config.ConnectionKey);
        }

        [Fact]
        public void PlayerConnect_RegistersEntity_DisconnectRemoves()
        {
            _server.Start();

            ClientConnection? spawned = null;
            _server.OnClientConnected += c => spawned = c;

            // Свой клиент-менеджер (держим и пампим сами) — чтобы connect/disconnect-пакеты реально ушли.
            var clientListener = new EventBasedNetListener();
            var clientManager = new NetManager(clientListener);
            clientManager.Start();
            var peer = clientManager.Connect("127.0.0.1", _server.BoundPort, _config.ConnectionKey);

            // Регистрация в _entities идёт в OnPeerConnected на серверном PollEvents (GameLoop) — ждём.
            // Ждём И spawned: колбэк OnClientConnected дренируется из _mainThreadActions ПОСЛЕ PollEvents того же
            // тика — окно, где EntityCount уже 1, а spawned ещё null (ловилось как флейк ~1/10).
            TestWait.Until(() => spawned != null && _server.EntityCount >= 1,
                () => clientManager.PollEvents(), what: "player registered on server");

            Assert.Equal(1, _server.EntityCount);
            Assert.NotNull(spawned);
            Assert.True(spawned!.PlayerNetId > 0, "PlayerNetId выдан аллокатором (>0)");

            // Дисконнект: клиент шлёт disconnect-пакет, пампим до Remove из реестра на сервере.
            peer.Disconnect();
            TestWait.Until(() => _server.EntityCount == 0, () => clientManager.PollEvents(), what: "player removed on disconnect");
            Assert.Equal(0, _server.EntityCount);

            clientManager.Stop();
            _server.Stop();
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

            clientManager.Connect("127.0.0.1", _server.BoundPort, _config.ConnectionKey);

            try
            {
                TestWait.Until(() => connectedPeer != null, () => clientManager.PollEvents(), what: "peer connected");
            }
            catch (TimeoutException)
            {
                clientManager.Stop();
                throw new Exception($"Failed to connect to server on port {_server.BoundPort}");
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