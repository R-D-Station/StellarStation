using System.IO;
using Newtonsoft.Json;
using System;

namespace Shared.Configs;

/// <summary>
/// Серверные переменные. Содержат настройки, которые важны для работы сервера.
/// </summary>
public class SVars
{
    /// <summary>
    /// Singleton, чтобы все части сервера могли легко получить доступ к конфигу.
    /// </summary>
    public static SVars Instance { get; private set; } = new SVars();

    /// <summary>
    /// IP-адрес, который слушает сервер. Клиенты должны знать его, чтобы подключиться.
    /// </summary>
    public string Ip = "0.0.0.0";

    /// <summary>
    /// Порт, который слушает сервер. Клиенты должны знать его, чтобы подключиться.
    /// </summary>
    public int Port = 7777;

    /// <summary>
    /// Максимальное количество одновременных игроков. 
    /// </summary>
    public int MaxPlayers = 100;

    /// <summary>
    /// Сколько раз в секунду сервер обновляет состояние мира и отсылает его клиентам. 
    /// </summary>
    public int TickRate = 30;

    /// <summary>
    /// Строка используемая для идентификации приложения.
    /// </summary>
    public string ConnectionKey = string.Empty;

    /// <summary>
    /// Метод для загрузки конфигурации из JSON-строки. Обычно вызывается при старте сервера.
    /// </summary>
    /// <param name="path"> Путь до конфигурационного файла </param>
    public static void LoadFromJson(string path)
    {
        try
        {
            string json = File.ReadAllText(path);

            var loaded = JsonConvert.DeserializeObject<SVars>(json);
            Instance = loaded ?? new SVars();

            if (Instance == null)
            {
                Console.WriteLine($"Failed to deserialize JSON from path: {path}");
                Instance = new SVars();
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed to load config from {path}. Error: {e.Message}");
            Instance = new SVars();
        }
    }
}