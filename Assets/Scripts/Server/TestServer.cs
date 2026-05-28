using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

class Program
{
    static void Main()
    {
        TcpListener listener = new TcpListener(IPAddress.Any, 7777);
        listener.Start();
        Console.WriteLine("Server started on port 7777");
        Console.WriteLine("Waiting for client...");

        while (true)
        {
            TcpClient client = listener.AcceptTcpClient();
            Console.WriteLine("Client connected!");
            ThreadPool.QueueUserWorkItem(HandleClient, client);
        }
    }

    static void HandleClient(object obj)
    {
        TcpClient client = (TcpClient)obj;
        NetworkStream stream = client.GetStream();

        try
        {
            // Читаем сообщение логина
            byte[] lengthBuffer = new byte[4];
            stream.Read(lengthBuffer, 0, 4);
            int messageLength = BitConverter.ToInt32(lengthBuffer, 0);

            byte[] messageData = new byte[messageLength];
            int totalRead = 0;
            while (totalRead < messageLength)
            {
                int r = stream.Read(messageData, totalRead, messageLength - totalRead);
                if (r == 0) break;
                totalRead += r;
            }

            Console.WriteLine($"Received: {Encoding.UTF8.GetString(messageData)}");

            // Отправляем LoginResponse
            SendMessage(stream, "{\"messageType\":2,\"playerId\":0}", "{\"success\":true,\"playerId\":1}");
            Console.WriteLine("Sent LoginResponse");

            Thread.Sleep(1000);

            // Отправляем GameStart
            SendMessage(stream, "{\"messageType\":6,\"playerId\":0}", "{\"playerIds\":[1]}");
            Console.WriteLine("Sent GameStart");

            // Держим соединение, пока клиент не отключится
            while (client.Connected)
            {
                // Проверяем, не отключился ли клиент
                if (client.Client.Poll(0, SelectMode.SelectRead))
                {
                    byte[] buff = new byte[1];
                    if (client.Client.Receive(buff, SocketFlags.Peek) == 0)
                        break;
                }
                Thread.Sleep(1000);
            }

            Console.WriteLine("Client disconnected gracefully");
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error: {e.Message}");
        }
        finally
        {
            client.Close();
            Console.WriteLine("Connection closed");
        }
    }

    static void SendMessage(NetworkStream stream, string headerJson, string bodyJson)
    {
        byte[] bodyBytes = Encoding.UTF8.GetBytes(bodyJson);
        byte[] headerJsonBytes = Encoding.UTF8.GetBytes(headerJson);

        byte[] headerFull = new byte[4 + headerJsonBytes.Length];
        BitConverter.GetBytes(headerJsonBytes.Length).CopyTo(headerFull, 0);
        headerJsonBytes.CopyTo(headerFull, 4);

        byte[] fullMessage = new byte[headerFull.Length + bodyBytes.Length];
        headerFull.CopyTo(fullMessage, 0);
        bodyBytes.CopyTo(fullMessage, headerFull.Length);

        byte[] lengthPrefix = BitConverter.GetBytes(fullMessage.Length);
        stream.Write(lengthPrefix, 0, 4);
        stream.Write(fullMessage, 0, fullMessage.Length);
    }
}