using System.IO;
using System.Text;
using Shared.World;

namespace ServerTests.Shared.World
{
    public class MapSerializerTests
    {
        [Fact]
        public void RoundTrip_PreservesDoorAndSpecial()
        {
            var map = new GridMap();

            var door = Tile.Floor();
            door.DoorType = 3;
            door.DoorOpen = true;
            map.SetTile(5, 7, 1, door);

            var stair = Tile.Floor();
            stair.Special = TileSpecial.StairUp;
            map.SetTile(2, 2, 0, stair);

            using var ms = new MemoryStream();
            MapSerializer.Write(ms, map);
            ms.Position = 0;
            var loaded = MapSerializer.Read(ms);

            var d = loaded.GetTile(5, 7, 1);
            Assert.Equal((byte)3, d.DoorType);
            Assert.True(d.DoorOpen);
            Assert.True(d.Walkable);

            var s = loaded.GetTile(2, 2, 0);
            Assert.Equal(TileSpecial.StairUp, s.Special);
        }

        [Fact]
        public void Read_V1Map_DefaultsDoorAndSpecial()
        {
            // Собираем v1-поток вручную (тайл 3 байта: FloorType, WallType, flags).
            using var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true))
            {
                w.Write(MapSerializer.Magic);
                w.Write((ushort)1);                 // версия 1
                w.Write(1);                         // chunkCount
                w.Write(0); w.Write(0); w.Write(0); // cx, cy, z

                for (int i = 0; i < Chunk.TileCount; i++)
                {
                    if (i == 0)
                    {
                        w.Write((byte)1);           // FloorType
                        w.Write((byte)0);           // WallType
                        w.Write((byte)(1 | 4 | 16)); // flags: Support | VBlock | SealV
                    }
                    else
                    {
                        w.Write((byte)0); w.Write((byte)0); w.Write((byte)0); // космос
                    }
                }
            }

            ms.Position = 0;
            var map = MapSerializer.Read(ms);

            var t = map.GetTile(0, 0, 0);
            Assert.Equal((byte)1, t.FloorType);
            Assert.True(t.Support);
            Assert.Equal((byte)0, t.DoorType);          // двери нет — дефолт
            Assert.Equal(TileSpecial.None, t.Special);  // спец-тайла нет — дефолт
            Assert.True(t.Walkable);
        }
    }
}
