using System;

namespace Shared.Simulation.Blocks
{
    public struct LiftSegment
    {
        public float FromY;
        public float ToY;
        public uint StartTick;
        public float BlocksPerTick;

        public LiftSegment(float fromY, float toY, uint startTick, float blocksPerTick)
        {
            FromY = fromY;
            ToY = toY;
            StartTick = startTick;
            BlocksPerTick = blocksPerTick;
        }
    }

    public static class LiftTrajectory
    {
        public static float YAt(in LiftSegment s, uint tick)
        {
            if (tick <= s.StartTick || s.BlocksPerTick <= 0f)
                return s.FromY;

            float travel = (tick - s.StartTick) * s.BlocksPerTick;
            if (s.ToY >= s.FromY)
            {
                float y = s.FromY + travel;
                return y >= s.ToY ? s.ToY : y;
            }
            else
            {
                float y = s.FromY - travel;
                return y <= s.ToY ? s.ToY : y;
            }
        }

        public static uint ArrivalTick(in LiftSegment s)
        {
            float dist = Math.Abs(s.ToY - s.FromY);
            if (dist <= 0f)
                return s.StartTick;
            if (s.BlocksPerTick <= 0f)
                return uint.MaxValue;
            return s.StartTick + (uint)MathF.Ceiling(dist / s.BlocksPerTick);
        }

        public static float DeltaAt(in LiftSegment s, uint tick)
        {
            if (tick <= s.StartTick)
                return 0f;
            return YAt(in s, tick) - YAt(in s, tick - 1);
        }
    }
}
