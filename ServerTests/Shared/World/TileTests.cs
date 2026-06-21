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
            t.DoorType = 1;
            t.DoorOpen = false;
            Assert.False(t.Walkable);
        }

        [Fact]
        public void OpenDoor_IsWalkable()
        {
            var t = Tile.Floor();
            t.DoorType = 1;
            t.DoorOpen = true;
            Assert.True(t.Walkable);
        }

        [Fact]
        public void OpenDoor_WithoutFloor_IsNotWalkable()
        {
            // Дверь без пола под ней: стоять негде даже когда открыта.
            var t = Tile.Space;
            t.DoorType = 1;
            t.DoorOpen = true;
            Assert.False(t.Walkable);
        }
    }
}
