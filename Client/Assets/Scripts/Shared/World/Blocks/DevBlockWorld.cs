using Shared.Simulation.Blocks;

namespace Shared.World.Blocks
{
    /// <summary>
    /// Детерминированный дев-полигон блок-мира (B2): строится КОДОМ одинаково на сервере и клиенте —
    /// это фолбэк, когда по MapPath нет карты v10. Формы блоков — собственные (Shapes), НЕ из каталога:
    /// полигон не зависит от авторского контента. Оси как в Unity: X/Z — план, Y — высота.
    /// </summary>
    public static class DevBlockWorld
    {
        public const ushort Full = 1;
        public const ushort Slab = 2;
        public const ushort HalfStep = 3;
        public const ushort QuarterStep = 4;

        public const float SpawnX = 0.5f;
        public const float SpawnY = 1f; // поверхность слаб-пола y=0
        public const float SpawnZ = 0.5f;

        /// <summary>Формы дев-типов (движение и дебаг-рендер обязаны использовать ИХ, не каталог).</summary>
        public static readonly IBlockShapes Shapes = new DevShapes();

        private sealed class DevShapes : IBlockShapes
        {
            private static readonly BlockBox[] None = System.Array.Empty<BlockBox>();
            private static readonly BlockBox[] FullBox = { BlockBox.Full };
            private static readonly BlockBox[] SlabBox = { BlockBox.SlabTop };
            private static readonly BlockBox[] HalfBox = { new BlockBox(0, 0, 0, 16, 8, 16) };
            private static readonly BlockBox[] QuarterBox = { new BlockBox(0, 0, 0, 16, 4, 16) };

            public BlockBox[] GetBoxes(ushort type, byte state) => type switch
            {
                Full => FullBox,
                Slab => SlabBox,
                HalfStep => HalfBox,
                QuarterStep => QuarterBox,
                _ => None
            };
        }

        /// <summary>Построить полигон: пол, лесенка 0.25/0.5, уступ 1.0, стена 2, кроул-навес 0.75, яма.</summary>
        public static void Build(BlockGrid g)
        {
            // Основание: слаб-пол (поверхность y=1).
            for (int x = -8; x <= 24; x++)
                for (int z = -8; z <= 8; z++)
                    g.SetBlock(x, 0, z, Slab);

            // Лесенка автошага: +0.25, затем +0.5.
            for (int z = -2; z <= 2; z++)
            {
                g.SetBlock(4, 1, z, QuarterStep);
                g.SetBlock(6, 1, z, HalfStep);
            }

            // Уступ высотой 1 блок (верх 2.0) — берётся прыжком.
            for (int x = 10; x <= 12; x++)
                for (int z = -2; z <= 2; z++)
                    g.SetBlock(x, 1, z, Full);

            // Стена высотой 2 (верх 3.0) — непреодолима.
            for (int z = -4; z <= 4; z++)
            {
                g.SetBlock(16, 1, z, Full);
                g.SetBlock(16, 2, z, Full);
            }

            // Кроул-навес: слабы над полом (низ 1.75) — зазор 0.75, только ползком.
            for (int x = 19; x <= 21; x++)
                for (int z = -2; z <= 2; z++)
                    g.SetBlock(x, 1, z, Slab);

            // Яма: вырез в основании + дно на y=-4 (поверхность −3).
            for (int x = -4; x <= -2; x++)
                for (int z = -1; z <= 1; z++)
                {
                    g.SetBlock(x, 0, z, 0);
                    g.SetBlock(x, -4, z, Slab);
                }

            g.ClearDirty(); // построение — не рантайм-изменения
        }
    }
}
