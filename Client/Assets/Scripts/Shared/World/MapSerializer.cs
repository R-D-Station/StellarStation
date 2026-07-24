namespace Shared.World
{
    public static class MapSerializer
    {
        public const int Magic = ('S') | ('M' << 8) | ('A' << 16) | ('P' << 24);
        public const ushort Version = 3;
    }
}
