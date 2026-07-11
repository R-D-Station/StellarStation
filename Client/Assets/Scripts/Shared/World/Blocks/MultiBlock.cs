namespace Shared.World.Blocks
{
    /// <summary>
    /// Мульти-блок (объект из нескольких позиций одного типа): части адресуются part-битами state-канала
    /// (2 бита → до 4 частей, т.е. размеры 1..2 по осям при sx·sy·sz ≤ 4; дверь 2×1×2 = 4 части).
    /// Якорь — часть 0. Порядок частей: ширина (локальный X), затем глубина (локальный Z), затем высота.
    /// Поворот — facing state-канала: шаги 90° по часовой вокруг Y; при 0 ширина → +X, глубина → +Z.
    /// </summary>
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
