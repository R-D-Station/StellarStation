namespace Shared.World.Blocks
{
    /// <summary>Мульти-блок: части адресуются part-битами state-канала (до 4, якорь — часть 0), поворот — facing.</summary>
    public static class MultiBlock
    {
        public const int MaxParts = 4; // ёмкость part-бит

        public static int PartCount(int sx, int sy, int sz) => sx * sy * sz;

        public static bool IsMulti(int sx, int sy, int sz) => PartCount(sx, sy, sz) > 1;

        /// <summary>Часть → локальные координаты (w — ширина, y — высота, d — глубина).</summary>
        public static void PartToLocal(int part, int sx, int sz, out int w, out int y, out int d)
        {
            w = part % sx;
            d = (part / sx) % sz;
            y = part / (sx * sz);
        }

        public static int LocalToPart(int w, int y, int d, int sx, int sz) => w + d * sx + y * sx * sz;

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
