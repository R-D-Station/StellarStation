using Shared.Simulation.Blocks;

namespace Client.Lifts
{
    public static class LiftMotion
    {
        public static float Fraction(float subTickAlpha)
            => subTickAlpha < 0f ? 0f : (subTickAlpha > 1f ? 1f : subTickAlpha);

        public static float RenderTick(uint tick, float subTickAlpha)
            => tick + Fraction(subTickAlpha);

        public static float YAtFraction(in LiftSegment s, float tick)
        {
            float dt = tick - s.StartTick;
            if (dt <= 0f || s.BlocksPerTick <= 0f)
                return s.FromY;

            float travel = dt * s.BlocksPerTick;
            if (s.ToY >= s.FromY)
            {
                float y = s.FromY + travel;
                return y >= s.ToY ? s.ToY : y;
            }

            float down = s.FromY - travel;
            return down <= s.ToY ? s.ToY : down;
        }

        public static float VisualY(in LiftSegment s, float renderTick)
            => YAtFraction(in s, renderTick);
    }
}
