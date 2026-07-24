using System;
using Shared.Messages.Core;

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

        /// <summary>Множитель шага при ползании (Laying). Литерал (как InvSqrt2) — один и тот же бит обе стороны.</summary>
        public const float CrawlMultiplier = 0.7f;

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

        /// <summary>IntentDirection → facing-байт Direction (N=0, S=1, E=2, W=3). None сохраняет текущий.</summary>
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