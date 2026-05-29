using System;
using System.Net.Sockets;
using System.Threading.Tasks;
using UnityEngine;

namespace Server.Network
{
    public class ServerClient
    {
        public int PlayerId { get; private set; }
        public string PlayerName { get; set; }

        private TcpClient _tcpClient;
        private NetworkStream _stream;

        public event Action<ServerClient, byte[]> OnMessageReceived;
        public event Action<ServerClient> OnDisconnected;

        public ServerClient(TcpClient tcpClient, int playerId)
        {
            _tcpClient = tcpClient;
            _stream = tcpClient.GetStream();
            PlayerId = playerId;

            _ = ReceiveLoop();
        }

        async Task ReceiveLoop()
        {
            byte[] lengthBuf = new byte[4];

            while (_tcpClient?.Connected == true)
            {
                try
                {
                    int read = await _stream.ReadAsync(lengthBuf, 0, 4);
                    if (read < 4) break;

                    int msgLen = BitConverter.ToInt32(lengthBuf, 0);
                    byte[] data = new byte[msgLen];
                    int total = 0;
                    while (total < msgLen)
                    {
                        int r = await _stream.ReadAsync(data, total, msgLen - total);
                        if (r == 0) break;
                        total += r;
                    }

                    OnMessageReceived?.Invoke(this, data);
                }
                catch
                {
                    break;
                }
            }

            OnDisconnected?.Invoke(this);
        }

        public void Send(byte[] data)
        {
            if (_stream == null) return;

            try
            {
                byte[] prefix = BitConverter.GetBytes(data.Length);
                _stream.Write(prefix, 0, 4);
                _stream.Write(data, 0, data.Length);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ServerClient] Send error: {e.Message}");
            }
        }

        public void Disconnect()
        {
            _stream?.Close();
            _tcpClient?.Close();
        }
    }
}