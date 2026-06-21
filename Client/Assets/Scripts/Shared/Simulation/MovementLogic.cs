using System;
using Shared.Messages.Core;
using Shared.World;

namespace Shared.Simulation
{
    /// <summary>
    /// Единая логика движения для сервера и клиента (предсказание). Общий код =
    /// детерминизм. Один вызов = один тик = один MoveIntent; движение суб-тайловое, Z не меняется.
    /// </summary>
    public static class MovementLogic
    {
        /// <summary>Базовый шаг за тик при ходьбе.</summary>
        public const float StepPerTick = 0.1f;

        /// <summary>Множитель шага при беге.</summary>
        public const float SprintMultiplier = 2f;

        /// <summary>1/√2 — нормализация диагонали, чтобы она не была быстрее прямого хода.</summary>
        public const float InvSqrt2 = 0.70710677f;

        /// <summary>Применить один шаг движения к позиции (X/Y суб-тайловые, Z не меняется).</summary>
        public static void Apply(GridMap map, int z, ref float x, ref float y, IntentDirection dir, bool sprint)
        {
            GetAxes(dir, out int dx, out int dy);
            if (dx == 0 && dy == 0) return;

            float step = StepPerTick * (sprint ? SprintMultiplier : 1f);
            if (dx != 0 && dy != 0) step *= InvSqrt2; // диагональ не быстрее прямого хода

            // Оси раздельно: упёрлись по одной — скользим вдоль стены по другой.
            if (dx != 0) MoveAxisX(map, z, ref x, ref y, dx * step);
            if (dy != 0) MoveAxisY(map, z, ref x, ref y, dy * step);
        }

        // Ход по X. Если тело упёрлось, но впереди реально проход — подцентровываемся
        // по Y к центру строки, чтобы проскользнуть в проём. У сплошной стены — стоп.
        private static void MoveAxisX(GridMap map, int z, ref float x, ref float y, float dx)
        {
            float nx = x + dx;
            if (!IsBlocked(map, nx, y, z)) { x = nx; return; }

            int col = (int)MathF.Floor(nx + (dx > 0 ? CollisionRadius : -CollisionRadius));
            int row = (int)MathF.Floor(y);
            if (map == null || !map.GetTile(col, row, z).Walkable) return; // впереди стена

            float ny = y + Math.Clamp((row + 0.5f) - y, -StepPerTick, StepPerTick);
            if (ny != y && !IsBlocked(map, x, ny, z)) y = ny;   // подцентровались по Y
            if (!IsBlocked(map, nx, y, z)) x = nx;              // пробуем вперёд
        }

        // Ход по Y (симметрично MoveAxisX: подцентровка по X).
        private static void MoveAxisY(GridMap map, int z, ref float x, ref float y, float dy)
        {
            float ny = y + dy;
            if (!IsBlocked(map, x, ny, z)) { y = ny; return; }

            int row = (int)MathF.Floor(ny + (dy > 0 ? CollisionRadius : -CollisionRadius));
            int col = (int)MathF.Floor(x);
            if (map == null || !map.GetTile(col, row, z).Walkable) return; // впереди стена

            float nx = x + Math.Clamp((col + 0.5f) - x, -StepPerTick, StepPerTick);
            if (nx != x && !IsBlocked(map, nx, y, z)) x = nx;   // подцентровались по X
            if (!IsBlocked(map, x, ny, z)) y = ny;              // пробуем вперёд
        }

        /// <summary>Разложить направление на оси: dx, dy ∈ {-1, 0, 1}.</summary>
        public static void GetAxes(IntentDirection dir, out int dx, out int dy)
        {
            dx = 0;
            dy = 0;
            switch (dir)
            {
                case IntentDirection.North: dy = 1; break;
                case IntentDirection.South: dy = -1; break;
                case IntentDirection.East: dx = 1; break;
                case IntentDirection.West: dx = -1; break;
                case IntentDirection.NorthEast: dx = 1; dy = 1; break;
                case IntentDirection.NorthWest: dx = -1; dy = 1; break;
                case IntentDirection.SouthEast: dx = 1; dy = -1; break;
                case IntentDirection.SouthWest: dx = -1; dy = -1; break;
            }
        }

        /// <summary>Половина «тела» игрока в тайлах (AABB-радиус). Строго &lt; 0.5, иначе застрянет в коридоре шириной 1.</summary>
        public const float CollisionRadius = 0.45f;

        // Задевает ли «тело» игрока (квадрат 2*CollisionRadius вокруг cx,cy) непроходимый тайл на этаже z.
        private static bool IsBlocked(GridMap map, float cx, float cy, int z)
        {
            if (map == null || map.Chunks.Count == 0)
                return false;

            int minX = (int)MathF.Floor(cx - CollisionRadius);
            int maxX = (int)MathF.Floor(cx + CollisionRadius);
            int minY = (int)MathF.Floor(cy - CollisionRadius);
            int maxY = (int)MathF.Floor(cy + CollisionRadius);

            for (int tx = minX; tx <= maxX; tx++)
                for (int ty = minY; ty <= maxY; ty++)
                    if (!map.GetTile(tx, ty, z).Walkable)
                        return true;

            return false;
        }

        /// <summary>IntentDirection → facing-байт Entity.Direction (N=0, S=1, E=2, W=3). None сохраняет текущий.</summary>
        public static byte ToFacing(IntentDirection dir, byte currentFacing)
        {
            switch (dir)
            {
                case IntentDirection.North: return 0;
                case IntentDirection.South: return 1;
                case IntentDirection.East:
                case IntentDirection.NorthEast:
                case IntentDirection.SouthEast: return 2; // диагональ → горизонталь
                case IntentDirection.West:
                case IntentDirection.NorthWest:
                case IntentDirection.SouthWest: return 3;
                default: return currentFacing;            // None — не вращаем
            }
        }
    }
}