using Shared.World.Atmos;
using Shared.World.Blocks;

namespace ServerTests.Shared.World.AtmosCore
{
    public class GasMixTests
    {
        [Fact]
        public void Standard_HasStandardPressure()
        {
            var mix = GasMix.Standard;
            Assert.Equal(AtmosConstants.StandardPressureKpa, mix.PressureKpa, 3);
            Assert.Equal(AtmosConstants.StandardTotalMoles, mix.TotalMoles, 5);
        }

        [Fact]
        public void Standard_OxygenPartialPressure_IsFractionOfTotal()
        {
            var mix = GasMix.Standard;
            Assert.Equal(AtmosConstants.StandardPressureKpa * AtmosConstants.OxygenFraction, mix.OxygenKpa, 3);
            Assert.True(mix.OxygenKpa < mix.PressureKpa);
        }

        [Fact]
        public void Vacuum_IsVacuum_ZeroPressure()
        {
            var mix = GasMix.Vacuum;
            Assert.True(mix.IsVacuum());
            Assert.Equal(0f, mix.PressureKpa, 6);
            Assert.Equal(0f, mix.OxygenKpa, 6);
        }

        [Fact]
        public void Moles_Add_PressureScalesLinearly()
        {
            var single = new GasMix(0.21f, 0.79f);
            var doubled = new GasMix(0.42f, 1.58f);
            Assert.Equal(single.PressureKpa * 2f, doubled.PressureKpa, 3);
            Assert.Equal(single.OxygenKpa * 2f, doubled.OxygenKpa, 3);
        }

        [Fact]
        public void PureOxygen_OxygenKpa_EqualsTotalPressure()
        {
            var mix = new GasMix(1f, 0f);
            Assert.Equal(mix.PressureKpa, mix.OxygenKpa, 5);
        }
    }

    public class GasSectionTests
    {
        [Fact]
        public void LocalIndex_MatchesChunkSectionPacking()
        {
            Assert.Equal(ChunkSection.LocalIndex(3, 5, 7), GasSection.LocalIndex(3, 5, 7));
            Assert.Equal(GasSection.CellCount, ChunkSection.BlockCount);
            Assert.Equal(GasSection.Size, ChunkSection.Size);
        }

        [Fact]
        public void SetGet_RoundTrips_PerCell()
        {
            var s = new GasSection();
            int a = GasSection.LocalIndex(1, 2, 3);
            int b = GasSection.LocalIndex(15, 15, 15);

            s.Set(a, new GasMix(0.5f, 1.5f));
            Assert.Equal(0.5f, s.Get(a).O2Moles, 5);
            Assert.Equal(1.5f, s.Get(a).N2Moles, 5);
            Assert.Equal(0f, s.Get(b).TotalMoles, 5);
        }

        [Fact]
        public void SpaceBits_SetClear_Independent()
        {
            var s = new GasSection();
            int a = GasSection.LocalIndex(0, 0, 0);
            int b = GasSection.LocalIndex(4, 0, 0);
            int far = GasSection.LocalIndex(15, 15, 15);

            Assert.False(s.IsSpace(a));
            s.MarkSpace(a);
            Assert.True(s.IsSpace(a));
            Assert.False(s.IsSpace(b));
            Assert.False(s.IsSpace(far));

            s.MarkSpace(far);
            Assert.True(s.IsSpace(far));

            s.MarkSpace(a, false);
            Assert.False(s.IsSpace(a));
            Assert.True(s.IsSpace(far));
        }
    }

    public class AtmosGridTests
    {
        [Fact]
        public void GetMix_NoSection_IsVacuum()
        {
            var atmos = new AtmosGrid();
            Assert.True(atmos.GetMix(1000, -50, 7).IsVacuum());
            Assert.False(atmos.IsSpace(1000, -50, 7));
            Assert.False(atmos.HasSection(0, 0, 0));
        }

        [Fact]
        public void SetMix_RoundTrips_ByWorldCell()
        {
            var atmos = new AtmosGrid();
            atmos.SetMix(5, 6, 7, GasMix.Standard);

            var back = atmos.GetMix(5, 6, 7);
            Assert.Equal(AtmosConstants.StandardPressureKpa, back.PressureKpa, 3);
            Assert.True(atmos.HasSection(0, 0, 0));
            Assert.True(atmos.GetMix(5, 6, 8).IsVacuum());
        }

        [Fact]
        public void NegativeCoords_MapToOwnSection_NoAliasing()
        {
            var atmos = new AtmosGrid();
            atmos.SetMix(-1, -1, -1, new GasMix(2f, 0f));
            atmos.SetMix(1, 1, 1, new GasMix(3f, 0f));

            Assert.Equal(2f, atmos.GetMix(-1, -1, -1).O2Moles, 5);
            Assert.Equal(3f, atmos.GetMix(1, 1, 1).O2Moles, 5);
            Assert.Equal(-1, AtmosGrid.SectionCoord(-1));
            Assert.Equal(15, AtmosGrid.LocalCoord(-1));
        }

        [Fact]
        public void MarkSpace_RoundTripsByWorldCell()
        {
            var atmos = new AtmosGrid();
            atmos.MarkSpace(20, 3, -5);
            Assert.True(atmos.IsSpace(20, 3, -5));
            Assert.False(atmos.IsSpace(21, 3, -5));
        }

        [Fact]
        public void SectionKey_MatchesBlockGridPacking()
        {
            Assert.Equal(BlockGrid.Key(1, -2, 3), AtmosGrid.Key(1, -2, 3));
        }
    }
}
