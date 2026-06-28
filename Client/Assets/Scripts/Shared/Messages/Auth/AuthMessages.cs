using System;
using System.Text;
using Newtonsoft.Json;

namespace Shared.Messages.Auth
{
    /// <summary>
    /// “ипы ответов авторизации
    /// </summary>
    public enum AuthResponseStatus : byte
    {
        Success = 0,
        Pending = 1,
        Queued = 2,
        Rejected = 3,
        Error = 4,
        Timeout = 5
    }

    /// <summary>
    /// «апрос на авторизацию от клиента к игровому серверу
    /// </summary>
    public class ClientAuthRequest : INetMessage
    {
        public string Login { get; set; } = string.Empty;
        public string EncLogin { get; set; } = string.Empty;
        public string EncPassword { get; set; } = string.Empty;
        public ulong Nonce { get; set; }

        public MessageType Type => MessageType.AuthRequest;

        public byte[] Serialize()
        {
            var json = JsonConvert.SerializeObject(new
            {
                Login,
                EncLogin,
                EncPassword,
                Nonce
            });
            return Encoding.UTF8.GetBytes(json);
        }

        public void Deserialize(byte[] data)
        {
            if (data == null || data.Length == 0)
                throw new ArgumentException("Data cannot be null or empty", nameof(data));

            try
            {
                var json = Encoding.UTF8.GetString(data);
                var obj = JsonConvert.DeserializeObject<dynamic>(json);

                Login = obj.Login ?? string.Empty;
                EncLogin = obj.EncLogin ?? string.Empty;
                EncPassword = obj.EncPassword ?? string.Empty;
                Nonce = obj.Nonce != null ? Convert.ToUInt64(obj.Nonce) : 0;
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Failed to deserialize AuthRequest: {ex.Message}", ex);
            }
        }
    }

    /// <summary>
    /// «апрос от игрового сервера к серверу авторизации
    /// </summary>
    public class AuthServerRequest
    {
        [JsonProperty("login")]
        public string Login { get; set; } = string.Empty;

        [JsonProperty("enc_login")]
        public string EncLogin { get; set; } = string.Empty;

        [JsonProperty("enc_password")]
        public string EncPassword { get; set; } = string.Empty;

        [JsonProperty("nonce")]
        public ulong Nonce { get; set; }
    }

    /// <summary>
    /// ќтвет от сервера авторизации
    /// </summary>
    public class AuthServerResponse
    {
        [JsonProperty("status")]
        public string Status { get; set; } = string.Empty;

        [JsonProperty("message")]
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// —татус авторизации от сервера авторизации (внутренний JSON)
    /// </summary>
    public class AuthStatusData
    {
        [JsonProperty("status")]
        public string Status { get; set; } = string.Empty;

        [JsonProperty("timestamp")]
        public ulong Timestamp { get; set; }

        [JsonProperty("nonce")]
        public ulong Nonce { get; set; }
    }

    /// <summary>
    /// ќтвет игрового сервера клиенту
    /// </summary>
    public struct AuthResponse : INetMessage
    {
        public AuthResponseStatus Status { get; set; }
        public string Message { get; set; }
        public int PlayerNetId { get; set; }
        public float SpawnX { get; set; }
        public float SpawnY { get; set; }
        public int SpawnZ { get; set; }
        public int QueuePosition { get; set; }

        public MessageType Type => MessageType.AuthResponse;

        public byte[] Serialize()
        {
            var json = JsonConvert.SerializeObject(new
            {
                Status = (byte)Status,
                Message,
                PlayerNetId,
                SpawnX,
                SpawnY,
                SpawnZ,
                QueuePosition
            });
            return Encoding.UTF8.GetBytes(json);
        }

        public void Deserialize(byte[] data)
        {
            if (data == null || data.Length == 0)
                throw new ArgumentException("Data cannot be null or empty", nameof(data));

            try
            {
                var json = Encoding.UTF8.GetString(data);
                var obj = JsonConvert.DeserializeObject<dynamic>(json);

                Status = (AuthResponseStatus)(byte)obj.Status;
                Message = obj.Message ?? string.Empty;
                PlayerNetId = obj.PlayerNetId != null ? (int)obj.PlayerNetId : 0;
                SpawnX = obj.SpawnX != null ? (float)obj.SpawnX : 0;
                SpawnY = obj.SpawnY != null ? (float)obj.SpawnY : 0;
                SpawnZ = obj.SpawnZ != null ? (int)obj.SpawnZ : 0;
                QueuePosition = obj.QueuePosition != null ? (int)obj.QueuePosition : 0;
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Failed to deserialize AuthResponse: {ex.Message}", ex);
            }
        }
    }
}