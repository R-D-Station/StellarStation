using System;
using Shared.Messages.Lifts;
using Shared.Simulation.Blocks;

namespace Client.Lifts
{
    public static class LiftCabinDoorState
    {
        public const float DefaultEps = 0.05f;

        public static bool TryStopAtY(LiftStopEntry[] stops, float cabinY, float eps, out LiftStopEntry stop)
        {
            stop = default;
            if (stops == null)
                return false;

            for (int i = 0; i < stops.Length; i++)
            {
                if (!stops[i].HasDoor)
                    continue;
                if (MathF.Abs(stops[i].Y - cabinY) > eps)
                    continue;
                stop = stops[i];
                return true;
            }
            return false;
        }

        public static bool ShouldOpen(LiftPhaseKind phase, bool stopFound, bool doorOpenBit)
            => stopFound && doorOpenBit && phase != LiftPhaseKind.Travel;
    }
}
