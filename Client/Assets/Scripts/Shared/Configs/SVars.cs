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

    /// <summary>Атмос: период суб-тика потока в серверных тиках (15 ≈ 0.5с при 30 Гц).</summary>
    public int AtmosIntervalTicks = 15;

    /// <summary>Атмос: потолок обработанных клеток за суб-тик (амортизация; остаток ждёт следующего).</summary>
    public int AtmosMaxCellsPerSubtick = 1024;

    /// <summary>Атмос: доля разницы газа, переносимая за суб-тик (≤1/8 — устойчивость при 6 соседях).</summary>
    public float AtmosFlowRate = 0.125f;

    /// <summary>Атмос: порог переноса, ниже которого клетка засыпает (выход из активного множества).</summary>
    public float AtmosEpsilon = 1e-4f;

    /// <summary>Экспозиция: минимум парциального кислорода (кПа) для дыхания; ниже — счётчик удушья растёт.</summary>
    public float AtmosMinO2Kpa = 16f;

    /// <summary>Экспозиция: секунд удушья до потери сознания.</summary>
    public int AtmosUnconsciousSec = 15;

    /// <summary>Экспозиция: секунд удушья до смерти.</summary>
    public int AtmosDeathSec = 45;

    /// <summary>Датчик атмосферы: период отправки AtmosSync владельцу в тиках (вне порога изменения).</summary>
    public int AtmosSyncIntervalTicks = 30;

    /// <summary>Шлюз: перепад давления по сторонам (кПа), выше которого авто-дверь не открывается сама.</summary>
    public float AirlockMaxDeltaKpa = 20f;

    /// <summary>Тестовый лифт L2 (стенд покатушки; заменит скан карты L3): включён ли.</summary>
    public bool DebugLiftEnabled = false;

    /// <summary>Тестовый лифт: якорь платформы, план X.</summary>
    public float DebugLiftX = 5.5f;

    /// <summary>Тестовый лифт: якорь платформы, план Z.</summary>
    public float DebugLiftZ = 5.5f;

    /// <summary>Тестовый лифт: нижний уровень (высота низа платформы).</summary>
    public float DebugLiftFromY = 1f;

    /// <summary>Тестовый лифт: верхний уровень.</summary>
    public float DebugLiftToY = 4f;

    /// <summary>Тестовый лифт: скорость (блоков/тик).</summary>
    public float DebugLiftSpeed = 0.05f;

    /// <summary>Тестовый лифт: полуширина платформы (1 = 2×2 клетки).</summary>
    public float DebugLiftHalfW = 1f;

    /// <summary>Тестовый лифт: толщина платформы.</summary>
    public float DebugLiftHeight = 0.25f;

    /// <summary>Тестовый лифт: стоянка на этаже (тиков).</summary>
    public int DebugLiftPauseTicks = 60;

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
