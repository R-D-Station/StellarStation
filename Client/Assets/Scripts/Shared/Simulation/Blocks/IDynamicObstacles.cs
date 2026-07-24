namespace Shared.Simulation.Blocks
{
    public interface IDynamicObstacles
    {
        int Count { get; }
        void Get(int i, out float minX, out float minY, out float minZ, out float maxX, out float maxY, out float maxZ);
    }
}
