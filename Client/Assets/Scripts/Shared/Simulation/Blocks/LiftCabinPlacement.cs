using System;
using Shared.World.Blocks;

namespace Shared.Simulation.Blocks
{
    /// <summary>Бокс кабины в УГЛОВОМ object-space модуля: CenterX/CenterZ — от УГЛА планового прямоугольника
    /// шахты; Y — от ПОЛА КАБИНЫ (абсолютную высоту даёт сегмент), НЕ от пола этажа и НЕ мировая координата.</summary>
    public readonly struct LiftBox
    {
        public readonly float CenterX, Y, CenterZ, HalfX, HalfZ, Height;

        public LiftBox(float centerX, float y, float centerZ, float halfX, float halfZ, float height)
        {
            CenterX = centerX;
            Y = y;
            CenterZ = centerZ;
            HalfX = halfX;
            HalfZ = halfZ;
            Height = height;
        }
    }

    /// <summary>Размещение кабины лифта: позиция в плане ВЫВОДИТСЯ от якоря рельса, размер задаёт автор боксами.
    /// Общая точка для сервера и редактора — редактор обязан рисовать кабину там, где её поставит движок.</summary>
    public static class LiftCabinPlacement
    {
        /// <summary>Плановый прямоугольник шахты (включительно) по якорю рельса, габариту модуля и facing.</summary>
        public static void PlanRect(int railX, int railZ, int moduleX, int moduleZ, int facing,
            out int x0, out int x1, out int z0, out int z1)
        {
            int minDX = int.MaxValue, maxDX = int.MinValue, minDZ = int.MaxValue, maxDZ = int.MinValue;
            for (int w = 0; w < Math.Max(1, moduleX); w++)
                for (int d = 0; d < Math.Max(1, moduleZ); d++)
                {
                    MultiBlock.RotateLocal(w, d, facing, out int dx, out int dz);
                    if (dx < minDX) minDX = dx;
                    if (dx > maxDX) maxDX = dx;
                    if (dz < minDZ) minDZ = dz;
                    if (dz > maxDZ) maxDZ = dz;
                }
            x0 = railX + minDX; x1 = railX + maxDX;
            z0 = railZ + minDZ; z1 = railZ + maxDZ;
        }

        /// <summary>Размер планового прямоугольника шахты (клеток) с учётом поворота: facing 1/3 меняет оси местами.</summary>
        public static void PlanSize(int moduleX, int moduleZ, int facing, out int planW, out int planD)
        {
            PlanRect(0, 0, moduleX, moduleZ, facing, out int x0, out int x1, out int z0, out int z1);
            planW = x1 - x0 + 1;
            planD = z1 - z0 + 1;
        }

        /// <summary>КОНВЕНЦИЯ ПИВОТА КАБИНЫ. Корень префаба = ПЛАН-ЦЕНТР модуля шахты на уровне ПОЛА КАБИНЫ:
        /// смещение от УГЛА планового прямоугольника = (planW*0.5, 0, planD*0.5), поворот = Euler(0, 90*facing, 0),
        /// Y = ровно YAt без слагаемых. <c>BlockDefinition.PivotYOffset</c> к кабине НЕ применяется.</summary>
        public static void PivotFromPlan(int planW, int planD, out float offX, out float offZ)
        {
            offX = (planW < 1 ? 1 : planW) * 0.5f;
            offZ = (planD < 1 ? 1 : planD) * 0.5f;
        }

        /// <summary>Мировая позиция КОРНЯ префаба, стоящего в плановом прямоугольнике шахты на высоте
        /// <paramref name="floorY"/>. Одна на всех обитателей шахты: кабина и модуль рельса делят
        /// прямоугольник, значит делят и пивот — второй конвенции быть не должно.</summary>
        public static void WorldPivot(float anchorX, float anchorZ, int planW, int planD, float floorY,
            out float x, out float y, out float z)
        {
            PivotFromPlan(planW, planD, out float offX, out float offZ);
            x = anchorX + offX;
            y = floorY;
            z = anchorZ + offZ;
        }

        /// <summary>Мировой AABB авторского бокса кабины, стоящей на этаже <paramref name="cabinFloorY"/>.</summary>
        public static void WorldBounds(int railX, int cabinFloorY, int railZ, int moduleX, int moduleZ, int facing,
            in TriggerBox box, out float minX, out float minY, out float minZ,
            out float maxX, out float maxY, out float maxZ)
            => AutoDoorLogic.TriggerWorldBounds(railX, cabinFloorY, railZ, in box, moduleX, moduleZ, facing,
                out minX, out minY, out minZ, out maxX, out maxY, out maxZ);

        /// <summary>Боксы кабины ОТНОСИТЕЛЬНО угла планового прямоугольника (якорь рельса сокращается).</summary>
        public static LiftBox[] LocalBoxes(int moduleX, int moduleZ, int facing, TriggerBox[] boxes)
        {
            if (boxes == null || boxes.Length == 0)
                return Array.Empty<LiftBox>();

            PlanRect(0, 0, moduleX, moduleZ, facing, out int x0, out _, out int z0, out _);
            var result = new LiftBox[boxes.Length];
            for (int i = 0; i < boxes.Length; i++)
            {
                WorldBounds(-x0, 0, -z0, moduleX, moduleZ, facing, in boxes[i],
                    out float minX, out float minY, out float minZ,
                    out float maxX, out float maxY, out float maxZ);
                result[i] = new LiftBox(
                    (minX + maxX) * 0.5f, minY, (minZ + maxZ) * 0.5f,
                    (maxX - minX) * 0.5f, (maxZ - minZ) * 0.5f, maxY - minY);
            }
            return result;
        }
    }
}
