using System.Collections.Generic;
using Shared.Simulation;
using Shared.World;

namespace ServerTests.Shared.Simulation
{
    public class VisibilityTests
    {
        // Точка внутри полигона (ray-crossing). Полигон — звёздный из наблюдателя.
        private static bool Inside(List<Visibility.Vec2> poly, float px, float py)
        {
            bool inside = false;
            int n = poly.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                var pi = poly[i];
                var pj = poly[j];
                if (((pi.Y > py) != (pj.Y > py)) &&
                    (px < (pj.X - pi.X) * (py - pi.Y) / (pj.Y - pi.Y) + pi.X))
                    inside = !inside;
            }
            return inside;
        }

        [Fact]
        public void OpenArea_PointsVisible()
        {
            var map = new GridMap(); // нет стен
            var poly = Visibility.ComputePolygon(map, 0.5f, 0.5f, 0, 8f);

            Assert.True(poly.Count >= 4);
            Assert.True(Inside(poly, 3.5f, 0.5f));
            Assert.True(Inside(poly, 0.5f, 4.5f));
        }

        [Fact]
        public void Wall_BlocksPointBehind()
        {
            var map = new GridMap();
            map.SetTile(2, 0, 0, new Tile { WallType = 1, Support = true, BlocksHorizontalSight = true });

            var poly = Visibility.ComputePolygon(map, 0.5f, 0.5f, 0, 10f);

            Assert.False(Inside(poly, 6.5f, 0.5f)); // прямо за стеной — тень
            Assert.True(Inside(poly, 1.5f, 0.5f));  // перед стеной — видно
            Assert.True(Inside(poly, 0.5f, 6.5f));  // вбок — открыто, видно
        }

        [Fact]
        public void OpenDoor_SeesThrough()
        {
            var map = new GridMap();
            var door = Tile.Floor();
            door.DoorType = 1;
            door.BlocksHorizontalSight = true; // как у закрытой двери
            door.DoorOpen = true;              // но открыта
            map.SetTile(2, 0, 0, door);

            var poly = Visibility.ComputePolygon(map, 0.5f, 0.5f, 0, 10f);
            Assert.True(Inside(poly, 6.5f, 0.5f)); // сквозь открытую дверь видно
        }
    }
}
