using System.Collections.Generic;

namespace Shared.Simulation.Blocks
{
    /// <summary>Пересобираемый за тик набор world-AABB (центр+halfW+height) — реализация IDynamicObstacles для тянущихся ящиков.</summary>
    public sealed class DynamicObstacleSet : IDynamicObstacles
    {
        private readonly struct Aabb
        {
            public readonly float MinX, MinY, MinZ, MaxX, MaxY, MaxZ, DeltaY;

            public Aabb(float minX, float minY, float minZ, float maxX, float maxY, float maxZ, float deltaY)
            {
                MinX = minX; MinY = minY; MinZ = minZ;
                MaxX = maxX; MaxY = maxY; MaxZ = maxZ;
                DeltaY = deltaY;
            }
        }

        private readonly List<Aabb> _boxes = new List<Aabb>();
        private int _movingCount;

        public int Count => _boxes.Count;

        public bool HasMovingBoxes => _movingCount > 0;

        public void Clear()
        {
            _boxes.Clear();
            _movingCount = 0;
        }

        public void Add(float centerX, float y, float centerZ, float halfW, float height)
            => Add(centerX, y, centerZ, halfW, halfW, height, 0f);

        public void Add(float centerX, float y, float centerZ, float halfW, float height, float deltaY)
            => Add(centerX, y, centerZ, halfW, halfW, height, deltaY);

        /// <summary>Прямоугольный в плане бокс: у кабины лифта СТЕНЫ, квадратом halfW они невыразимы.</summary>
        public void Add(float centerX, float y, float centerZ, float halfX, float halfZ, float height, float deltaY)
        {
            _boxes.Add(new Aabb(centerX - halfX, y, centerZ - halfZ, centerX + halfX, y + height, centerZ + halfZ, deltaY));
            if (deltaY != 0f)
                _movingCount++;
        }

        public void Get(int i, out float minX, out float minY, out float minZ, out float maxX, out float maxY, out float maxZ)
        {
            var a = _boxes[i];
            minX = a.MinX; minY = a.MinY; minZ = a.MinZ;
            maxX = a.MaxX; maxY = a.MaxY; maxZ = a.MaxZ;
        }

        public float GetDeltaY(int i) => _boxes[i].DeltaY;

        public void CopyInto(DynamicObstacleSet target)
        {
            for (int i = 0; i < _boxes.Count; i++)
            {
                var a = _boxes[i];
                target._boxes.Add(a);
                if (a.DeltaY != 0f)
                    target._movingCount++;
            }
        }
    }
}
