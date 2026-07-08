using Shared.Messages.Core;
using Shared.Simulation;
using Shared.World;

namespace ServerTests.Shared.Simulation
{
    public class MovementLogicTests
    {
        private static Tile Wall() => new Tile { StructureType = 1, Support = true };
        private static Tile Hole() => new Tile { Support = false, StructureType = 0 }; // нет пола, нет структуры

        [Fact]
        public void StepsOntoHole_NotBlocked()
        {
            // Баг-3: раньше дыра = «стена» (Walkable требует Support) → на неё нельзя было зайти, ProcessFalls не
            // срабатывал. Теперь коллизия по BlocksMovement (структура), пол/дыру не блокирует → игрок заходит на дыру.
            var map = new GridMap();
            map.SetTile(0, 0, 0, Tile.Floor());
            map.SetTile(0, 1, 0, Hole());
            map.SetTile(0, 2, 0, Hole());

            float x = 0.5f, y = 0.5f;
            for (int i = 0; i < 30; i++)
                MovementLogic.Apply(map, 0, ref x, ref y, IntentDirection.North, false);

            Assert.True(y > 1.5f, $"на дыру должен заходить (горизонталь не блокирует), y={y}");
        }

        [Fact]
        public void HolePassable_ButWallBeyondStillBlocks()
        {
            // Дыра проходима, но глухая стена за ней — по-прежнему стоп (блок по структуре сохранён).
            var map = new GridMap();
            map.SetTile(0, 0, 0, Tile.Floor());
            map.SetTile(0, 1, 0, Hole());
            map.SetTile(0, 2, 0, Wall());

            float x = 0.5f, y = 0.5f;
            for (int i = 0; i < 30; i++)
                MovementLogic.Apply(map, 0, ref x, ref y, IntentDirection.North, false);

            Assert.True(y > 1.0f, $"дыру должен пройти, y={y}");
            Assert.True(y < 2.0f, $"в стену за дырой упереться, y={y}");
        }

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
            door.StructureType = 1; // закрытая дверь между (0,0) и (0,2)
            door.Openable = true;
            map.SetTile(0, 1, 0, door);

            // Закрыто — не проходим.
            float x = 0.5f, y = 0.5f;
            for (int i = 0; i < 30; i++)
                MovementLogic.Apply(map, 0, ref x, ref y, IntentDirection.North, false);
            Assert.True(y < 1.0f, $"закрытая дверь должна блокировать, y={y}");

            // Открыли — проходим.
            door.Open = true;
            map.SetTile(0, 1, 0, door);
            for (int i = 0; i < 30; i++)
                MovementLogic.Apply(map, 0, ref x, ref y, IntentDirection.North, false);
            Assert.True(y > 1.5f, $"открытая дверь должна пропускать, y={y}");
        }
    }
}
