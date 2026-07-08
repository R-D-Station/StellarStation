namespace Client.Gameplay.Entities
{
    /// <summary>Сторона, куда смотрит сущность (рендерится вьюхой по Facing-байту: N=0,S=1,E=2,W=3).</summary>
    public enum Direction
    {
        North,  // +Z
        South,  // -Z
        East,   // +X
        West    // -X
    }
}
