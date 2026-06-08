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
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            Console.CancelKeyPress += OnCancelKeyPress;

            try
            {
                Console.WriteLine("=== Stellar Station v.0 ===");
                Console.WriteLine("Loading configuration...");

                SVars.LoadFromJson("config.json");
                var config = SVars.Instance;

                _server = new GameServer(config);
                _playerManager = new PlayerManager(_server);

                _server.Start();

                Console.WriteLine($"Server started on {config.Ip}:{config.Port}");
                Console.WriteLine($"Max players: {config.MaxPlayers}");
                Console.WriteLine($"Tick rate: {config.TickRate} TPS");
                Console.WriteLine("Press Ctrl+C to stop");

                while (_isRunning)
                {
                    Thread.Sleep(100);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FATAL] Failed to start server: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");

                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
            }
            finally
            {
                _server?.Stop();
                Console.WriteLine("[Program] Server stopped");
            }
        }

        /// <summary>
        /// Обработчик необработанных исключений в домене приложения
        /// Срабатывает, когда исключение не поймано ни в одном try-catch
        /// </summary>
        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception;

            Console.WriteLine($"[FATAL] Unhandled exception caught!");
            Console.WriteLine($"[FATAL] Is terminating: {e.IsTerminating}");
            Console.WriteLine($"[FATAL] Exception: {exception?.Message}");
            Console.WriteLine($"[FATAL] Stack trace: {exception?.StackTrace}");

            File.AppendAllText("error.log",
                $"[{DateTime.Now}] FATAL: {exception?.Message}\n{exception?.StackTrace}\n\n");

            if (!e.IsTerminating)
            {
                RestoreCriticalComponents();
            }
        }

        /// <summary>
        /// Обработчик непрослеживаемых исключений в Task (TPL)
        /// </summary>
        private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            Console.WriteLine($"[FATAL] Unobserved Task exception: {e.Exception.Message}");
            Console.WriteLine($"Stack trace: {e.Exception.StackTrace}");

            e.SetObserved();

            File.AppendAllText("error.log",
                $"[{DateTime.Now}] TASK ERROR: {e.Exception.Message}\n{e.Exception.StackTrace}\n\n");
        }

        /// <summary>
        /// Обработчик Ctrl+C для graceful shutdown
        /// </summary>
        private static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true; // Отменяем немедленное завершение
            _isRunning = false;
            Console.WriteLine("\n[Program] Shutting down gracefully...");
        }

        /// <summary>
        /// Попытка восстановить критические компоненты после ошибки
        /// </summary>
        private static void RestoreCriticalComponents()
        {
            try
            {
                Console.WriteLine("[RECOVERY] Attempting to restore server components...");

                // Перезапускаем сервер, если он упал
                if (_server == null)
                {
                    var config = SVars.Instance;
                    _server = new GameServer(config);
                    _playerManager = new PlayerManager(_server);
                    _server.Start();
                    Console.WriteLine("[RECOVERY] Server restarted successfully");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RECOVERY] Failed to restore: {ex.Message}");
            }
        }
    }
}