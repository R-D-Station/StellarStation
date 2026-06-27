using Shared.Messages.Core;
using Shared.World;

namespace ServerTests.Shared.Messages.Core
{
    public class TileUpdateTests
    {
        [Fact]
        public void SerializeDeserialize_RoundTrips()
        {
            var tile = Tile.Floor();
            tile.StructureType = 2;
            tile.Openable = true;
            tile.Open = true;

            var msg = new TileUpdate { X = -3, Y = 12, Z = 1, Tile = tile };

            var bytes = msg.Serialize();
            var back = new TileUpdate();
            back.Deserialize(bytes);

            Assert.Equal(-3, back.X);
            Assert.Equal(12, back.Y);
            Assert.Equal(1, back.Z);
            Assert.Equal((byte)2, back.Tile.StructureType);
            Assert.True(back.Tile.Open);
            Assert.True(back.Tile.Walkable);
        }
    }
}
