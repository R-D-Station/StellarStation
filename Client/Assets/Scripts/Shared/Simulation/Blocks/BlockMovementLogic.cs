using System;
using Shared.World.Blocks;

namespace Shared.Simulation.Blocks
{
    /// <summary>
    /// Воксельное движение (фаза B): AABB игрока против коллизионных боксов блоков. Оси как в Unity:
    /// X/Z — план, Y — высота. Единая логика сервера и клиента — детерминизм критичен (только
    /// +,*,сравнения; поверхности квантованы 1/16). Один вызов Step = один тик. Порядок фаз тика
    /// фиксирован: план (X, затем Z, автошаг с опоры) → проверка опоры → прыжок → гравитация →
    /// вертикаль. Скорости — из MovementLogic (StepPerTick/Sprint/Crawl/диагональ); IntentDirection:
    /// North = +Z плана. Отсутствие секции = Air.
    /// </summary>
    public static class BlockMovementLogic
    {
        /// <summary>Применить один тик ввода к состоянию. Мир — через IBlockSampler (клиентский стопор фронтира).</summary>
        public static void Step(IBlockSampler grid, IBlockShapes shapes, ref BlockMoverState s, in BlockMoveInput input, float baseStep, IDynamicObstacles dyn = null)
        {
            float height = input.Crawl ? BlockMovementConfig.CrawlHeight : BlockMovementConfig.StandHeight;

            MovementLogic.GetAxes(input.Dir, out int dx, out int dz);
            if (dx != 0 || dz != 0)
            {
                float step = baseStep * (input.Sprint ? MovementLogic.SprintMultiplier : 1f);
                if (dx != 0 && dz != 0) step *= MovementLogic.InvSqrt2;
                if (input.Crawl) step *= MovementLogic.CrawlMultiplier;

                if (dx != 0) MoveAxis(grid, shapes, ref s, dx * step, 0f, height, dyn);
                if (dz != 0) MoveAxis(grid, shapes, ref s, 0f, dz * step, height, dyn);
            }

            // Опора могла уйти после плана (сошли с края).
            if (s.Grounded && !HasSupport(grid, shapes, s.X, s.Y, s.Z, dyn))
                s.Grounded = false;

            if (input.Jump && s.Grounded && !input.Crawl)
            {
                s.VY = BlockMovementConfig.JumpVelocity;
                s.Grounded = false;
            }

            if (s.Grounded)
            {
                s.VY = 0f;
                return;
            }

            s.VY = Math.Max(s.VY - BlockMovementConfig.Gravity, -BlockMovementConfig.TerminalVelocity);
            MoveVertical(grid, shapes, ref s, height, dyn);
        }

        /// <summary>Хватает ли высоты встать (для будущего FSM: запрет подъёма под низким потолком).</summary>
        public static bool CanStand(IBlockSampler grid, IBlockShapes shapes, float x, float y, float z, IDynamicObstacles dyn = null)
            => !Collides(grid, shapes, x, y, z, BlockMovementConfig.StandHeight, dyn);

        /// <summary>Вывод Grounded из позиции+VY (сид реконсиляции: Grounded не реплицируется).</summary>
        public static bool IsGrounded(IBlockSampler grid, IBlockShapes shapes, float x, float y, float z, float vy, IDynamicObstacles dyn = null)
            => vy == 0f && HasSupport(grid, shapes, x, y, z, dyn);

        public static bool CollidesBox(IBlockSampler grid, IBlockShapes shapes, float centerX, float y, float centerZ, float halfW, float height)
        {
            float minX = centerX - halfW, maxX = centerX + halfW;
            float minZ = centerZ - halfW, maxZ = centerZ + halfW;
            float maxY = y + height;

            int bx1 = FloorToInt(maxX), by1 = FloorToInt(maxY), bz1 = FloorToInt(maxZ);
            for (int bx = FloorToInt(minX); bx <= bx1; bx++)
                for (int bz = FloorToInt(minZ); bz <= bz1; bz++)
                    for (int by = FloorToInt(y); by <= by1; by++)
                    {
                        var boxes = shapes.GetBoxes(grid.GetBlock(bx, by, bz), grid.GetState(bx, by, bz));
                        for (int i = 0; i < boxes.Length; i++)
                        {
                            ref readonly var b = ref boxes[i];
                            if (minX < bx + b.MaxXf && maxX > bx + b.MinXf &&
                                minZ < bz + b.MaxZf && maxZ > bz + b.MinZf &&
                                y < by + b.MaxYf && maxY > by + b.MinYf)
                                return true;
                        }
                    }
            return false;
        }

        // Ход по одной оси плана; у препятствия — попытка автошага (только с опоры).
        private static void MoveAxis(IBlockSampler grid, IBlockShapes shapes, ref BlockMoverState s, float dx, float dz, float height, IDynamicObstacles dyn)
        {
            float nx = s.X + dx;
            float nz = s.Z + dz;
            if (!Collides(grid, shapes, nx, s.Y, nz, height, dyn))
            {
                s.X = nx;
                s.Z = nz;
                return;
            }

            if (!s.Grounded)
                return;

            float rise = RequiredRise(grid, shapes, nx, s.Y, nz, height, dyn);
            if (rise <= 0f || rise > BlockMovementConfig.AutoStep + BlockMovementConfig.Epsilon)
                return;
            if (Collides(grid, shapes, nx, s.Y + rise, nz, height, dyn))
                return; // над ступенью тесно (потолок/следующий ряд)

            s.X = nx;
            s.Z = nz;
            s.Y += rise; // встали на ступень, Grounded сохраняется
        }

        // Вертикальный свип на VY: вниз — приземление на высшую поверхность в пути, вверх — упор головой.
        private static void MoveVertical(IBlockSampler grid, IBlockShapes shapes, ref BlockMoverState s, float height, IDynamicObstacles dyn)
        {
            if (s.VY < 0f)
            {
                float target = s.Y + s.VY;
                float floor = HighestTopBelow(grid, shapes, s.X, s.Z, from: s.Y, to: target, dyn);
                if (floor >= target)
                {
                    s.Y = floor;
                    s.VY = 0f;
                    s.Grounded = true;
                }
                else
                {
                    s.Y = target;
                }
            }
            else if (s.VY > 0f)
            {
                float targetTop = s.Y + height + s.VY;
                float ceil = LowestBottomAbove(grid, shapes, s.X, s.Z, from: s.Y + height, to: targetTop, dyn);
                if (ceil < targetTop)
                {
                    s.Y = ceil - height;
                    s.VY = 0f; // упёрлись головой — дальше тянет гравитация
                }
                else
                {
                    s.Y += s.VY;
                }
            }
        }

        // Пересекает ли AABB (footprint HalfWidth в плане, ноги y, рост height) какой-либо бокс блока.
        // Касание граней — не пересечение.
        private static bool Collides(IBlockSampler grid, IBlockShapes shapes, float x, float y, float z, float height, IDynamicObstacles dyn)
        {
            float minX = x - BlockMovementConfig.HalfWidth, maxX = x + BlockMovementConfig.HalfWidth;
            float minZ = z - BlockMovementConfig.HalfWidth, maxZ = z + BlockMovementConfig.HalfWidth;
            float maxY = y + height;

            int bx1 = FloorToInt(maxX), by1 = FloorToInt(maxY), bz1 = FloorToInt(maxZ);
            for (int bx = FloorToInt(minX); bx <= bx1; bx++)
                for (int bz = FloorToInt(minZ); bz <= bz1; bz++)
                    for (int by = FloorToInt(y); by <= by1; by++)
                    {
                        var boxes = shapes.GetBoxes(grid.GetBlock(bx, by, bz), grid.GetState(bx, by, bz));
                        for (int i = 0; i < boxes.Length; i++)
                        {
                            ref readonly var b = ref boxes[i];
                            if (minX < bx + b.MaxXf && maxX > bx + b.MinXf &&
                                minZ < bz + b.MaxZf && maxZ > bz + b.MinZf &&
                                y < by + b.MaxYf && maxY > by + b.MinYf)
                                return true;
                        }
                    }

            if (dyn != null)
                for (int i = 0; i < dyn.Count; i++)
                {
                    dyn.Get(i, out float dminX, out float dminY, out float dminZ, out float dmaxX, out float dmaxY, out float dmaxZ);
                    if (minX < dmaxX && maxX > dminX &&
                        minZ < dmaxZ && maxZ > dminZ &&
                        y < dmaxY && maxY > dminY)
                        return true;
                }
            return false;
        }

        // Минимальный подъём, убирающий пересечение в целевой точке: max(top мешающих боксов) − y.
        // float.MaxValue, если мешает бокс выше AutoStep (стена) — автошаг невозможен.
        private static float RequiredRise(IBlockSampler grid, IBlockShapes shapes, float x, float y, float z, float height, IDynamicObstacles dyn)
        {
            float minX = x - BlockMovementConfig.HalfWidth, maxX = x + BlockMovementConfig.HalfWidth;
            float minZ = z - BlockMovementConfig.HalfWidth, maxZ = z + BlockMovementConfig.HalfWidth;
            float maxY = y + height;
            float rise = 0f;

            int bx1 = FloorToInt(maxX), by1 = FloorToInt(maxY), bz1 = FloorToInt(maxZ);
            for (int bx = FloorToInt(minX); bx <= bx1; bx++)
                for (int bz = FloorToInt(minZ); bz <= bz1; bz++)
                    for (int by = FloorToInt(y); by <= by1; by++)
                    {
                        var boxes = shapes.GetBoxes(grid.GetBlock(bx, by, bz), grid.GetState(bx, by, bz));
                        for (int i = 0; i < boxes.Length; i++)
                        {
                            ref readonly var b = ref boxes[i];
                            if (minX < bx + b.MaxXf && maxX > bx + b.MinXf &&
                                minZ < bz + b.MaxZf && maxZ > bz + b.MinZf &&
                                y < by + b.MaxYf && maxY > by + b.MinYf)
                            {
                                float top = by + b.MaxYf - y;
                                if (top > BlockMovementConfig.AutoStep + BlockMovementConfig.Epsilon)
                                    return float.MaxValue;
                                if (top > rise) rise = top;
                            }
                        }
                    }

            if (dyn != null)
                for (int i = 0; i < dyn.Count; i++)
                {
                    dyn.Get(i, out float dminX, out float dminY, out float dminZ, out float dmaxX, out float dmaxY, out float dmaxZ);
                    if (minX < dmaxX && maxX > dminX &&
                        minZ < dmaxZ && maxZ > dminZ &&
                        y < dmaxY && maxY > dminY)
                    {
                        float top = dmaxY - y;
                        if (top > BlockMovementConfig.AutoStep + BlockMovementConfig.Epsilon)
                            return float.MaxValue;
                        if (top > rise) rise = top;
                    }
                }
            return rise;
        }

        // Высшая solid-поверхность под футпринтом с top ∈ [to, from]; float.MinValue, если её нет.
        private static float HighestTopBelow(IBlockSampler grid, IBlockShapes shapes, float x, float z, float from, float to, IDynamicObstacles dyn)
        {
            float minX = x - BlockMovementConfig.HalfWidth, maxX = x + BlockMovementConfig.HalfWidth;
            float minZ = z - BlockMovementConfig.HalfWidth, maxZ = z + BlockMovementConfig.HalfWidth;
            float best = float.MinValue;

            int bx1 = FloorToInt(maxX), by1 = FloorToInt(from), bz1 = FloorToInt(maxZ);
            for (int bx = FloorToInt(minX); bx <= bx1; bx++)
                for (int bz = FloorToInt(minZ); bz <= bz1; bz++)
                    for (int by = FloorToInt(to) - 1; by <= by1; by++)
                    {
                        var boxes = shapes.GetBoxes(grid.GetBlock(bx, by, bz), grid.GetState(bx, by, bz));
                        for (int i = 0; i < boxes.Length; i++)
                        {
                            ref readonly var b = ref boxes[i];
                            if (minX < bx + b.MaxXf && maxX > bx + b.MinXf &&
                                minZ < bz + b.MaxZf && maxZ > bz + b.MinZf)
                            {
                                float top = by + b.MaxYf;
                                if (top <= from + BlockMovementConfig.Epsilon && top >= to && top > best)
                                    best = top;
                            }
                        }
                    }

            if (dyn != null)
                for (int i = 0; i < dyn.Count; i++)
                {
                    dyn.Get(i, out float dminX, out float dminY, out float dminZ, out float dmaxX, out float dmaxY, out float dmaxZ);
                    if (minX < dmaxX && maxX > dminX &&
                        minZ < dmaxZ && maxZ > dminZ)
                    {
                        float top = dmaxY;
                        if (top <= from + BlockMovementConfig.Epsilon && top >= to && top > best)
                            best = top;
                    }
                }
            return best;
        }

        // Низший потолок над головой с bottom ∈ [from, to); float.MaxValue, если его нет.
        private static float LowestBottomAbove(IBlockSampler grid, IBlockShapes shapes, float x, float z, float from, float to, IDynamicObstacles dyn)
        {
            float minX = x - BlockMovementConfig.HalfWidth, maxX = x + BlockMovementConfig.HalfWidth;
            float minZ = z - BlockMovementConfig.HalfWidth, maxZ = z + BlockMovementConfig.HalfWidth;
            float best = float.MaxValue;

            int bx1 = FloorToInt(maxX), by1 = FloorToInt(to), bz1 = FloorToInt(maxZ);
            for (int bx = FloorToInt(minX); bx <= bx1; bx++)
                for (int bz = FloorToInt(minZ); bz <= bz1; bz++)
                    for (int by = FloorToInt(from); by <= by1; by++)
                    {
                        var boxes = shapes.GetBoxes(grid.GetBlock(bx, by, bz), grid.GetState(bx, by, bz));
                        for (int i = 0; i < boxes.Length; i++)
                        {
                            ref readonly var b = ref boxes[i];
                            if (minX < bx + b.MaxXf && maxX > bx + b.MinXf &&
                                minZ < bz + b.MaxZf && maxZ > bz + b.MinZf)
                            {
                                float bottom = by + b.MinYf;
                                if (bottom >= from - BlockMovementConfig.Epsilon && bottom < to && bottom < best)
                                    best = bottom;
                            }
                        }
                    }

            if (dyn != null)
                for (int i = 0; i < dyn.Count; i++)
                {
                    dyn.Get(i, out float dminX, out float dminY, out float dminZ, out float dmaxX, out float dmaxY, out float dmaxZ);
                    if (minX < dmaxX && maxX > dminX &&
                        minZ < dmaxZ && maxZ > dminZ)
                    {
                        float bottom = dminY;
                        if (bottom >= from - BlockMovementConfig.Epsilon && bottom < to && bottom < best)
                            best = bottom;
                    }
                }
            return best;
        }

        // Опора: любая часть футпринта над solid-поверхностью на уровне ног (правило «любая опора держит»).
        private static bool HasSupport(IBlockSampler grid, IBlockShapes shapes, float x, float y, float z, IDynamicObstacles dyn)
        {
            float minX = x - BlockMovementConfig.HalfWidth, maxX = x + BlockMovementConfig.HalfWidth;
            float minZ = z - BlockMovementConfig.HalfWidth, maxZ = z + BlockMovementConfig.HalfWidth;

            int bx1 = FloorToInt(maxX), by1 = FloorToInt(y), bz1 = FloorToInt(maxZ);
            for (int bx = FloorToInt(minX); bx <= bx1; bx++)
                for (int bz = FloorToInt(minZ); bz <= bz1; bz++)
                    for (int by = FloorToInt(y) - 1; by <= by1; by++)
                    {
                        var boxes = shapes.GetBoxes(grid.GetBlock(bx, by, bz), grid.GetState(bx, by, bz));
                        for (int i = 0; i < boxes.Length; i++)
                        {
                            ref readonly var b = ref boxes[i];
                            if (minX < bx + b.MaxXf && maxX > bx + b.MinXf &&
                                minZ < bz + b.MaxZf && maxZ > bz + b.MinZf &&
                                Math.Abs(by + b.MaxYf - y) <= BlockMovementConfig.Epsilon)
                                return true;
                        }
                    }

            if (dyn != null)
                for (int i = 0; i < dyn.Count; i++)
                {
                    dyn.Get(i, out float dminX, out float dminY, out float dminZ, out float dmaxX, out float dmaxY, out float dmaxZ);
                    if (minX < dmaxX && maxX > dminX &&
                        minZ < dmaxZ && maxZ > dminZ &&
                        Math.Abs(dmaxY - y) <= BlockMovementConfig.Epsilon)
                        return true;
                }
            return false;
        }

        private static int FloorToInt(float v) => (int)MathF.Floor(v);
    }
}
