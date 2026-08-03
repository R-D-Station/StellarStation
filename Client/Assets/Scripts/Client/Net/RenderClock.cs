using System;

namespace Client.Net
{
    public sealed class RenderClock
    {
        public const float MaxDriftTicks = 8f;
        public const float MaxCorrectionRate = 0.05f;

        private bool _seeded;
        private float _accumulator;
        private float _correctionTicks;

        public uint Tick { get; private set; }

        public float Alpha { get; private set; }

        public float RenderTick => Tick + Alpha;

        public bool Seeded => _seeded;

        public float PendingCorrection => _correctionTicks;

        public void Advance(float dt, float tickInterval)
        {
            if (!(dt > 0f) || !(tickInterval > 0f))
                return;

            float elapsed = dt / tickInterval;
            float absorb = 0f;
            if (_correctionTicks != 0f)
            {
                float budget = MaxCorrectionRate * elapsed;
                absorb = _correctionTicks > 0f
                    ? MathF.Min(budget, _correctionTicks)
                    : MathF.Max(-budget, _correctionTicks);
                _correctionTicks -= absorb;
            }

            _accumulator += elapsed + absorb;
            while (_accumulator >= 1f)
            {
                _accumulator -= 1f;
                Tick++;
            }
            Alpha = _accumulator;
        }

        public void SyncToServer(uint serverTick)
        {
            uint target = serverTick > 0u ? serverTick - 1u : 0u;

            if (!_seeded)
            {
                Reseed(target);
                return;
            }

            float drift = (long)target - (long)Tick - _accumulator;
            if (drift > MaxDriftTicks || drift < -MaxDriftTicks)
            {
                Reseed(target);
                return;
            }
            _correctionTicks = drift;
        }

        private void Reseed(uint target)
        {
            _seeded = true;
            Tick = target;
            _accumulator = 0f;
            Alpha = 0f;
            _correctionTicks = 0f;
        }
    }
}
