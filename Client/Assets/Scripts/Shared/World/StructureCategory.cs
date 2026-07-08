namespace Shared.World
{
    /// <summary>Категория настенного объекта (ось «структур»): одинаковые замещают друг друга при рисовании, разные —
    /// сосуществуют на клетке. Edit-time метаданные SO — НЕ сериализуется в карту (формат .smap цел).</summary>
    public enum StructureCategory : byte
    {
        Wall = 0,
        Door = 1,
        Hatch = 2,
        Window = 3,
        Ladder = 4
    }

    /// <summary>Декларативный классификатор поведения категории — единый источник (engine-agnostic, зовут клиент и сервер).</summary>
    public static class StructureCategoryInfo
    {
        /// <summary>Открываемая категория (дверь/люк), а не глухая (стена/окно/лестница).</summary>
        public static bool IsOpenable(StructureCategory c) => c == StructureCategory.Door || c == StructureCategory.Hatch;
    }
}
