namespace Shared.World
{
    /// <summary>Чистый кодировщик верха стены для материала TileTop (шейдер TileReader): основной слой `_cur` по форме +
    /// слой углов `_cur_corner`/`_rotate` по маске углов. Engine-agnostic (System-only) — лежит в Shared ради юнит-тестов.</summary>
    public static class WallCornerCode
    {
        /// <summary>Индекс основного слоя `_cur` по форме: Cross=0, T=1, Corner=2, Straight=3, End=4, Single=5.</summary>
        public static byte CurFromShape(WallShape shape) => (byte)(5 - (int)shape);

        // Поправка ориентации спрайта СМЕЖНОЙ пары углов (_cur_corner=2). Рекалибровка 2026-07-07 (перерисованный атлас
        // Iron→Base): смежная пара (T/Cross) +90° к значению 06.07 → 0. Флип-точка направления ЗДЕСЬ; общая для стен и пола.
        private const int AdjacentPairRotate = 0;

        // Поправка ориентации ОДИНОЧНОГО угла (_cur_corner=1): рекалибровка 2026-07-07 → −90° (=3). Флип-точка, общая стены+пол.
        private const int SingleCornerRotate = 3; // −90°

        /// <summary>Маска углов (NW=1, NE=2, SE=4, SW=8; по часовой от NW) → (`_cur_corner` 0..5, `_rotate` 0..3) по
        /// таблице пользователя. `_rotate` здесь в МИРОВЫХ осях; поворот меша компенсирует <see cref="CompensateRotateForMesh"/>.</summary>
        public static (byte curCorner, byte rotate) EncodeCorners(byte cornerMask)
        {
            int mask = cornerMask & 0x0F;
            int count = (mask & 1) + ((mask >> 1) & 1) + ((mask >> 2) & 1) + ((mask >> 3) & 1);

            switch (count)
            {
                case 0: return (0, 0);
                case 1: return (1, (byte)((LowestPos(mask) + SingleCornerRotate) & 3)); // одиночный угол + калибровка (−90°)
                case 4: return (5, 0);
                case 3: return (4, (byte)LowestPos((~mask) & 0x0F));       // _rotate = позиция ОТСУТСТВУЮЩЕГО угла
                case 2:
                    // 2 по диагонали → _cur_corner=3 (180°-симметрична — поправка не нужна)
                    if (mask == 0b0101) return (3, 0); // NW+SE
                    if (mask == 0b1010) return (3, 1); // NE+SW
                    // 2 смежных → _cur_corner=2, база _rotate = ведущий (CW) угол пары + AdjacentPairRotate (визуальная поправка)
                    int lead = mask switch
                    {
                        0b0011 => 0, // NW+NE
                        0b0110 => 1, // NE+SE
                        0b1100 => 2, // SE+SW
                        _       => 3 // 0b1001 SW+NW
                    };
                    return (2, (byte)((lead + AdjacentPairRotate) & 3));
            }
            return (0, 0); // недостижимо: mask 0..15 покрыт выше
        }

        // Базовая ориентация спрайта угла (авторен из ВЕРХ-ЛЕВОГО) при визуальной сверке (2026-07-06) оказалась развёрнута
        // на 180° относительно того, что даёт кодировщик → добавляем полуоборот. Это часть ЕДИНОЙ точки сборки
        // world-rotate → шейдерный `_rotate`; при рассинхроне базовой ориентации менять ЗДЕСЬ.
        private const int RotateHalfTurn = 2; // 180° поправка базовой ориентации спрайта угла

        /// <summary>Floor-only база ориентации углового спрайта пола: прибавляется к итоговому `_rotate` в floor-ветке
        /// MapRenderer И в EditorWindow.DrawFloorTop (единый источник → редактор/рантайм в lockstep). Дефолт 0 = пол на
        /// wall-базе (RotateHalfTurn применяется) — СТАРТОВАЯ ДОГАДКА, не «нейтраль». Равномерный сдвиг после сверки —
        /// крутить 0..3 ЗДЕСЬ; per-config расхождение или редактор≠рантайм → отдельная floor-таблица (будущая задача).</summary>
        public const int FloorCornerBaseQuarter = 0;

        /// <summary>Итоговый шейдерный `_rotate` из мирового rotate: (1) −steps компенсирует ДВОЙНОЙ поворот (UV верхней
        /// грани — из АТРИБУТОВ МЕША, меш повёрнут на 90°·steps → слой углов уже повёрнут мешем); (2) +180° базовой
        /// ориентации спрайта угла. ИЗОЛИРОВАННАЯ точка ручной сверки направления/базы (менять знак −steps ↔ +steps
        /// и/или RotateHalfTurn ЗДЕСЬ).</summary>
        public static byte CompensateRotateForMesh(byte rotate, int steps)
            => (byte)(((rotate - steps + RotateHalfTurn) % 4 + 4) % 4);

        /// <summary>Per-surface/per-shape поправка ориентации углового слоя (визуальная калибровка 2026-07-07): базовые UV
        /// мешей форм авторены неодинаково → после <see cref="CompensateRotateForMesh"/> остаётся per-форма сдвиг, РАЗНЫЙ у
        /// пола и стены. Дельта в четвертях (90°), прибавляется к итоговому `_rotate`. Общая точка рантайма (MapRenderer) и
        /// редактора (EditorWindow) → lockstep. Флип/тюнинг значений ЗДЕСЬ.</summary>
        public static int ShapeCornerQuarter(bool isFloor, WallShape shape, byte curCorner)
        {
            if (isFloor)
            {
                if (shape == WallShape.Cross && curCorner == 1) return 1; // пол-крест с одним углом: +90°
                if (shape == WallShape.Corner) return 1;                  // пол-угол: калибровка по тайлу 24,20 → _rotate=3
                return 0;
            }
            if (shape == WallShape.Cross && curCorner == 1) return 1; // стена-крест с одним углом: +90°
            if (shape == WallShape.Corner) return 1;                  // стена-угол: +90°
            if (shape == WallShape.T) return 3;                       // стена-T: −90°
            return 0;
        }

        // Индекс младшего установленного бита (0..3); mask должен иметь ≥1 бит.
        private static int LowestPos(int mask)
        {
            if ((mask & 1) != 0) return 0;
            if ((mask & 2) != 0) return 1;
            if ((mask & 4) != 0) return 2;
            return 3;
        }
    }
}
