using System.IO;
using Newtonsoft.Json;
using System;

namespace Shared.Configs;

/// <summary>
/// Настройки сервера. Грузятся из JSON, общие для клиента и сервера.
/// </summary>
public class SVars
{
    public static SVars Instance { get; private set; } = new SVars();

    public string Ip = "0.0.0.0";

    public int Port = 7777;

    public int MaxPlayers = 100;

    public int TickRate = 30;

    public string ConnectionKey = string.Empty;

    /// <summary>Путь к файлу карты (.smap), грузится на старте. Нет файла — сервер поднимется без коллизии.</summary>
    public string MapPath = "station.smap";

    /// <summary>Загрузка настроек из JSON. Любая ошибка → дефолты, сервер не падает.</summary>
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
