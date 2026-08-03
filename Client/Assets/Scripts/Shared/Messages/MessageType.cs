namespace Shared.Messages;

/// <summary>
/// Тип сетевого сообщения — тег для маршрутизации при сериализации/десериализации.
/// </summary>
public enum MessageType : ushort
{
    // Core (0-99)
    MoveIntent = 1,
    WorldSnapshot = 2,
    EntitySnapshot = 3,
    // 4/5/7/8 — RESERVED (тайл-легаси: MapData/TileUpdate/MapChunk/MapChunkUnload). НЕ переиспользовать:
    // старый клиент истолкует новый id как старое сообщение.
    ItemSnapshot = 9,    // отдельный PVS-поток наземных предметов (server→client)
    BlockMapData = 10,   // ВЫВЕДЕН (был блоб-мост B2 до стрима); id зарезервирован, не переиспользовать
    BlockChunkData = 11,   // фаза C: одна секция 16³ (server→client, ReliableOrdered)
    BlockSectionGone = 12, // фаза C: секция ушла — вне радиуса (забудь) либо опустела (воздух)
    BlockUpdateBatch = 13, // фаза C: пакет дельт блоков одного тика (server→client, держателям секций)
    AtmosSync = 14, // server→client, OWNER-ONLY: атмосфера клетки игрока (давление+O₂ кПа) — HUD-датчик
    LiftSync = 15,  // server→client, BROADCAST: сегмент траектории лифта (только при смене; позиция = Y(tick) на обеих сторонах)
    LiftRegistry = 16, // server→client, BROADCAST: СТАТИЧНАЯ геометрия лифтов (якорь + боксы кабины); обязан прийти ДО первого LiftSync
    LiftFloorRequest = 17, // client→server: выбор этажа с панели в кабине (request-only, без предсказания)

    // Player (100-199)
    LoginRequest = 100,
    LoginResponse = 101,
    PlayerJoined = 102,
    PlayerLeft = 103,

    UseIntent = 6,

    // Interaction (200-299)
    InteractIntent = 200, // адресный клик по тайлу/сущности (перепрофиль заглушки ClickIntent; wire-id стабилен)
    PickupItem = 201,
    DropItem = 202,
    InventorySync = 203, // server→client, OWNER-ONLY: слепок занятых слотов (variable-length, адрес Category+Index)
    SwapHand = 204,      // client→server: сменить активную руку
    MoveSlot = 205,      // client→server: переместить предмет между слотами
    OpenContainer = 206,     // client→server: открыть контейнер (по NetId)
    CloseContainer = 207,    // client→server: закрыть контейнер
    PutInContainer = 208,    // client→server: положить предмет активной руки в контейнер
    TakeFromContainer = 209, // client→server: взять предмет из контейнера в активную руку
    ContainerSync = 210,     // server→client, VIEWER-ONLY: слепок содержимого открытого контейнера
    PullItem = 211,          // client→server: тоггл тяги наземного предмета (взять/отпустить)
    PullSync = 212,          // server→client, OWNER-ONLY: состояние тяги (PulledNetId+ItemDefId; 0/0 = release) для HUD
    ContainSync = 213,       // server→client, OWNER-ONLY: заперт ли игрок в SS14-ящике (ContainerNetId; 0 = свободен) — заморозка ввода + скрытие своего спрайта

    // Chat (300-399)
    ChatMessage = 300,
}