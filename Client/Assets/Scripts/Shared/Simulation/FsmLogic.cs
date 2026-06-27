using Shared.Messages.Core;

namespace Shared.Simulation
{
    /// <summary>
    /// Детерминированный переход FSM игрока — общий код для сервера (авторитет) и клиента
    /// (предсказание). Чистая функция без side-effect: те же входы → тот же выход на обеих
    /// сторонах. НЕ держит ссылок на UnityEngine/сущность — оперирует только состоянием и вводом.
    /// Скелет (Этап 1): Stand↔Move и добровольное лежание. Стан/нокдаун по таймерам и серверные
    /// выходы из Dead/Unconscious появятся на Этапах 5–6.
    /// </summary>
    public static class FsmLogic
    {
        /// <summary>
        /// Следующее состояние из текущего по вводу. <paramref name="layToggle"/> — бит «лечь/встать»
        /// (добавится в MoveIntent на Этапе 4). <paramref name="timers"/> передаётся ref на будущее
        /// (декремент стана/нокдауна — Этап 5); сейчас не трогается.
        /// </summary>
        public static PlayerState Step(
            PlayerState cur,
            IntentDirection dir,
            bool layToggle,
            LayingReason reason,
            ref StatusTimers timers)
        {
            bool moving = dir != IntentDirection.None;

            switch (cur)
            {
                // Серверные состояния: выход — только через entry API / таймеры (Этапы 5–6).
                case PlayerState.Stun:
                case PlayerState.Unconscious:
                case PlayerState.Dead:
                    return cur;

                case PlayerState.Laying:
                    // Принудительное лежание держит сервер; добровольное снимается тем же toggle.
                    if (reason == LayingReason.KnockedDown)
                        return PlayerState.Laying;
                    return layToggle ? PlayerState.Stand : PlayerState.Laying;

                default: // Stand / Move — предсказуемая ось ввода
                    if (layToggle)
                        return PlayerState.Laying;
                    return moving ? PlayerState.Move : PlayerState.Stand;
            }
        }

        /// <summary>
        /// Разрешено ли движение в этом состоянии (гейт для MovementLogic.Apply). Laying двигается
        /// (ползёт) — множитель скорости применяется на месте вызова (Этап 5).
        /// </summary>
        public static bool MovementAllowed(PlayerState state)
            => state == PlayerState.Stand
            || state == PlayerState.Move
            || state == PlayerState.Laying;
    }
}
