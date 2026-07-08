using Shared.Simulation;

namespace Client.Gameplay.Interaction
{
    /// <summary>Текст подсказки по <see cref="HintKind"/> (MVP-switch). Будущее — SO-словарь локализации.</summary>
    public static class HintText
    {
        public static string For(HintKind k) => k switch
        {
            HintKind.ClimbUp => "Нажмите ЛКМ чтобы подняться",
            HintKind.ClimbDown => "Нажмите ЛКМ чтобы опуститься",
            _ => string.Empty
        };
    }
}
