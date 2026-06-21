using Shared.Messages.Core;

namespace Shared.Simulation
{
    /// <summary>
    /// Единая логика движения. Вызывается И сервером (авторитетная симуляция),
    /// И клиентом (предсказание своего игрока). Один код = детерминизм:
    /// предсказание клиента совпадает с сервером, reconciliation не дёргает позицию.
    ///
    /// Один вызов = один серверный тик = один обработанный MoveIntent.
    /// Шаг суб-тайловый (дробный X/Y), Z (этаж) здесь не трогается.
    /// </summary>
    public static class MovementLogic
    {
        /// <summary>Базовый шаг за один тик в тайлах.</summary>
        public const float StepPerTick = 0.1f;

        /// <summary>Множитель шага при спринте (0.2 / 0.1).</summary>
        public const float SprintMultiplier = 2f;

        /// <summary>
        /// Применяет одно намерение движения к позиции (X/Y — суб-тайловые).
        /// Z не меняется: смена этажа — отдельная механика.
        /// </summary>
        public static void Apply(ref float x, ref float y, IntentDirection dir, bool sprint)
        {
            float step = StepPerTick * (sprint ? SprintMultiplier : 1f);

            switch (dir)
            {
                case IntentDirection.North: y += step; break;
                case IntentDirection.South: y -= step; break;
                case IntentDirection.East: x += step; break;
                case IntentDirection.West: x -= step; break;
                case IntentDirection.None: break;
            }
        }

        /// <summary>
        /// Переводит IntentDirection в facing-байт в семантике Entity.Direction
        /// (North=0, South=1, East=2, West=3) — именно это читает клиентский визуал.
        /// ВАЖНО: IntentDirection имеет другой порядок (None=0, сдвиг +1), поэтому
        /// конвертация явная, а не приведением типа.
        ///
        /// None оставляет facing без изменений — возвращает текущее значение.
        /// </summary>
        public static byte ToFacing(IntentDirection dir, byte currentFacing)
        {
            switch (dir)
            {
                case IntentDirection.North: return 0; // Entity.Direction.North
                case IntentDirection.South: return 1; // Entity.Direction.South
                case IntentDirection.East: return 2; // Entity.Direction.East
                case IntentDirection.West: return 3; // Entity.Direction.West
                default: return currentFacing;        // None — не меняем
            }
        }
    }
}