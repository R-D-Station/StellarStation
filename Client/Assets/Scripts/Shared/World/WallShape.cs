namespace Shared.World
{
    /// <summary>Базовая форма соединения стены (6 форм покрывают все 16 комбинаций 4 прямых соседей через поворот).</summary>
    public enum WallShape : byte
    {
        Single = 0,   // 0 соседей
        End = 1,      // 1 сосед
        Straight = 2, // 2 противоположных
        Corner = 3,   // 2 смежных
        T = 4,        // 3 соседа
        Cross = 5     // 4 соседа
    }
}
