using System;
using System.Threading;
using System.Threading.Tasks;
using Shared.Configs;
using Server.Network;
using Server.Services;
using Xunit;

namespace ServerTests.Server
{
    public class ProgramTests
    {
        private string _configPath = "test-config.json";

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