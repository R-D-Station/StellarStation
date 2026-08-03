namespace Shared.Simulation.Blocks
{
    /// <summary>Мировые AABB динамических препятствий (двигающиеся ящики) для BlockMovementLogic; null = поведение не меняется.</summary>
    public interface IDynamicObstacles
    {
        int Count { get; }
        bool HasMovingBoxes { get; }
        void Get(int i, out float minX, out float minY, out float minZ, out float maxX, out float maxY, out float maxZ);
        float GetDeltaY(int i);
    }
}
