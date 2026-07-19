namespace Server.Network
{
    /// <summary>Ключ общего серверного реестра сущностей (GameServer._entities): NetId + серверная позиция.
    /// Реализуют игроки (ClientConnection) и будущие предметы-на-земле (4.4).</summary>
    public interface IWorldEntity
    {
        int NetId { get; }
        float X { get; }
        float Y { get; }
        int Z { get; }
    }
}
