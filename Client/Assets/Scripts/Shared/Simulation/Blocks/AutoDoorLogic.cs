using System;
using Shared.World.Blocks;

namespace Shared.Simulation.Blocks
{
    /// <summary>Геометрия и таймер-автомат авто-двери: попадание игрока в триггер + open/close по занятости.</summary>
    public static class AutoDoorLogic
    {
        /// <summary>Мировой AABB триггера: поворот object-space бокса вокруг пивота футпринта на facing + сдвиг на позицию блока.</summary>
        public static void TriggerWorldBounds(int ax, int ay, int az, in TriggerBox t, int sx, int sz, int facing,
            out float minX, out float minY, out float minZ, out float maxX, out float maxY, out float maxZ)
        {
            MultiBlock.FootprintCenterOffset(sx, sz, facing, out float cx, out float cz);
            float px = sx * 0.5f, pz = sz * 0.5f; // пивот префаба в object-space

            RotatePivot(t.MinXf - px, t.MinZf - pz, facing, out float x0, out float z0);
            RotatePivot(t.MaxXf - px, t.MaxZf - pz, facing, out float x1, out float z1);

            minX = ax + cx + Math.Min(x0, x1);
            maxX = ax + cx + Math.Max(x0, x1);
            minZ = az + cz + Math.Min(z0, z1);
            maxZ = az + cz + Math.Max(z0, z1);
            minY = ay + t.MinYf;
            maxY = ay + t.MaxYf;
        }

        /// <summary>Пересекает ли AABB игрока (halfWidth в плане, ноги y, рост height) хоть один триггер двери.</summary>
        public static bool PlayerInTrigger(float px, float py, float pz, float halfWidth, float height,
            int ax, int ay, int az, TriggerBox[] triggers, int sx, int sz, int facing)
        {
            if (triggers == null || triggers.Length == 0)
                return false;

            float pMinX = px - halfWidth, pMaxX = px + halfWidth;
            float pMinZ = pz - halfWidth, pMaxZ = pz + halfWidth;
            float pMinY = py, pMaxY = py + height;

            for (int i = 0; i < triggers.Length; i++)
            {
                TriggerWorldBounds(ax, ay, az, in triggers[i], sx, sz, facing,
                    out float minX, out float minY, out float minZ, out float maxX, out float maxY, out float maxZ);
                if (pMaxX > minX && pMinX < maxX &&
                    pMaxZ > minZ && pMinZ < maxZ &&
                    pMaxY > minY && pMinY < maxY)
                    return true;
            }
            return false;
        }

        /// <summary>Тик авто-двери: по занятости триггера и дедлайну закрытия вычисляет новое состояние open/closeAt.</summary>
        public static void Tick(bool occupied, bool open, uint closeAtTick, uint currentTick, uint closeDelayTicks,
            out bool newOpen, out uint newCloseAt)
        {
            newOpen = open;
            newCloseAt = closeAtTick;
            if (occupied)
            {
                newOpen = true;             // открыть / держать открытой
                newCloseAt = uint.MaxValue; // отменить отсчёт закрытия
            }
            else if (open)
            {
                if (closeAtTick == uint.MaxValue)
                    newCloseAt = currentTick + (closeDelayTicks == 0u ? 1u : closeDelayTicks); // старт отсчёта
                else if (currentTick >= closeAtTick)
                    newOpen = false; // время вышло — закрыть
            }
        }

        // Поворот плановой точки (относительно пивота) на facing 90°-шагов; направление = Unity Euler(0,90·f,0):
        // +Z→+X, +X→−Z (по часовой сверху). Совпадает с MultiBlock.RotateLocal и FacingRotation визуала.
        private static void RotatePivot(float lx, float lz, int facing, out float rx, out float rz)
        {
            switch (facing & 3)
            {
                case 0: rx = lx; rz = lz; break;    // север
                case 1: rx = lz; rz = -lx; break;   // восток
                case 2: rx = -lx; rz = -lz; break;  // юг
                default: rx = -lz; rz = lx; break;  // запад
            }
        }
    }
}
