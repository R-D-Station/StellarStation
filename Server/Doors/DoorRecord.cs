namespace Server.Doors
{
    public sealed class DoorRecord
    {
        public int Ax, Ay, Az;
        public ushort Type;
        public bool Open;
        public uint CloseAtTick; // uint.MaxValue = держать открытой (игрок в зоне)
    }

    public struct DoorScanStats
    {
        public int Registered;
        public int Interact;
        public int External;
        public int Openable;
        public int SkippedNoTrigger;
        public int SkippedNotAnchor;
    }
}
