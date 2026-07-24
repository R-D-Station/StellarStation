using System.Collections.Generic;

namespace Shared.Simulation.Blocks
{
    /// <summary>Пересобираемый за тик набор world-AABB (центр+halfW+height) — реализация IDynamicObstacles для тянущихся ящиков.</summary>
    public sealed class DynamicObstacleSet : IDynamicObstacles
    {
        private readonly struct Aabb
        {
            public readonly float MinX, MinY, MinZ, MaxX, MaxY, MaxZ;

            public Aabb(float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
            {
                MinX = minX; MinY = minY; MinZ = minZ;
                MaxX = maxX; MaxY = maxY; MaxZ = maxZ;
            }
        }

        private readonly List<Aabb> _boxes = new List<Aabb>();

        public int Count => _boxes.Count;

        public void Clear() => _boxes.Clear();

        public void Add(float centerX, float y, float centerZ, float halfW, float height)
            => _boxes.Add(new Aabb(centerX - halfW, y, centerZ - halfW, centerX + halfW, y + height, centerZ + halfW));

        public void Get(int i, out float minX, out float minY, out float minZ, out float maxX, out float maxY, out float maxZ)
        {
            var a = _boxes[i];
            minX = a.MinX; minY = a.MinY; minZ = a.MinZ;
            maxX = a.MaxX; maxY = a.MaxY; maxZ = a.MaxZ;
        }
    }
}
