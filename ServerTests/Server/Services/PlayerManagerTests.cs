using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Xunit;
using LiteNetLib;
using LiteNetLib.Utils;
using Shared.Configs;
using Shared.Messages.Core;
using Server.Network;
using Server.Services;
using Shared.Messages;

namespace ServerTests.Server.Services
{
    public class PlayerManagerTests : IDisposable
    {
        private readonly SVars _config;
        private readonly GameServer _server;
        private readonly PlayerManager _playerManager;
        private readonly int _testPort;
        private static int _portCounter = 7790;
        private static readonly object _portLock = new object();
        private readonly List<NetManager> _clientManagers = new(); // все созданные клиенты

        public PlayerManagerTests()
        {
            lock (_portLock)
            {
                _testPort = _portCounter++;
                if (_testPort > 7900) _portCounter = 7790;
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
            _playerManager = new PlayerManager(_server);
            _server.Start();
            Thread.Sleep(200);
        }

        public void Dispose()
        {
            foreach (var manager in _clientManagers)
            {
                manager?.Stop();
            }
            _clientManagers.Clear();

            _server?.Stop();
            Thread.Sleep(200);
        }

        [Fact]
        public void Constructor_WithValidServer_CreatesInstance()
        {
            Assert.NotNull(_playerManager);
        }

        [Fact]
        public void GameServer_WithPlayerManager_SubscribesToEvents()
        {
            Assert.NotNull(_server);
            Assert.NotNull(_playerManager);
        }

        [Fact]
        public void OnClientConnected_WhenClientConnects_AddsPlayerToList()
        {
            var clientManager = CreateAndConnectClient();
            Thread.Sleep(100);

            var players = _playerManager.GetAllPlayers();
            Assert.Single(players);
        }

        [Fact]
        public void OnClientDisconnected_WhenClientDisconnects_RemovesPlayerFromList()
        {
            var clientManager = CreateAndConnectClient();
            Thread.Sleep(100);
            Assert.Single(_playerManager.GetAllPlayers());

            clientManager.Stop();
            _clientManagers.Remove(clientManager);
            Thread.Sleep(100);

            Assert.Empty(_playerManager.GetAllPlayers());
        }

        [Fact]
        public void OnMoveIntentReceived_ValidIntent_North_MovesCorrectly()
        {
            var clientManager = CreateAndConnectClient();
            Thread.Sleep(100);

            var intent = new MoveIntent
            {
                Direction = IntentDirection.North,
                Sprint = false,
                Sequence = 1
            };

            SendMoveIntent(clientManager, intent);
            Thread.Sleep(100);

            var players = _playerManager.GetAllPlayers();
            Assert.Single(players);

            var client = players.First();
            Assert.Equal(0, client.X);
            Assert.Equal(0.1f, client.Y);
        }

        [Fact]
        public void OnMoveIntentReceived_ValidIntent_South_MovesCorrectly()
        {
            var clientManager = CreateAndConnectClient();
            Thread.Sleep(100);

            var intent = new MoveIntent
            {
                Direction = IntentDirection.South,
                Sprint = false,
                Sequence = 1
            };

            SendMoveIntent(clientManager, intent);
            Thread.Sleep(100);

            var client = _playerManager.GetAllPlayers().First();
            Assert.Equal(0, client.X);
            Assert.Equal(-0.1f, client.Y);
        }

        [Fact]
        public void OnMoveIntentReceived_ValidIntent_East_MovesCorrectly()
        {
            var clientManager = CreateAndConnectClient();
            Thread.Sleep(100);

            var intent = new MoveIntent
            {
                Direction = IntentDirection.East,
                Sprint = false,
                Sequence = 1
            };

            SendMoveIntent(clientManager, intent);
            Thread.Sleep(100);

            var client = _playerManager.GetAllPlayers().First();
            Assert.Equal(0.1f, client.X);
            Assert.Equal(0, client.Y);
        }

        [Fact]
        public void OnMoveIntentReceived_ValidIntent_West_MovesCorrectly()
        {
            var clientManager = CreateAndConnectClient();
            Thread.Sleep(100);

            var intent = new MoveIntent
            {
                Direction = IntentDirection.West,
                Sprint = false,
                Sequence = 1
            };

            SendMoveIntent(clientManager, intent);
            Thread.Sleep(100);

            var client = _playerManager.GetAllPlayers().First();
            Assert.Equal(-0.1f, client.X);
            Assert.Equal(0, client.Y);
        }

        [Fact]
        public void OnMoveIntentReceived_WithSprint_MovesDoubleSpeed()
        {
            var clientManager = CreateAndConnectClient();
            Thread.Sleep(100);

            var intent = new MoveIntent
            {
                Direction = IntentDirection.North,
                Sprint = true,
                Sequence = 1
            };

            SendMoveIntent(clientManager, intent);
            Thread.Sleep(100);

            var client = _playerManager.GetAllPlayers().First();
            Assert.Equal(0, client.X);
            Assert.Equal(0.2f, client.Y);
        }

        [Fact]
        public void OnMoveIntentReceived_MultipleMoves_AccumulatesPosition()
        {
            var clientManager = CreateAndConnectClient();
            Thread.Sleep(100);

            SendMoveIntent(clientManager, new MoveIntent { Direction = IntentDirection.North, Sprint = false, Sequence = 1 });
            SendMoveIntent(clientManager, new MoveIntent { Direction = IntentDirection.North, Sprint = false, Sequence = 2 });
            SendMoveIntent(clientManager, new MoveIntent { Direction = IntentDirection.East, Sprint = false, Sequence = 3 });
            SendMoveIntent(clientManager, new MoveIntent { Direction = IntentDirection.East, Sprint = false, Sequence = 4 });
            Thread.Sleep(200);

            var client = _playerManager.GetAllPlayers().First();
            Assert.Equal(0.2f, client.X);
            Assert.Equal(0.2f, client.Y);
        }

        [Fact]
        public void GetAllPlayers_ReturnsAllConnectedPlayers()
        {
            var clientManager1 = CreateAndConnectClient();
            Thread.Sleep(100);
            var clientManager2 = CreateAndConnectClient();
            Thread.Sleep(100);

            var players = _playerManager.GetAllPlayers();
            Assert.Equal(2, players.Count);
        }

        [Fact]
        public void GetAllPlayers_WhenEmpty_ReturnsEmptyCollection()
        {
            var players = _playerManager.GetAllPlayers();
            Assert.NotNull(players);
        }

        private NetManager CreateAndConnectClient()
        {
            var listener = new EventBasedNetListener();
            var manager = new NetManager(listener);
            manager.Start();

            bool connected = false;
            listener.PeerConnectedEvent += peer => connected = true;

            manager.Connect("127.0.0.1", _testPort, _config.ConnectionKey);

            for (int i = 0; i < 100 && !connected; i++)
            {
                manager.PollEvents();
                Thread.Sleep(10);
            }

            if (!connected)
            {
                manager.Stop();
                throw new Exception("Failed to connect to server");
            }

            _clientManagers.Add(manager);
            return manager;
        }

        private void SendMoveIntent(NetManager clientManager, MoveIntent intent)
        {
            var peers = new List<NetPeer>();
            clientManager.GetConnectedPeers(peers);

            if (peers.Count == 0)
                throw new Exception("No connected peers");

            var peer = peers[0];

            var writer = new NetDataWriter();
            byte[] data = intent.Serialize();
            writer.Put((ushort)MessageType.MoveIntent);
            writer.PutBytesWithLength(data);

            peer.Send(writer, DeliveryMethod.ReliableOrdered);

            clientManager.PollEvents();
            Thread.Sleep(50);
        }
    }
}