using Shared.Messages.Core;
using Shared.Simulation;
using Shared.World;

namespace ServerTests.Shared.Simulation
{
    public class MovementLogicTests
    {
        private static Tile Wall() => new Tile { WallType = 1, Support = true };

        [Fact]
        public void SlipsThroughOneWideGap_WhenOffCenter()
        {
            var map = new GridMap();
            // Пол 3x3 (x,y = 0..2), затем стены по строке y=1 кроме прохода в (1,1).
            for (int yy = 0; yy <= 2; yy++)
                for (int xx = 0; xx <= 2; xx++)
                    map.SetTile(xx, yy, 0, Tile.Floor());
            map.SetTile(0, 1, 0, Wall());
            map.SetTile(2, 1, 0, Wall());

            // Игрок смещён от центра прохода (1.5) и идёт на север сквозь проём (1,1).
            float x = 1.3f, y = 0.5f;
            for (int i = 0; i < 60; i++)
                MovementLogic.Apply(map, 0, ref x, ref y, IntentDirection.North, false);

            Assert.True(y > 2.0f, $"должен пролезть в верхнюю строку, y={y}");
            Assert.True(System.MathF.Abs(x - 1.5f) < 0.15f, $"должен подцентроваться, x={x}");
        }

        [Fact]
        public void StopsFlushAtSolidWall()
        {
            var map = new GridMap();
            map.SetTile(0, 0, 0, Tile.Floor());
            map.SetTile(0, 1, 0, Wall()); // сплошная стена на севере, прохода нет

            float x = 0.5f, y = 0.5f;
            for (int i = 0; i < 30; i++)
                MovementLogic.Apply(map, 0, ref x, ref y, IntentDirection.North, false);

            Assert.True(y < 1.0f, $"не должен пройти сквозь стену, y={y}");
        }

        [Fact]
        public void ClosedDoor_Blocks_OpenDoor_LetsThrough()
        {
            var map = new GridMap();
            map.SetTile(0, 0, 0, Tile.Floor());
            map.SetTile(0, 2, 0, Tile.Floor());

            var door = Tile.Floor();
            door.DoorType = 1; // закрытая дверь между (0,0) и (0,2)
            map.SetTile(0, 1, 0, door);

            // Закрыто — не проходим.
            float x = 0.5f, y = 0.5f;
            for (int i = 0; i < 30; i++)
                MovementLogic.Apply(map, 0, ref x, ref y, IntentDirection.North, false);
            Assert.True(y < 1.0f, $"закрытая дверь должна блокировать, y={y}");

            // Открыли — проходим.
            door.DoorOpen = true;
            map.SetTile(0, 1, 0, door);
            for (int i = 0; i < 30; i++)
                MovementLogic.Apply(map, 0, ref x, ref y, IntentDirection.North, false);
            Assert.True(y > 1.5f, $"открытая дверь должна пропускать, y={y}");
        }
    }
}
