namespace Shared.Messages;

/// <summary>
/// Перечисление типов сообщений, которыми могут обмениваться клиент и сервер.
/// </summary>
public enum MessageType : ushort
{
    // Core (0-99)
    MoveIntent = 1,
    WorldSnapshot = 2,
    EntitySnapshot = 3,

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