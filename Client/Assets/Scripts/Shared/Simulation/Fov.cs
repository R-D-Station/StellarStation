using System;
using Shared.World;

namespace Shared.Simulation
{
    /// <summary>
    /// Поле зрения (FOV) методом recursive shadowcasting. Стены отбрасывают тень.
    /// Результат — сетка «света» 0..1 в окне (2*radius+1) вокруг наблюдателя.
    /// </summary>
    public static class Fov
    {
        // Преобразование октанта (классический shadowcasting): (xx,xy,yx,yy) на октант.
        private static readonly int[] Xx = { 1, 0, 0, -1, -1, 0, 0, 1 };
        private static readonly int[] Xy = { 0, 1, -1, 0, 0, -1, 1, 0 };
        private static readonly int[] Yx = { 0, 1, 1, 0, 0, -1, -1, 0 };
        private static readonly int[] Yy = { 1, 0, 0, 1, -1, 0, 0, -1 };

        /// <summary>
        /// Посчитать видимость из (ox,oy) на этаже z в радиусе radius. light должен быть
        /// размера [2*radius+1, 2*radius+1]; индекс тайла (x,y): [x-ox+radius, y-oy+radius].
        /// </summary>
        public static void Compute(GridMap map, int ox, int oy, int z, int radius, float[,] light)
        {
            int size = 2 * radius + 1;
            for (int i = 0; i < size; i++)
                for (int j = 0; j < size; j++)
                    light[i, j] = 0f;

            light[radius, radius] = 1f; // тайл наблюдателя всегда виден
            for (int oct = 0; oct < 8; oct++)
                CastLight(map, ox, oy, z, radius, light, 1, 1.0f, 0.0f, oct);
        }

        private static void CastLight(GridMap map, int ox, int oy, int z, int radius, float[,] light,
            int row, float start, float end, int oct)
        {
            if (start < end) return;
            int r2 = radius * radius;
            int size = 2 * radius + 1;

            for (int j = row; j <= radius; j++)
            {
                int dx = -j - 1, dy = -j;
                bool blocked = false;
                float newStart = 0f;

                while (dx <= 0)
                {
                    dx++;
                    int X = ox + dx * Xx[oct] + dy * Xy[oct];
                    int Y = oy + dx * Yx[oct] + dy * Yy[oct];
                    float lSlope = (dx - 0.5f) / (dy + 0.5f);
                    float rSlope = (dx + 0.5f) / (dy - 0.5f);

                    if (start < rSlope) continue;
                    if (end > lSlope) break;

                    int d2 = dx * dx + dy * dy;
                    if (d2 <= r2)
                    {
                        int gx = X - ox + radius, gy = Y - oy + radius;
                        if (gx >= 0 && gx < size && gy >= 0 && gy < size)
                        {
                            float intensity = 1f - (float)Math.Sqrt(d2) / (radius + 1);
                            if (intensity > light[gx, gy]) light[gx, gy] = intensity;
                        }
                    }

                    bool wall = BlocksSight(map, X, Y, z);
                    if (blocked)
                    {
                        if (wall) { newStart = rSlope; continue; }
                        blocked = false;
                        start = newStart;
                    }
                    else if (wall && j < radius)
                    {
                        blocked = true;
                        CastLight(map, ox, oy, z, radius, light, j + 1, start, lSlope, oct);
                        newStart = rSlope;
                    }
                }

                if (blocked) break;
            }
        }

        /// <summary>Держит ли тайл обзор. Открытый объект (дверь/люк) — НЕ держит (видно сквозь).</summary>
        private static bool BlocksSight(GridMap map, int x, int y, int z)
        {
            if (map == null) return false;
            var t = map.GetTile(x, y, z);
            return t.BlocksHorizontalSight && !(t.Openable && t.Open);
        }
    }
}
