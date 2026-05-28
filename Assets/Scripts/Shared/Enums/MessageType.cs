namespace Shared.Enums;

public enum MessageType : byte
{
    LoginRequest = 1,
    LoginResponse = 2,
    PlayerJoined = 3,
    PlayerLeft = 4,
    PlayerPosition = 5,
    GameStart = 6
}