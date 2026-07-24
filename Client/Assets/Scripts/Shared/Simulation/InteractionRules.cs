using System;

namespace Shared.Simulation
{
    /// <summary>Правила адресной интеракции: дальность по ЦЕЛОМУ тайлу (chebyshev + тот же этаж). Зовут ОБЕ стороны.</summary>
    public static class InteractionRules
    {
        /// <summary>Радиус досягаемости в тайлах (chebyshev): 1 = свой тайл + 8 соседей.</summary>
        public const int InteractionRange = 1;

        /// <summary>Блок-мир: достижимость с вертикальным допуском ±1 блок по высоте (предмет на слабе/полу рядом) + chebyshev
        /// по плану. Паритет с клиентским пикером (|floor(view.y) − z| ≤ 1); в блок-мире Z — целая ячейка высоты, не этаж.</summary>
        public static bool InReachBlocks(int px, int py, int pz, int tx, int ty, int tz)
        {
            if (Math.Abs(pz - tz) > 1) return false;
            int dx = Math.Abs(px - tx);
            int dy = Math.Abs(py - ty);
            return (dx > dy ? dx : dy) <= InteractionRange;
        }
    }
}
