using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Shared.Messages;
using Shared.Messages.Auth;
using Shared.Configs;
using LiteNetLib;
using LiteNetLib.Utils;
using Server.Network;

namespace Server.Services
{
    /// <summary>
    /// Статус сессии авторизации
    /// </summary>
    public enum AuthSessionStatus
    {
        Pending,
        Verified,
        Rejected,
        Expired,
        Error
    }

    /// <summary>
    /// Сессия авторизации игрока
    /// </summary>
    public class AuthSession
    {
        public string Login { get; set; } = string.Empty;
        public ulong Nonce { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public AuthSessionStatus Status { get; set; } = AuthSessionStatus.Pending;
        public NetPeer? Peer { get; set; }
        public ClientConnection? Client { get; set; }

        public bool IsExpired(int lifetimeSeconds)
        {
            return DateTime.UtcNow - CreatedAt > TimeSpan.FromSeconds(lifetimeSeconds);
        }
    }

    /// <summary>
    /// Сервис авторизации игроков
    /// </summary>
    public class AuthService
    {
        private readonly SVars _config;
        private readonly HttpClient _httpClient;
        private readonly ConcurrentDictionary<ulong, AuthSession> _sessions;
        private readonly object _nonceLock = new();
        private ulong _nextNonce = 1;
        private bool _isRunning = true;

        public event Action<AuthSession>? OnAuthSuccess;
        public event Action<AuthSession>? OnAuthFailed;

        public AuthService(SVars config)
        {
            _config = config;
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(_config.AuthTimeoutSeconds + 2);
            _sessions = new ConcurrentDictionary<ulong, AuthSession>();

            // Запускаем очистку старых сессий
            Task.Run(CleanupSessions);
        }

        public void Stop()
        {
            _isRunning = false;
            _httpClient.Dispose();
        }

        /// <summary>
        /// Получить следующий nonce для запроса
        /// </summary>
        private ulong GetNextNonce()
        {
            lock (_nonceLock)
            {
                return _nextNonce++;
            }
        }

        /// <summary>
        /// Начать процесс авторизации
        /// </summary>
        public async Task<AuthSession> AuthenticateAsync(ClientAuthRequest request, NetPeer peer)
        {
            var nonce = GetNextNonce();
            var session = new AuthSession
            {
                Login = request.Login,
                Nonce = nonce,
                CreatedAt = DateTime.UtcNow,
                Peer = peer
            };

            _sessions[nonce] = session;

            try
            {
                Console.WriteLine($"[Auth] Starting auth for '{request.Login}' with nonce {nonce}");

                // Отправляем запрос на сервер авторизации
                var authRequest = new AuthServerRequest
                {
                    Login = request.Login,
                    EncLogin = request.EncLogin,
                    EncPassword = request.EncPassword,
                    Nonce = nonce
                };

                var json = JsonConvert.SerializeObject(authRequest);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_config.AuthTimeoutSeconds + 2));
                var response = await _httpClient.PostAsync(_config.AuthApiUrl, content, cts.Token);

                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync(cts.Token);
                    await ProcessAuthResponseAsync(session, responseJson);
                }
                else
                {
                    session.Status = AuthSessionStatus.Error;
                    OnAuthFailed?.Invoke(session);
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"[Auth] Request timed out for '{request.Login}'");
                session.Status = AuthSessionStatus.Expired;
                OnAuthFailed?.Invoke(session);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Auth] Error during auth: {ex.Message}");
                session.Status = AuthSessionStatus.Error;
                OnAuthFailed?.Invoke(session);
            }

            return session;
        }

        /// <summary>
        /// Обработка ответа от сервера авторизации
        /// </summary>
        private async Task ProcessAuthResponseAsync(AuthSession session, string responseJson)
        {
            try
            {
                // Парсим внешний JSON
                var outerResponse = JsonConvert.DeserializeObject<AuthServerResponse>(responseJson);

                if (outerResponse == null || outerResponse.Status != "success")
                {
                    session.Status = AuthSessionStatus.Rejected;
                    OnAuthFailed?.Invoke(session);
                    return;
                }

                // Парсим внутренний JSON из поля message
                var statusData = JsonConvert.DeserializeObject<AuthStatusData>(outerResponse.Message);

                if (statusData == null)
                {
                    session.Status = AuthSessionStatus.Rejected;
                    OnAuthFailed?.Invoke(session);
                    return;
                }

                // Проверяем nonce
                if (statusData.Nonce != session.Nonce)
                {
                    Console.WriteLine($"[Auth] Nonce mismatch: expected {session.Nonce}, got {statusData.Nonce}");
                    session.Status = AuthSessionStatus.Rejected;
                    OnAuthFailed?.Invoke(session);
                    return;
                }

                // Проверяем timestamp (не старше 5 секунд)
                var responseTime = DateTimeOffset.FromUnixTimeSeconds((long)statusData.Timestamp);
                var age = DateTimeOffset.UtcNow - responseTime;

                if (age.TotalSeconds > _config.AuthTimeoutSeconds)
                {
                    Console.WriteLine($"[Auth] Response too old: {age.TotalSeconds}s (max {_config.AuthTimeoutSeconds}s)");
                    session.Status = AuthSessionStatus.Expired;
                    OnAuthFailed?.Invoke(session);
                    return;
                }

                // Проверяем статус авторизации
                if (statusData.Status == "verified")
                {
                    session.Status = AuthSessionStatus.Verified;
                    session.CompletedAt = DateTime.UtcNow;
                    OnAuthSuccess?.Invoke(session);
                    Console.WriteLine($"[Auth] User '{session.Login}' verified successfully");
                }
                else
                {
                    session.Status = AuthSessionStatus.Rejected;
                    OnAuthFailed?.Invoke(session);
                    Console.WriteLine($"[Auth] User '{session.Login}' rejected: {statusData.Status}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Auth] Error processing response: {ex.Message}");
                session.Status = AuthSessionStatus.Error;
                OnAuthFailed?.Invoke(session);
            }
        }

        /// <summary>
        /// Получить сессию по nonce
        /// </summary>
        public AuthSession? GetSession(ulong nonce)
        {
            _sessions.TryGetValue(nonce, out var session);
            return session;
        }

        /// <summary>
        /// Удалить сессию
        /// </summary>
        public bool RemoveSession(ulong nonce)
        {
            return _sessions.TryRemove(nonce, out _);
        }

        /// <summary>
        /// Очистка старых сессий
        /// </summary>
        private async Task CleanupSessions()
        {
            while (_isRunning)
            {
                await Task.Delay(TimeSpan.FromSeconds(10));

                var expired = new List<ulong>();
                var now = DateTime.UtcNow;

                foreach (var kv in _sessions)
                {
                    if (kv.Value.IsExpired(_config.AuthSessionLifetimeSeconds))
                    {
                        expired.Add(kv.Key);
                        if (kv.Value.Status == AuthSessionStatus.Pending)
                        {
                            kv.Value.Status = AuthSessionStatus.Expired;
                            OnAuthFailed?.Invoke(kv.Value);
                        }
                    }
                }

                foreach (var nonce in expired)
                {
                    _sessions.TryRemove(nonce, out _);
                }

                if (expired.Count > 0)
                {
                    Console.WriteLine($"[Auth] Cleaned up {expired.Count} expired sessions");
                }
            }
        }
    }
}