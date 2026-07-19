namespace Shared.Simulation.Blocks
{
    /// <summary>
    /// Константы воксельного движения (фаза B). Все — const в Shared: детерминизм = одинаковые биты
    /// на клиенте и сервере; тюнинг через пересборку обеих сторон (решение в.42). Единицы: блоки и тики.
    /// </summary>
    public static class BlockMovementConfig
    {
        /// <summary>Полуширина AABB игрока (0.8 в ширину).</summary>
        public const float HalfWidth = 0.4f;

        /// <summary>Рост стоя (проём 1 блок непроходим, дверной проём — 2 блока).</summary>
        public const float StandHeight = 1.4f;

        /// <summary>Рост лёжа: подпол 0.75 и вентиляция проходимы ползком.</summary>
        public const float CrawlHeight = 0.45f;

        /// <summary>Перепад, который берётся ногами без прыжка (слаб 0.25, полублок 0.5).</summary>
        public const float AutoStep = 0.5f;

        /// <summary>Начальная скорость прыжка (блоков/тик): апекс ≈ 1.10 — 1 блок берётся, 1.25 нет.</summary>
        public const float JumpVelocity = 0.26f;

        /// <summary>Ускорение падения (блоков/тик²).</summary>
        public const float Gravity = 0.0275f;

        /// <summary>Предельная скорость падения (блоков/тик).</summary>
        public const float TerminalVelocity = 0.9f;

        /// <summary>Допуск сравнения поверхностей (поверхности квантованы 1/16 — допуск много меньше).</summary>
        public const float Epsilon = 1f / 1024f;
    }
}
