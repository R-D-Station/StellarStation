using UnityEngine;
using Shared.World.Blocks;

namespace Client.Map
{
    /// <summary>Общая геометрия визуала мульти-блока (рендер и редактор): центр низа футпринта + поворот.</summary>
    public static class MultiBlockVisual
    {
        /// <summary>Позиция префаба объекта: центр низа всего футпринта (якорь + повёрнутые части).</summary>
        public static Vector3 FootprintBottomCenter(int ax, int ay, int az, int sx, int sz, int facing)
        {
            int parts = MultiBlock.PartCount(sx, 1, sz); // центр по плану — высота не влияет
            int minX = int.MaxValue, maxX = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;
            for (int p = 0; p < parts; p++)
            {
                MultiBlock.PartWorldOffset(p, sx, sz, facing, out int dx, out _, out int dz);
                if (dx < minX) minX = dx;
                if (dx > maxX) maxX = dx;
                if (dz < minZ) minZ = dz;
                if (dz > maxZ) maxZ = dz;
            }
            return new Vector3(ax + (minX + maxX + 1) * 0.5f, ay, az + (minZ + maxZ + 1) * 0.5f);
        }

        public static Quaternion FacingRotation(int facing) => Quaternion.Euler(0f, 90f * (facing & 3), 0f);
    }
}
