namespace Shared.World.Items
{
    /// <summary>Где живёт предмет — ДАННЫЕ, не тип; Inside зарезервирован, в 4.5 не эмитится/не принимается.</summary>
    public enum ItemLocationKind : byte
    {
        Ground = 0,
        Held = 1,
        Inside = 2
    }
}
