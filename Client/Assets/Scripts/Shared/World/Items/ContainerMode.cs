namespace Shared.World.Items
{
    /// <summary>Как контейнер реагирует на открытие: UI — окно-вьюер (Open/Close/Put/Take); SS14 — физическая крышка (E всасывает/высыпает).</summary>
    public enum ContainerMode : byte
    {
        UI = 0,
        SS14 = 1
    }
}
