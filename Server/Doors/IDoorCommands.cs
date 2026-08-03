namespace Server.Doors
{
    public enum DoorCommandResult : byte
    {
        Applied,
        AlreadyInState,
        NoSuchDoor,
        BlockedByOccupant,
        BlockedByPressure
    }

    public interface IDoorCommands
    {
        bool TryGetAnchorKey(int x, int y, int z, out long key);
        bool Exists(long key);
        bool IsOpen(long key);
        DoorCommandResult TrySetOpen(long key, bool open, bool force = false);
    }
}
