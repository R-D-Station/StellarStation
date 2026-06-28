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
    UseIntent = 6,
    AuthRequest = 7,
    AuthResponse = 8,

    // Player (100-199)
    LoginRequest = 100,
    LoginResponse = 101,
    PlayerJoined = 102,
    PlayerLeft = 103,

    // Interaction (200-299)
    ClickIntent = 200,
    PickupItem = 201,
    DropItem = 202,

    // Chat (300-399)
    ChatMessage = 300,
}