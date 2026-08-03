namespace Shared.Simulation.Blocks
{
    public static class LiftScanQueue
    {
        public const int MaxFloors = 32;

        public static uint Add(uint calls, int floor)
            => (uint)floor < MaxFloors ? calls | (1u << floor) : calls;

        public static uint Clear(uint calls, int floor)
            => (uint)floor < MaxFloors ? calls & ~(1u << floor) : calls;

        public static bool Has(uint calls, int floor)
            => (uint)floor < MaxFloors && (calls & (1u << floor)) != 0u;

        public static int Next(uint calls, int floor, int dir, int stopCount)
        {
            if (calls == 0u)
                return -1;
            if (stopCount > MaxFloors)
                stopCount = MaxFloors;
            if (Has(calls, floor))
                return floor;

            int step = dir >= 0 ? 1 : -1;
            int found = Sweep(calls, floor, step, stopCount);
            return found >= 0 ? found : Sweep(calls, floor, -step, stopCount);
        }

        private static int Sweep(uint calls, int floor, int step, int stopCount)
        {
            for (int f = floor + step; f >= 0 && f < stopCount; f += step)
                if (Has(calls, f))
                    return f;
            return -1;
        }

        public static bool CanStillStop(float y, float floorY, int dir, float blocksPerTick)
        {
            if (blocksPerTick <= 0f)
                return false;
            if (dir > 0)
                return y + blocksPerTick <= floorY;
            if (dir < 0)
                return y - blocksPerTick >= floorY;
            return y == floorY;
        }

        public static int NextEnRoute(uint calls, float y, float[] stopY, int dir, int target, float blocksPerTick)
        {
            if (calls == 0u || stopY == null || dir == 0)
                return -1;
            if ((uint)target >= (uint)stopY.Length)
                return -1;

            int count = stopY.Length > MaxFloors ? MaxFloors : stopY.Length;
            float targetY = stopY[target];
            int best = -1;
            float bestY = 0f;
            for (int f = 0; f < count; f++)
            {
                if (!Has(calls, f))
                    continue;
                float fy = stopY[f];
                if (dir > 0 ? fy > targetY : fy < targetY)
                    continue;
                if (!CanStillStop(y, fy, dir, blocksPerTick))
                    continue;
                if (best < 0 || (dir > 0 ? fy < bestY : fy > bestY))
                {
                    best = f;
                    bestY = fy;
                }
            }
            return best;
        }
    }
}
