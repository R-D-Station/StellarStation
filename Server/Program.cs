using System;
using System.Threading;
using System.Threading.Tasks;
using Shared.Configs;
using Server.Network;
using Server.Services;

namespace Server
{
    class Program
    {
        private static GameServer? _server;
        private static PlayerManager? _playerManager;
        private static bool _isRunning = true;

        static async Task Main(string[] args)
        {
            Console.WriteLine("=== Stellar Station v.0 ===");
            Console.WriteLine("Loading configuration...");

            SVars.LoadFromJson("config.json");
            var config = SVars.Instance;

            _server = new GameServer(config);
            _playerManager = new PlayerManager(_server);

            // Обработка Ctrl+C для graceful shutdown
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                _isRunning = false;
                Console.WriteLine("\n[Program] Shutting down...");
            };

            _server.Start();

            Console.WriteLine($"Server started on {config.Ip}:{config.Port}");
            Console.WriteLine($"Max players: {config.MaxPlayers}");
            Console.WriteLine($"Tick rate: {config.TickRate} TPS");
            Console.WriteLine("Press Ctrl+C to stop");

            while (_isRunning)
            {
                await Task.Delay(100);
            }

            _server.Stop();
            Console.WriteLine("[Program] Server stopped");
        }
    }
}