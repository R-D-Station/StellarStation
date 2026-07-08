namespace Client.UI.Labels
{
    /// <summary>Хук пула: сброс объекта до pristine при выдаче из пула (зовётся из PoolMono.GetFreeElement до возврата).</summary>
    public interface IPooledLabel
    {
        void OnAcquire();
    }
}
