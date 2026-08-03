namespace Shared.Simulation.Blocks
{
    public struct LiftPlan
    {
        public LiftSegment Segment;
        public uint DwellUntilTick;
    }

    public enum LiftPhaseKind : byte
    {
        Travel = 0,
        Dwell = 1,
        Closing = 2,
        Ready = 3
    }

    public static class LiftPhase
    {
        public static LiftPhaseKind At(in LiftPlan p, uint tick, uint doorLeadTicks)
        {
            if (tick < LiftTrajectory.ArrivalTick(in p.Segment))
                return LiftPhaseKind.Travel;
            if (tick >= p.DwellUntilTick)
                return LiftPhaseKind.Ready;
            uint closeAt = p.DwellUntilTick > doorLeadTicks ? p.DwellUntilTick - doorLeadTicks : 0u;
            return tick < closeAt ? LiftPhaseKind.Dwell : LiftPhaseKind.Closing;
        }

        public static int Direction(in LiftSegment s)
            => s.ToY > s.FromY ? 1 : (s.ToY < s.FromY ? -1 : 0);

        public static uint DwellUntil(in LiftSegment s, uint dwellTicks, uint doorLeadTicks)
            => LiftTrajectory.ArrivalTick(in s) + dwellTicks + doorLeadTicks;
    }
}
