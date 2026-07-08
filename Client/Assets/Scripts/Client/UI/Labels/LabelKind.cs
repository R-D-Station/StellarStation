namespace Client.UI.Labels
{
    /// <summary>Вид экранной надписи. Сейчас живёт CursorHint; остальные — задел на следующий срез (world-anchored).</summary>
    public enum LabelKind
    {
        CursorHint,
        PlayerMessage,
        ObjectMessage,
        SoundMessage
    }
}
