using System;

namespace Shared.Simulation
{
    /// <summary>Правила адресной интеракции: дальность по ЦЕЛОМУ тайлу (chebyshev + тот же этаж). Зовут ОБЕ стороны.</summary>
    public static class InteractionRules
    {
        /// <summary>Радиус досягаемости в тайлах (chebyshev): 1 = свой тайл + 8 соседей.</summary>
        public const int InteractionRange = 1;

        /// <summary>Достижим ли тайл (tx,ty,tz) из клетки игрока (px,py,pz): chebyshev ≤ Range и тот же этаж.</summary>
        public static bool InReach(int px, int py, int pz, int tx, int ty, int tz)
        {
            if (pz != tz) return false;
            int dx = Math.Abs(px - tx);
            int dy = Math.Abs(py - ty);
            return (dx > dy ? dx : dy) <= InteractionRange;
        }
    }
}
