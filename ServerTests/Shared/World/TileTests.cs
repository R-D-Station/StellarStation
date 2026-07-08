using Shared.World;

namespace ServerTests.Shared.World
{
    public class TileTests
    {
        [Fact]
        public void Floor_IsWalkable()
        {
            Assert.True(Tile.Floor().Walkable);
        }

        [Fact]
        public void Space_IsNotWalkable()
        {
            Assert.False(Tile.Space.Walkable);
        }

        [Fact]
        public void ClosedDoor_IsNotWalkable()
        {
            var t = Tile.Floor();
            t.StructureType = 1;
            t.Openable = true;
            t.Open = false;
            Assert.False(t.Walkable);
        }

        [Fact]
        public void OpenDoor_IsWalkable()
        {
            var t = Tile.Floor();
            t.StructureType = 1;
            t.Openable = true;
            t.Open = true;
            Assert.True(t.Walkable);
        }

        [Fact]
        public void OpenDoor_WithoutFloor_IsNotWalkable()
        {
            // Дверь без пола под ней: стоять негде даже когда открыта.
            var t = Tile.Space;
            t.StructureType = 1;
            t.Openable = true;
            t.Open = true;
            Assert.False(t.Walkable);
        }

        // BlocksMovement — горизонтальный упор коллизии (по структуре, НЕ по полу).

        [Fact]
        public void Floor_DoesNotBlockMovement()
        {
            Assert.False(Tile.Floor().BlocksMovement);
        }

        [Fact]
        public void Hole_DoesNotBlockMovement()
        {
            // Ключ баг-фикса: дыра (нет пола, нет структуры) по горизонтали проходима — падение решает ProcessFalls.
            Assert.False(Tile.Space.BlocksMovement);
        }

        [Fact]
        public void Wall_BlocksMovement()
        {
            var t = new Tile { StructureType = 1, Support = true }; // глухая стена
            Assert.True(t.BlocksMovement);
        }

        [Fact]
        public void ClosedDoor_BlocksMovement()
        {
            var t = Tile.Floor();
            t.StructureType = 1; t.Openable = true; t.Open = false;
            Assert.True(t.BlocksMovement);
        }

        [Fact]
        public void OpenDoor_DoesNotBlockMovement()
        {
            var t = Tile.Floor();
            t.StructureType = 1; t.Openable = true; t.Open = true;
            Assert.False(t.BlocksMovement);
        }
    }
}
