namespace Shared.World.Atmos
{
    public static class AtmosConstants
    {
        public const float StandardPressureKpa = 101.325f;

        public const float OxygenFraction = 0.21f;
        public const float NitrogenFraction = 0.79f;

        public const float StandardTotalMoles = 1f;

        public const float KpaPerMole = StandardPressureKpa / StandardTotalMoles;

        public const float MoleEpsilon = 1e-6f;
        public const float PressureEpsilon = 1e-4f;

        public const float MaxFlowRate = 1f / 8f;
        public const float DefaultFlowRate = 0.125f;
        public const float DefaultFlowEpsilon = 1e-4f;
    }
}
