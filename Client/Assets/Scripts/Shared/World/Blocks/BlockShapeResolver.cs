namespace Shared.World.Blocks
{
    /// <summary>
    /// Форма и поворот соединяемого блока по 4 план-соседям (автотайл, наследник tile-WallConnectivity).
    /// Конвенция мешей: базовый меш «смотрит» соединением на СЕВЕР (+Z); поворот — шагами 90° по часовой
    /// (шаг = поворот вокруг Unity-Y). Corner-база соединяет N+E; T-база — всех, кроме запада.
    /// </summary>
    public static class BlockShapeResolver
    {
        public const int North = 1; // +Z
        public const int East = 2;  // +X
        public const int South = 4; // −Z
        public const int West = 8;  // −X

        /// <summary>Маска соседей → (форма, шаги поворота 90° по часовой).</summary>
        public static (WallShape shape, int rotSteps) Resolve(int mask)
        {
            switch (mask)
            {
                case 0: return (WallShape.Single, 0);

                case North: return (WallShape.End, 0);
                case East: return (WallShape.End, 1);
                case South: return (WallShape.End, 2);
                case West: return (WallShape.End, 3);

                case North | South: return (WallShape.Straight, 0);
                case East | West: return (WallShape.Straight, 1);

                case North | East: return (WallShape.Corner, 0);
                case East | South: return (WallShape.Corner, 1);
                case South | West: return (WallShape.Corner, 2);
                case West | North: return (WallShape.Corner, 3);

                case North | East | South: return (WallShape.T, 0); // нет запада
                case East | South | West: return (WallShape.T, 1);  // нет севера
                case South | West | North: return (WallShape.T, 2); // нет востока
                case West | North | East: return (WallShape.T, 3);  // нет юга

                default: return (WallShape.Cross, 0);
            }
        }
    }
}
