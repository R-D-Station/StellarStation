namespace Shared.World.Blocks
{
    /// <summary>Мульти-блок: часть вычисляется из смещения до якоря (слой принадлежности грида), поворот — facing.</summary>
    public static class MultiBlock
    {
        /// <summary>Предел размера по оси: |смещение| ≤ 15 после поворота вокруг якоря-угла (5 бит на ось в слое).</summary>
        public const int MaxAxis = 16;

        /// <summary>Санити-предел числа частей для кодогена (размер таблицы ломтиков), не бит-ограничение.</summary>
        public const int MaxPartsSanity = 256;

        /// <summary>Диапазон смещения на ось в слое принадлежности: [−15..15].</summary>
        public const int MaxOffset = 15;

        public static int PackOffset(int dx, int dy, int dz)
            => (dx + MaxOffset) | ((dy + MaxOffset) << 5) | ((dz + MaxOffset) << 10);

        public static void UnpackOffset(ushort packed, out int dx, out int dy, out int dz)
        {
            dx = (packed & 0x1F) - MaxOffset;
            dy = ((packed >> 5) & 0x1F) - MaxOffset;
            dz = ((packed >> 10) & 0x1F) - MaxOffset;
        }

        public static bool OffsetInRange(int dx, int dy, int dz)
            => dx >= -MaxOffset && dx <= MaxOffset
            && dy >= -MaxOffset && dy <= MaxOffset
            && dz >= -MaxOffset && dz <= MaxOffset;

        /// <summary>Мировой сдвиг → локальные (обратный RotateLocal).</summary>
        public static void UnrotateWorld(int dx, int dz, int facing, out int w, out int d)
        {
            switch (facing & 3)
            {
                case 0: w = dx; d = dz; break;
                case 1: w = -dz; d = dx; break;
                case 2: w = -dx; d = -dz; break;
                default: w = dz; d = -dx; break;
            }
        }

        /// <summary>Номер части по мировому смещению от якоря (обратная PartWorldOffset); −1 — смещение вне футпринта.</summary>
        public static int PartFromWorldOffset(int dx, int dy, int dz, int sx, int sy, int sz, int facing)
        {
            UnrotateWorld(dx, dz, facing, out int w, out int d);
            return LocalToPart(w, dy, d, sx, sy, sz);
        }

        /// <summary>Часть клетки по слою принадлежности: 0 — одиночный/якорь/без записи, part — валиден, −1 — запись вне футпринта.</summary>
        public static int PartAt(IBlockSampler grid, int sx, int sy, int sz, int facing, int x, int y, int z)
        {
            if (PartCount(sx, sy, sz) <= 1 || grid == null
                || !grid.TryGetStructOffset(x, y, z, out int dx, out int dy, out int dz))
                return 0;
            return PartFromWorldOffset(dx, dy, dz, sx, sy, sz, facing);
        }

        public static int PartCount(int sx, int sy, int sz) => sx * sy * sz;

        public static bool IsMulti(int sx, int sy, int sz) => PartCount(sx, sy, sz) > 1;

        /// <summary>Часть → локальные координаты (w — ширина, y — высота, d — глубина).</summary>
        public static void PartToLocal(int part, int sx, int sz, out int w, out int y, out int d)
        {
            w = part % sx;
            d = (part / sx) % sz;
            y = part / (sx * sz);
        }

        /// <summary>Локальные координаты → номер части; −1 — если (w,y,d) вне габарита (устаревшая/битая запись слоя).</summary>
        public static int LocalToPart(int w, int y, int d, int sx, int sy, int sz)
            => (w < 0 || w >= sx || y < 0 || y >= sy || d < 0 || d >= sz) ? -1 : w + d * sx + y * sx * sz;

        /// <summary>Локальная ширина/глубина → мировой сдвиг по facing (90° по часовой вокруг Y).</summary>
        public static void RotateLocal(int w, int d, int facing, out int dx, out int dz)
        {
            switch (facing & 3)
            {
                case 0: dx = w; dz = d; break;   // север: w→+X, d→+Z
                case 1: dx = d; dz = -w; break;  // восток
                case 2: dx = -w; dz = -d; break; // юг
                default: dx = -d; dz = w; break; // запад
            }
        }

        /// <summary>Мировой сдвиг части относительно якоря.</summary>
        public static void PartWorldOffset(int part, int sx, int sz, int facing, out int dx, out int dy, out int dz)
        {
            PartToLocal(part, sx, sz, out int w, out int y, out int d);
            RotateLocal(w, d, facing, out dx, out dz);
            dy = y;
        }

        /// <summary>План-центр футпринта относительно якоря при facing — тот же пивот, что у визуала мульти-блока.</summary>
        public static void FootprintCenterOffset(int sx, int sz, int facing, out float cx, out float cz)
        {
            int parts = PartCount(sx, 1, sz); // план — высота не влияет
            int minX = int.MaxValue, maxX = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;
            for (int p = 0; p < parts; p++)
            {
                PartWorldOffset(p, sx, sz, facing, out int dx, out _, out int dz);
                if (dx < minX) minX = dx;
                if (dx > maxX) maxX = dx;
                if (dz < minZ) minZ = dz;
                if (dz > maxZ) maxZ = dz;
            }
            cx = (minX + maxX + 1) * 0.5f;
            cz = (minZ + maxZ + 1) * 0.5f;
        }

        /// <summary>Позиция якоря по позиции части (part и facing — из state-байта этой части).</summary>
        public static void AnchorOf(int x, int y, int z, int part, int sx, int sz, int facing,
                                    out int ax, out int ay, out int az)
        {
            PartWorldOffset(part, sx, sz, facing, out int dx, out int dy, out int dz);
            ax = x - dx;
            ay = y - dy;
            az = z - dz;
        }
    }
}
