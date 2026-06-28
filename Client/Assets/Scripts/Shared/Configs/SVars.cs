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
    public int SoftMaxPlayers = 80;
    public int TickRate = 30;
    public string ConnectionKey = string.Empty;
    public string MapPath = "station.smap";

    // Настройки авторизации
    public string AuthApiUrl = "http://127.0.0.1:45607/api/v2/auth/check_player";
    public int AuthTimeoutSeconds = 5;
    public int AuthSessionLifetimeSeconds = 7;

    /// <summary>
    /// Загрузка настроек из JSON.
    /// </summary>
    public static void LoadFromJson(string path)
    {
        try
        {
            string json = File.ReadAllText(path);
            var loaded = JsonConvert.DeserializeObject<SVars>(json);
            Instance = loaded ?? new SVars();
        }
        catch (Exception e)
        {
            Console.WriteLine($"Failed to load config from {path}. Error: {e.Message}");
            Instance = new SVars();
        }
    }
}