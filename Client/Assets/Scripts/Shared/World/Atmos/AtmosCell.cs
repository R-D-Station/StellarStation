namespace Shared.World.Atmos
{
    public static class AtmosCell
    {
        public static readonly int[] NeighborDx = { 1, -1, 0, 0, 0, 0 };
        public static readonly int[] NeighborDy = { 0, 0, 1, -1, 0, 0 };
        public static readonly int[] NeighborDz = { 0, 0, 0, 0, 1, -1 };

        public const int NeighborCount = 6;

        public static long Pack(int x, int y, int z)
            => ((long)(x & 0x1FFFFF)) | ((long)(y & 0x1FFFFF) << 21) | ((long)(z & 0x1FFFFF) << 42);

        public static void Unpack(long packed, out int x, out int y, out int z)
        {
            x = SignExtend21((int)(packed & 0x1FFFFF));
            y = SignExtend21((int)((packed >> 21) & 0x1FFFFF));
            z = SignExtend21((int)((packed >> 42) & 0x1FFFFF));
        }

        private static int SignExtend21(int v) => (v & (1 << 20)) != 0 ? v | ~0x1FFFFF : v;
    }
}
