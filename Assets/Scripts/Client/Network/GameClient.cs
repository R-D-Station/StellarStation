using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Client.Core;

namespace Client.Network;

public class GameClient
{
    private TcpClient tcpClient;
    private NetworkStream stream;
    private CancellationTokenSource cancellationToken;

    public event Action OnConnected;
    public event Action OnDisconnected;
    public event Action<byte[]> OnMessageReceived;
    public event Action<string> OnError;

    public bool IsConnected => tcpClient?.Connected ?? false;

    public async void Connect(string ip, int port)
    {
        try
        {
            cancellationToken = new CancellationTokenSource();
            tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(ip, port);
            stream = tcpClient.GetStream();

            Debug.Log($"[GameClient] Connected to {ip}:{port}");

            _ = ReceiveLoop();

            UnityMainThreadDispatcher.Instance?.Enqueue(() => OnConnected?.Invoke());
        }
        catch (Exception e)
        {
            Debug.LogError($"[GameClient] Connection failed: {e.Message}");
            UnityMainThreadDispatcher.Instance?.Enqueue(() => OnError?.Invoke(e.Message));
        }
    }

    private async Task ReceiveLoop()
    {
        byte[] lengthBuffer = new byte[4];

        while (tcpClient?.Connected == true)
        {
            try
            {
                int bytesRead = await stream.ReadAsync(lengthBuffer, 0, 4);
                if (bytesRead < 4) break;

                int messageLength = BitConverter.ToInt32(lengthBuffer, 0);
                if (messageLength <= 0 || messageLength > 1024 * 1024) break;

                byte[] messageBuffer = new byte[messageLength];
                int totalRead = 0;
                while (totalRead < messageLength)
                {
                    int read = await stream.ReadAsync(messageBuffer, totalRead, messageLength - totalRead);
                    if (read == 0) break;
                    totalRead += read;
                }

                UnityMainThreadDispatcher.Instance?.Enqueue(() => OnMessageReceived?.Invoke(messageBuffer));
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameClient] Receive error: {e.Message}");
                break;
            }
        }

        UnityMainThreadDispatcher.Instance?.Enqueue(() => OnDisconnected?.Invoke());
    }

    public void SendMessage(byte[] data)
    {
        if (!IsConnected || stream == null) return;

        try
        {
            stream.Write(data, 0, data.Length);
            stream.Flush();
        }
        catch (Exception e)
        {
            Debug.LogError($"[GameClient] Send error: {e.Message}");
        }
    }

    public void Disconnect()
    {
        cancellationToken?.Cancel();
        stream?.Close();
        tcpClient?.Close();
    }
}