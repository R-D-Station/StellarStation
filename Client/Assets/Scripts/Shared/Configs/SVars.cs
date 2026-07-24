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


    /// <summary>Стриминг: глубина этажей вокруг текущего (окно z ∈ [Z-depth .. Z+depth]). Держать синхронно

    /// <summary>Стриминг: через сколько секунд «вне радиуса» чанк выгружается у клиента (пере-стрим при возврате).</summary>
    public int ChunkUnloadTimeoutSec = 10;

    /// <summary>Entity-PVS: радиус интереса по сущностям (тайлы). Держать ≥ видимой/стримящейся зоны
    /// Тюнит человек.</summary>
    public float EntityInterestRadius = 40f;

    /// <summary>Entity-PVS: окно этажей интереса |E.Z - C.Z| ≤ depth. 0 = только свой этаж.</summary>
    public int EntityInterestZDepth = 0;

    /// <summary>Диагностика авто-дверей: лог реестра при загрузке + троттл-лог детекта раз в секунду.</summary>
    public bool DebugAutoDoors = false;

    /// <summary>Диагностика зон: детальный лог зон/стыков/конфликтов при флудфилле на загрузке.</summary>
    public bool DebugZones = false;

    /// <summary>Горизонтальная дальность плана-градиента зоны (тайлы); едет клиенту в LoginResponse.</summary>
    public float ZoneFadeDistance = 10f;

    /// <summary>Вертикальная дальность градиента зоны (этажи); едет клиенту в LoginResponse.</summary>
    public float ZoneFadeVertical = 1.5f;

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
