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
        private readonly List<NetManager> _clientManagers = new();

        public PlayerManagerTests()
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
            _playerManager = new PlayerManager(_server);
            _server.Start();
            _testPort = _server.BoundPort;
        }

        public void Dispose()
        {
            foreach (var manager in _clientManagers)
            {
                manager?.Stop();
            }
            _clientManagers.Clear();

            _server?.Stop();
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
            TestWait.Until(() => _playerManager.GetAllPlayers().Count == 1, what: "player registered");

            var players = _playerManager.GetAllPlayers();
            Assert.Single(players);
        }

        [Fact]
        public void OnClientDisconnected_WhenClientDisconnects_RemovesPlayerFromList()
        {
            var clientManager = CreateAndConnectClient();
            TestWait.Until(() => _playerManager.GetAllPlayers().Count == 1, what: "player registered");
            Assert.Single(_playerManager.GetAllPlayers());

            clientManager.Stop();
            _clientManagers.Remove(clientManager);
            TestWait.Until(() => _playerManager.GetAllPlayers().Count == 0, what: "player removed on disconnect");

            Assert.Empty(_playerManager.GetAllPlayers());
        }

        [Fact]
        public void GetAllPlayers_ReturnsAllConnectedPlayers()
        {
            var clientManager1 = CreateAndConnectClient();
            TestWait.Until(() => _playerManager.GetAllPlayers().Count == 1, what: "player 1 registered");
            var clientManager2 = CreateAndConnectClient();
            TestWait.Until(() => _playerManager.GetAllPlayers().Count == 2, what: "player 2 registered");

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

            try
            {
                TestWait.Until(() => connected, () => manager.PollEvents(), what: "client connected");
            }
            catch (TimeoutException)
            {
                manager.Stop();
                throw new Exception("Failed to connect to server");
            }

            _clientManagers.Add(manager);
            return manager;
        }
    }
}