using Shared.World.Blocks;
using Shared.Simulation.Blocks;

namespace ServerTests.Shared.World.Blocks
{
    /// <summary>Дверь-фаза 1: бит Open в state, open/closed наборы боксов, гейт по Openable.</summary>
    public class BlockDoorTests
    {
        [Fact]
        public void BlockState_Open_RoundTrips_PreservingOtherBits()
        {
            byte s = BlockState.WithFacing(BlockState.Wire, 2); // facing 2 + провод
            Assert.False(BlockState.GetOpen(s));

            byte opened = BlockState.WithOpen(s, true);
            Assert.True(BlockState.GetOpen(opened));
            Assert.Equal(2, BlockState.GetFacing(opened));    // сохранён
            Assert.NotEqual(0, opened & BlockState.Wire);     // сохранён

            byte closed = BlockState.WithOpen(opened, false);
            Assert.False(BlockState.GetOpen(closed));
            Assert.Equal(s, closed);
        }

        [Fact]
        public void GetBoxes_Open_ReturnsOpenSet_ForDoor()
        {
            var door = new BlockInfo(5, "Door", BlockCategory.Door, BlockFaceFlags.All, BlockFaceFlags.All, 0,
                new[] { new[] { BlockBox.Full } }, 1, 1, 1,
                new[] { System.Array.Empty<BlockBox>() }); // открыто = проходима

            Assert.Single(door.GetBoxes(0, 0, false)); // закрыта — блокирует
            Assert.Empty(door.GetBoxes(0, 0, true));   // открыта — сквозная
        }

        [Fact]
        public void GetBoxes_Open_IgnoredForNonOpenable()
        {
            // У стены open-набор не задан; бит Open не должен ничего менять (гейт по Openable).
            var wall = new BlockInfo(3, "Wall", BlockCategory.Wall, BlockFaceFlags.All, BlockFaceFlags.All, 0,
                new[] { new[] { BlockBox.Full } }, 1, 1, 1);

            Assert.Single(wall.GetBoxes(0, 0, false));
            Assert.Single(wall.GetBoxes(0, 0, true)); // Openable=false → всегда закрытый набор
        }

        [Fact]
        public void GetBoxes_OpenSet_RotatesByFacing()
        {
            // Открытый набор тоже предвычисляет повороты: +X-половина → −Z-половина при facing 1.
            var door = new BlockInfo(5, "Door", BlockCategory.Door, BlockFaceFlags.All, BlockFaceFlags.All, 0,
                new[] { new[] { BlockBox.Full } }, 1, 1, 1,
                new[] { new[] { new BlockBox(8, 0, 0, 16, 16, 16) } });

            var f0 = door.GetBoxes(0, 0, true)[0];
            Assert.Equal((8, 16), ((int)f0.MinX, (int)f0.MaxX));

            var f1 = door.GetBoxes(0, 1, true)[0]; // 90° CW
            Assert.Equal((0, 16, 0, 8), ((int)f1.MinX, (int)f1.MaxX, (int)f1.MinZ, (int)f1.MaxZ));
        }

        [Fact]
        public void Shapes_ReadOpenBit_FromState()
        {
            // BlockCatalogShapes отдаёт по state: тот же тип с битом Open даёт другой набор
            // (проверяем через кастомный IBlockShapes-эквивалент на BlockInfo напрямую).
            var door = new BlockInfo(5, "Door", BlockCategory.Door, BlockFaceFlags.All, BlockFaceFlags.All, 0,
                new[] { new[] { BlockBox.Full } }, 1, 1, 1,
                new[] { System.Array.Empty<BlockBox>() });

            byte closed = 0;
            byte open = BlockState.WithOpen(0, true);
            Assert.Single(door.GetBoxes(0, BlockState.GetFacing(closed), BlockState.GetOpen(closed)));
            Assert.Empty(door.GetBoxes(0, BlockState.GetFacing(open), BlockState.GetOpen(open)));
        }

        [Fact]
        public void DoorMeta_DefaultsAndValues()
        {
            var d = new BlockInfo(5, "Door", BlockCategory.Door, BlockFaceFlags.All, BlockFaceFlags.All, 0,
                new[] { new[] { BlockBox.Full } }, 1, 1, 1,
                null, DoorOpening.Auto,
                new[] { new TriggerBox(-8, 0, 0, 24, 32, 16) }, 1.5f);

            Assert.Equal(DoorOpening.Auto, d.Opening);
            Assert.Equal(1.5f, d.CloseDelay);
            Assert.Single(d.Triggers);
            Assert.Equal(-0.5f, d.Triggers[0].MinXf); // триггер торчит за габарит (−8/16)
            Assert.Equal(1.5f, d.Triggers[0].MaxXf);  // 24/16
        }
    }
}
