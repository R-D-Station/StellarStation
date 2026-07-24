using Shared.Simulation.Blocks;

namespace Shared.World.Blocks
{
    /// <summary>Определяет ячейку покоя предмета при свободном падении — общий алгоритм сервера и клиента (см. ItemView.BlockSurface).</summary>
    public static class ItemGroundSnap
    {
        public const int MaxScanDepth = 64;
        private const float FullTop = 0.999f; // порог "почти полный бокс" — считаем верхней твёрдой поверхностью (полблок/четверть-ступень — нет)

        /// <summary>Сканирует вниз от startY по тем же коллизионным боксам, что движение, и возвращает ячейку Y, где предмет ляжет (без движения — та же startY).</summary>
        public static int SnapDown(IBlockSampler grid, IBlockShapes shapes, int x, int startY, int z)
        {
            for (int y = startY; y > startY - MaxScanDepth; y--)
            {
                var boxes = shapes.GetBoxes(grid.GetBlock(x, y, z), grid.GetState(x, y, z));
                if (boxes.Length == 0) continue;

                float partialTop = 0f;
                bool full = false;
                for (int i = 0; i < boxes.Length; i++)
                {
                    float top = boxes[i].MaxYf;
                    if (top >= FullTop) full = true;
                    else if (top > partialTop) partialTop = top;
                }

                if (full) return y + 1;    // полный/топ-слэб — предмет ложится в клетку НАД поверхностью
                if (partialTop > 0f) return y; // частичный степ (полблок/четверть) — предмет остаётся В этой клетке
            }
            return startY; // ничего не найдено (вакуум/превышен MaxScanDepth) — предмет остаётся на старте
        }
    }
}
