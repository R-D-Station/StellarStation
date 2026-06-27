namespace Shared.Simulation
{
    /// <summary>
    /// Причина лежания при <see cref="PlayerState.Laying"/>. Voluntary снимается самим игроком
    /// (тот же toggle, предсказуемо); KnockedDown держит сервер по тик-таймеру.
    /// </summary>
    public enum LayingReason : byte
    {
        None = 0,
        Voluntary = 1,
        KnockedDown = 2
    }
}
