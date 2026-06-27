namespace Shared.Simulation
{
    /// <summary>
    /// Тик-таймеры статусов игрока. В ТИКАХ, не в секундах — чтобы стан/нокдаун были
    /// детерминированы и одинаково реплеились в предсказании. Декремент — на сервере (Этап 5).
    /// </summary>
    public struct StatusTimers
    {
        public int StunTicksRemaining;
        public int KnockdownTicksRemaining;
    }
}
