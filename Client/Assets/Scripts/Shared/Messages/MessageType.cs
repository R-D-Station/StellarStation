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
    MapData = 4,
    TileUpdate = 5,
    MapChunk = 7,        // стриминг: один чанк карты (server→client)
    MapChunkUnload = 8,  // стриминг: выгрузить чанк (server→client)
    ItemSnapshot = 9,    // отдельный PVS-поток наземных предметов (server→client)

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
    InventorySync = 203, // server→client, OWNER-ONLY: полный слепок 6 слотов инвентаря
    SwapHand = 204,      // client→server: сменить активную руку
    MoveSlot = 205,      // client→server: переместить предмет между слотами

    // Chat (300-399)
    ChatMessage = 300,
}