using System;
using System.Collections.Generic;
using Shared.World;

namespace Shared.Simulation
{
    /// <summary>
    /// Полигон видимости (2D): лучи в углы стен дают ровные рёбра теней.
    /// Результат — граничные точки в мировых координатах по кругу (веер треугольников).
    /// </summary>
    public static class Visibility
    {
        public readonly struct Vec2
        {
            public readonly float X;
            public readonly float Y;
            public Vec2(float x, float y) { X = x; Y = y; }
        }

        private readonly struct Seg
        {
            public readonly float Ax, Ay, Bx, By;
            public Seg(float ax, float ay, float bx, float by) { Ax = ax; Ay = ay; Bx = bx; By = by; }
        }

        /// <summary>Построить полигон видимости из (ox,oy) на этаже z в радиусе radius.</summary>
        public static List<Vec2> ComputePolygon(GridMap map, float ox, float oy, int z, float radius)
        {
            var segs = new List<Seg>();
            var corners = new List<Vec2>();
            GatherSegments(map, ox, oy, z, radius, segs, corners);

            const float eps = 0.0001f;
            var hits = new List<(float ang, float x, float y)>(corners.Count * 3 + 12);

            foreach (var c in corners)
            {
                float baseAng = MathF.Atan2(c.Y - oy, c.X - ox);
                for (int k = -1; k <= 1; k++)
                {
                    float ang = baseAng + k * eps;
                    CastRay(ox, oy, ang, radius, segs, out float hx, out float hy);
                    hits.Add((ang, hx, hy));
                }
            }

            hits.Sort((a, b) => a.ang.CompareTo(b.ang));

            var poly = new List<Vec2>(hits.Count);
            foreach (var h in hits)
                poly.Add(new Vec2(h.x, h.y));
            return poly;
        }

        private static void GatherSegments(GridMap map, float ox, float oy, int z, float radius,
            List<Seg> segs, List<Vec2> corners)
        {
            int minX = (int)MathF.Floor(ox - radius);
            int maxX = (int)MathF.Floor(ox + radius);
            int minY = (int)MathF.Floor(oy - radius);
            int maxY = (int)MathF.Floor(oy + radius);

            for (int ty = minY; ty <= maxY; ty++)
                for (int tx = minX; tx <= maxX; tx++)
                {
                    if (!Blocks(map, tx, ty, z)) continue;

                    // Силуэт: ребро добавляем, только если сосед за ним НЕ стена.
                    if (!Blocks(map, tx, ty - 1, z)) AddSeg(segs, corners, tx, ty, tx + 1, ty);         // низ
                    if (!Blocks(map, tx, ty + 1, z)) AddSeg(segs, corners, tx, ty + 1, tx + 1, ty + 1); // верх
                    if (!Blocks(map, tx - 1, ty, z)) AddSeg(segs, corners, tx, ty, tx, ty + 1);         // лево
                    if (!Blocks(map, tx + 1, ty, z)) AddSeg(segs, corners, tx + 1, ty, tx + 1, ty + 1); // право
                }

            // Граница радиуса: лучи в открытое упираются в этот квадрат.
            float x0 = ox - radius, x1 = ox + radius, y0 = oy - radius, y1 = oy + radius;
            AddSeg(segs, corners, x0, y0, x1, y0);
            AddSeg(segs, corners, x1, y0, x1, y1);
            AddSeg(segs, corners, x1, y1, x0, y1);
            AddSeg(segs, corners, x0, y1, x0, y0);
        }

        private static void AddSeg(List<Seg> segs, List<Vec2> corners, float ax, float ay, float bx, float by)
        {
            segs.Add(new Seg(ax, ay, bx, by));
            corners.Add(new Vec2(ax, ay));
            corners.Add(new Vec2(bx, by));
        }

        private static bool Blocks(GridMap map, int x, int y, int z)
        {
            if (map == null) return false;
            var t = map.GetTile(x, y, z);
            return t.BlocksHorizontalSight && !(t.DoorType != 0 && t.DoorOpen);
        }

        // Ближайшее пересечение луча (origin, угол) с сегментами; не дальше radius.
        // Всегда возвращает точку (на radius, если ничего не задело).
        private static void CastRay(float ox, float oy, float ang, float radius, List<Seg> segs,
            out float hx, out float hy)
        {
            float dx = MathF.Cos(ang), dy = MathF.Sin(ang);
            float v3x = -dy, v3y = dx; // перпендикуляр направления
            float best = radius;

            foreach (var s in segs)
            {
                float v2x = s.Bx - s.Ax, v2y = s.By - s.Ay;
                float denom = v2x * v3x + v2y * v3y;
                if (MathF.Abs(denom) < 1e-9f) continue; // параллельно

                float v1x = ox - s.Ax, v1y = oy - s.Ay;
                float t1 = (v2x * v1y - v2y * v1x) / denom; // расстояние вдоль луча
                float t2 = (v1x * v3x + v1y * v3y) / denom; // параметр вдоль сегмента
                if (t1 >= 0f && t1 < best && t2 >= 0f && t2 <= 1f)
                    best = t1;
            }

            hx = ox + dx * best;
            hy = oy + dy * best;
        }
    }
}
