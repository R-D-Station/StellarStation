using System;
using System.Collections.Generic;
using System.Net;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

class Program
{
    static List<TcpClient> _clients = new List<TcpClient>();
    static List<string> _playerNames = new List<string>();
    static object _lock = new object();

    static void Main()
    {
        TcpListener listener = new TcpListener(IPAddress.Any, 7777);
        listener.Start();
        Console.WriteLine("Server started on port 7777");
        Console.WriteLine("Waiting for client...");

        while (true)
        {
            TcpClient client = listener.AcceptTcpClient();

            lock (_lock)
            {
                _clients.Add(client);
                _playerNames.Add($"Player{_clients.Count}");
            }

            Console.WriteLine($"Client connected! Total: {_clients.Count}");
            ThreadPool.QueueUserWorkItem(HandleClient, client);
        }
    }

    static void HandleClient(object obj)
    {
        TcpClient client = (TcpClient)obj;
        NetworkStream stream = client.GetStream();
        int playerId;
        string playerName;

        lock (_lock)
        {
            playerId = _clients.IndexOf(client) + 1;
            playerName = _playerNames[playerId - 1];
        }

        try
        {
            // Читаем логин
            byte[] lengthBuffer = new byte[4];
            int read = stream.Read(lengthBuffer, 0, 4);
            if (read < 4)
            {
                client.Close();
                return;
            }

            int messageLength = BitConverter.ToInt32(lengthBuffer, 0);

            byte[] messageData = new byte[messageLength];
            int totalRead = 0;
            while (totalRead < messageLength)
            {
                int r = stream.Read(messageData, totalRead, messageLength - totalRead);
                if (r == 0) break;
                totalRead += r;
            }

            // Извлекаем имя
            string received = Encoding.UTF8.GetString(messageData);
            Console.WriteLine($"Received login: {received}");

            if (received.Contains("\"username\":\""))
            {
                int start = received.IndexOf("\"username\":\"") + 12;
                int end = received.IndexOf("\"", start);
                if (end > start)
                    playerName = received.Substring(start, end - start);
            }

            lock (_lock)
            {
                _playerNames[playerId - 1] = playerName;
            }

            // LoginResponse
            string loginBody = $"{{\"success\":true,\"playerId\":{playerId}}}";
            SendMessage(stream, $"{{\"messageType\":2,\"playerId\":0}}", loginBody);
            Console.WriteLine($"Sent LoginResponse to {playerName}");

            Thread.Sleep(300);

            // PlayerJoined всем
            string joinedBody = $"{{\"playerId\":{playerId},\"playerName\":\"{playerName}\"}}";
            BroadcastMessage($"{{\"messageType\":3,\"playerId\":0}}", joinedBody, client);
            Console.WriteLine($"Broadcasted PlayerJoined: {playerName}");

            Thread.Sleep(200);

            // LobbyState всем
            string[] allNames;
            lock (_lock) { allNames = _playerNames.ToArray(); }
            string namesJson = string.Join("\",\"", allNames);
            string lobbyBody = $"{{\"players\":[\"{namesJson}\"]}}";
            BroadcastMessage($"{{\"messageType\":7,\"playerId\":0}}", lobbyBody, null);
            Console.WriteLine("Sent LobbyState to all");

            // Цикл приёма чата
            byte[] buffer = new byte[4096];
            while (true)
            {
                try
                {
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;

                    // Копируем полученные данные
                    byte[] receivedData = new byte[bytesRead];
                    Array.Copy(buffer, receivedData, bytesRead);

                    string msgStr = Encoding.UTF8.GetString(receivedData);
                    Console.WriteLine($"RAW received: {msgStr}");

                    // Ищем ChatMessage (messageType:8)
                    if (msgStr.Contains("\"messageType\":8"))
                    {
                        string chatText = "";
                        int msgStart = msgStr.IndexOf("\"message\":\"");
                        if (msgStart >= 0)
                        {
                            msgStart += 11;
                            int msgEnd = msgStr.IndexOf("\"}", msgStart);
                            if (msgEnd < 0) msgEnd = msgStr.IndexOf("\"}", msgStart);
                            if (msgEnd < 0) msgEnd = msgStr.LastIndexOf("\"");
                            if (msgEnd > msgStart)
                                chatText = msgStr.Substring(msgStart, msgEnd - msgStart);
                        }

                        if (!string.IsNullOrEmpty(chatText))
                        {
                            Console.WriteLine($"Chat from {playerName}: {chatText}");
                            string chatBody = $"{{\"playerId\":{playerId},\"playerName\":\"{playerName}\",\"message\":\"{chatText}\"}}";
                            BroadcastMessage($"{{\"messageType\":8,\"playerId\":{playerId}}}", chatBody, null);
                        }
                    }
                }
                catch (IOException)
                {
                    break;
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error in receive loop: {e.Message}");
                    break;
                }
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error with {playerName}: {e.Message}");
        }
        finally
        {
            lock (_lock)
            {
                _clients.Remove(client);
                _playerNames.RemoveAt(playerId - 1);
            }

            client.Close();
            Console.WriteLine($"{playerName} disconnected. Total: {_clients.Count}");

            // PlayerLeft всем
            string leftBody = $"{{\"playerId\":{playerId},\"playerName\":\"{playerName}\"}}";
            BroadcastMessage($"{{\"messageType\":4,\"playerId\":0}}", leftBody, null);

            // Обновлённый LobbyState всем
            string[] names;
            lock (_lock) { names = _playerNames.ToArray(); }
            string nJson = string.Join("\",\"", names);
            string lbBody = $"{{\"players\":[\"{nJson}\"]}}";
            BroadcastMessage($"{{\"messageType\":7,\"playerId\":0}}", lbBody, null);
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

    static void BroadcastMessage(string headerJson, string bodyJson, TcpClient excludeClient)
    {
        TcpClient[] clients;
        lock (_lock) { clients = _clients.ToArray(); }

        foreach (var client in clients)
        {
            if (client == excludeClient) continue;
            if (!client.Connected) continue;

            try
            {
                SendMessage(client.GetStream(), headerJson, bodyJson);
            }
            catch { }
        }
    }
}