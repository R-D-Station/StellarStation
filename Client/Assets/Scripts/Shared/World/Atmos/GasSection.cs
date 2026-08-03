using Shared.World.Blocks;

namespace Shared.World.Atmos
{
    public sealed class GasSection
    {
        public const int Size = ChunkSection.Size;
        public const int CellCount = ChunkSection.BlockCount;

        private readonly float[] _o2 = new float[CellCount];
        private readonly float[] _n2 = new float[CellCount];
        private readonly ulong[] _spaceBits = new ulong[CellCount / 64];

        public static int LocalIndex(int lx, int ly, int lz) => ChunkSection.LocalIndex(lx, ly, lz);

        public GasMix Get(int localIndex) => new GasMix(_o2[localIndex], _n2[localIndex]);

        public void Set(int localIndex, in GasMix mix)
        {
            _o2[localIndex] = mix.O2Moles;
            _n2[localIndex] = mix.N2Moles;
        }

        public bool IsSpace(int localIndex) => (_spaceBits[localIndex >> 6] & (1UL << (localIndex & 63))) != 0UL;

        public void MarkSpace(int localIndex, bool space = true)
        {
            ulong bit = 1UL << (localIndex & 63);
            if (space) _spaceBits[localIndex >> 6] |= bit;
            else _spaceBits[localIndex >> 6] &= ~bit;
        }
    }
}
