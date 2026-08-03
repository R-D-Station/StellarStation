namespace Shared.World.Atmos
{
    public struct GasMix
    {
        public float O2Moles;
        public float N2Moles;

        public GasMix(float o2Moles, float n2Moles)
        {
            O2Moles = o2Moles;
            N2Moles = n2Moles;
        }

        public float TotalMoles => O2Moles + N2Moles;

        public float PressureKpa => TotalMoles * AtmosConstants.KpaPerMole;

        public float OxygenKpa
        {
            get
            {
                float total = TotalMoles;
                return total <= 0f ? 0f : total * AtmosConstants.KpaPerMole * (O2Moles / total);
            }
        }

        public static GasMix Standard => new GasMix(
            AtmosConstants.StandardTotalMoles * AtmosConstants.OxygenFraction,
            AtmosConstants.StandardTotalMoles * AtmosConstants.NitrogenFraction);

        public static GasMix Vacuum => default;

        public bool IsVacuum(float eps = AtmosConstants.MoleEpsilon) => TotalMoles <= eps;
    }
}
