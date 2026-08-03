using System.Collections.Generic;
using Shared.World.Blocks;

namespace Shared.World.Atmos
{
    public sealed class AtmosGrid
    {
        private readonly Dictionary<long, GasSection> _sections = new Dictionary<long, GasSection>();

        public IReadOnlyDictionary<long, GasSection> Sections => _sections;

        public static long Key(int cx, int cy, int cz) => BlockGrid.Key(cx, cy, cz);

        public static int SectionCoord(int world) => FloorDiv(world, GasSection.Size);

        public static int LocalCoord(int world) => world - SectionCoord(world) * GasSection.Size;

        public bool HasSection(int cx, int cy, int cz) => _sections.ContainsKey(Key(cx, cy, cz));

        public GasSection GetOrCreateSection(int cx, int cy, int cz)
        {
            long key = Key(cx, cy, cz);
            if (!_sections.TryGetValue(key, out var s))
            {
                s = new GasSection();
                _sections[key] = s;
            }
            return s;
        }

        public GasMix GetMix(int x, int y, int z)
        {
            return _sections.TryGetValue(Key(SectionCoord(x), SectionCoord(y), SectionCoord(z)), out var s)
                ? s.Get(LocalIndexOf(x, y, z))
                : GasMix.Vacuum;
        }

        public void SetMix(int x, int y, int z, in GasMix mix)
            => GetOrCreateSection(SectionCoord(x), SectionCoord(y), SectionCoord(z)).Set(LocalIndexOf(x, y, z), in mix);

        public bool IsSpace(int x, int y, int z)
        {
            return _sections.TryGetValue(Key(SectionCoord(x), SectionCoord(y), SectionCoord(z)), out var s)
                && s.IsSpace(LocalIndexOf(x, y, z));
        }

        public void MarkSpace(int x, int y, int z, bool space = true)
            => GetOrCreateSection(SectionCoord(x), SectionCoord(y), SectionCoord(z)).MarkSpace(LocalIndexOf(x, y, z), space);

        private static int LocalIndexOf(int x, int y, int z)
            => GasSection.LocalIndex(LocalCoord(x), LocalCoord(y), LocalCoord(z));

        private static int FloorDiv(int a, int b)
        {
            int q = a / b;
            if ((a % b != 0) && ((a < 0) != (b < 0))) q--;
            return q;
        }
    }
}
