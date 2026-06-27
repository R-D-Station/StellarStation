using Shared.Simulation;
using Shared.World;

namespace ServerTests.Shared.Simulation
{
    public class FovTests
    {
        private static GridMap FloorRegion(int half)
        {
            var map = new GridMap();
            for (int x = -half; x <= half; x++)
                for (int y = -half; y <= half; y++)
                    map.SetTile(x, y, 0, Tile.Floor());
            return map;
        }

        private static Tile Wall()
            => new Tile { StructureType = 1, Support = true, BlocksHorizontalSight = true };

        [Fact]
        public void Origin_AlwaysVisible()
        {
            var map = FloorRegion(5);
            int r = 5;
            var light = new float[2 * r + 1, 2 * r + 1];
            Fov.Compute(map, 0, 0, 0, r, light);
            Assert.True(light[r, r] > 0f);
        }

        [Fact]
        public void Wall_CastsShadowBehindIt()
        {
            var map = FloorRegion(6);
            map.SetTile(2, 0, 0, Wall()); // стена на восток от наблюдателя

            int r = 6;
            var light = new float[2 * r + 1, 2 * r + 1];
            Fov.Compute(map, 0, 0, 0, r, light);

            Assert.True(light[1 + r, 0 + r] > 0f, "тайл перед стеной виден");
            Assert.Equal(0f, light[3 + r, 0 + r]); // прямо за стеной — тень
            Assert.True(light[0 + r, 5 + r] > 0f, "открытое направление видно");
        }

        [Fact]
        public void ClosedDoor_Blocks_OpenDoor_SeesThrough()
        {
            var map = FloorRegion(6);

            var door = Tile.Floor();
            door.StructureType = 1;
            door.Openable = true;
            door.BlocksHorizontalSight = true; // как кладёт редактор для закрытой двери
            map.SetTile(2, 0, 0, door);

            int r = 6;
            var light = new float[2 * r + 1, 2 * r + 1];

            // Закрыто — за дверью тень.
            Fov.Compute(map, 0, 0, 0, r, light);
            Assert.Equal(0f, light[3 + r, 0 + r]);

            // Открыто — видно сквозь.
            door.Open = true;
            map.SetTile(2, 0, 0, door);
            Fov.Compute(map, 0, 0, 0, r, light);
            Assert.True(light[3 + r, 0 + r] > 0f);
        }
    }
}
