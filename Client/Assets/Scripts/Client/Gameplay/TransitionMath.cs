namespace Client.Gameplay
{
    public static class TransitionMath
    {
        public static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        public static float StepTowards(float current, float target, float speed, float dt)
        {
            if (speed <= 0f || dt <= 0f)
                return speed <= 0f ? target : current;
            float step = speed * dt;
            float diff = target - current;
            if (diff > step) return current + step;
            if (diff < -step) return current - step;
            return target;
        }

        public static float StepAccelerated(float current, float target, float velocity,
                                            float acceleration, float dt, out float newVelocity)
        {
            if (acceleration <= 0f || dt <= 0f)
            {
                newVelocity = 0f;
                return acceleration <= 0f ? target : current;
            }

            float diff = target - current;
            if (diff == 0f)
            {
                newVelocity = 0f;
                return target;
            }

            newVelocity = velocity + acceleration * dt;
            float step = newVelocity * dt;
            float distance = diff < 0f ? -diff : diff;
            if (step >= distance)
            {
                newVelocity = 0f;
                return target;
            }
            return diff > 0f ? current + step : current - step;
        }

        public static float LerpChannel(float from, float to, float k) => from + (to - from) * k;
    }
}
