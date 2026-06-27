namespace Shared.Simulation
{
    /// <summary>
    /// Состояние игрока, общее для клиента и сервера. Авторитетно на сервере, реплицируется
    /// байтом (EntitySnapshot.State). Stand/Move предсказываются клиентом, остальные — серверные.
    /// </summary>
    public enum PlayerState : byte
    {
        Stand = 0,       // нет ввода движения; движение разрешено
        Move = 1,        // ненулевой intent; движение разрешено
        Stun = 2,        // оглушён; движение запрещено; спадает по StunTicksRemaining
        Laying = 3,      // лежит (Voluntary/KnockedDown); ползёт медленнее
        Unconscious = 4, // без сознания; движение запрещено; выход внешний (медик)
        Dead = 5         // мёртв; движение запрещено; коллизия игнорируется
    }
}
