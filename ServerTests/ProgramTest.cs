using System;
using System.Threading;
using System.Threading.Tasks;
using Shared.Configs;
using Server.Network;
using Server.Services;
using Xunit;

namespace ServerTests
{
    public class ProgramTest
    {
        private string _configPath = "test-config.json";

        [Fact]
        public void Test_DiscoveryWorks()
        {
            Assert.True(true);
        }

        [Fact]
        public void LoadConfig_WithValidPath_ReturnsCorrectData()
        {
            SVars.LoadFromJson(_configPath);
            var config = SVars.Instance;

            Assert.Equal("192.168.0.101", config.Ip);
            Assert.Equal(8921, config.Port);
            Assert.Equal(70, config.MaxPlayers);
            Assert.Equal(40, config.TickRate);
            Assert.Equal("VGVzdF9zZXJ2ZXIx", config.ConnectionKey);
        }

        [Fact]
        public void LoadConfig_WithInvalidPath_ReturnsDefaultValues()
        {
            SVars.LoadFromJson("test_incorrect_config.json");
            var config = SVars.Instance;

            Assert.Equal("0.0.0.0", config.Ip);
            Assert.Equal(7777, config.Port);
            Assert.Equal(100, config.MaxPlayers);
            Assert.Equal(30, config.TickRate);
            Assert.Equal("", config.ConnectionKey);
        }

        [Fact]
        public void LoadConfig_WithEmptyFile_UsesDefaultValues()
        {
            var empty_configPath = "empty-config.json";
            System.IO.File.WriteAllText(empty_configPath, "{}");

            SVars.LoadFromJson(empty_configPath);
            var config = SVars.Instance;

            Assert.Equal("0.0.0.0", config.Ip);
            Assert.Equal(7777, config.Port);
            Assert.Equal(100, config.MaxPlayers);
            Assert.Equal(30, config.TickRate);
            Assert.Equal("", config.ConnectionKey);

            System.IO.File.Delete(empty_configPath);
        }

        [Fact]
        public void LoadGameServer_WithValidConfig_ReturnNotNull()
        {
            SVars.LoadFromJson(_configPath);
            var config = SVars.Instance;

            var server = new GameServer(config);

            Assert.NotNull(server);
        }

        [Fact]
        public void LoadPlayerManager_WithValidGameServer_ReturnNotNull()
        {
            SVars.LoadFromJson(_configPath);
            var config = SVars.Instance;

            var server = new GameServer(config);
            var playerManager = new PlayerManager(server);

            Assert.NotNull(playerManager);
        }

        [Fact]
        public void StartGameServer_StartAndStop_DoesNotThrowException()
        {
            SVars.LoadFromJson(_configPath);
            var config = SVars.Instance;
            var server = new GameServer(config);

            var exception = Record.Exception(() =>
            {
                server.Start();
                Thread.Sleep(100); // Даем серверу немного времени, чтобы запуститься
                server.Stop();
            });

            Assert.Null(exception);
        }
    }
}