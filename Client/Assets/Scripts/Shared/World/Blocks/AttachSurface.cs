namespace Shared.World.Blocks
{
    /// <summary>Поверхность, к которой может крепиться блок (порядок в массиве AttachTo — приоритет резолва).</summary>
    public enum AttachSurface : byte
    {
        Floor,
        Wall,
        Ceiling,
        AnySolid
    }
}
