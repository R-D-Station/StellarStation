using System;
using Shared.World.Blocks;

namespace ServerTests.Shared.World.Blocks
{
    public class BlockGridTests
    {
        [Fact]
        public void SetGet_NegativeCoords()
        {
            var g = new BlockGrid();
            g.SetBlock(-1, -17, -33, 42);

            Assert.Equal((ushort)42, g.GetBlock(-1, -17, -33));
            Assert.Equal((ushort)0, g.GetBlock(-2, -17, -33));
        }

        [Fact]
        public void Sections_CreatedAndRemoved()
        {
            var g = new BlockGrid();
            Assert.False(g.SetBlock(3, 3, 3, 0)); // Air в пустоту — секция не создаётся
            Assert.Empty(g.Sections);

            g.SetBlock(3, 3, 3, 1);
            Assert.Single(g.Sections);

            g.SetBlock(3, 3, 3, 0); // опустошение удаляет секцию
            Assert.Empty(g.Sections);
        }

        [Fact]
        public void DirtyTracking_MarksAndClears()
        {
            var g = new BlockGrid();
            g.SetBlock(0, 0, 0, 1);
            g.SetBlock(100, 0, 0, 2); // другая секция

            Assert.Equal(2, g.DirtySections.Count);
            g.ClearDirty();
            Assert.Empty(g.DirtySections);

            g.SetState(0, 0, 0, BlockState.Pipe); // state тоже метит dirty
            Assert.Single(g.DirtySections);

            g.ClearDirty();
            g.SetBlock(0, 0, 0, 0); // удаление секции — dirty остаётся (для сети/сейва)
            Assert.Single(g.DirtySections);
        }

        [Fact]
        public void VerticalBounds_ReadAirWriteThrows()
        {
            // Вертикаль — Y (второй аргумент), оси Unity.
            var g = new BlockGrid();
            Assert.Equal((ushort)0, g.GetBlock(0, BlockGrid.VerticalLimit, 0)); // запрос за край легален

            Assert.Throws<ArgumentOutOfRangeException>(() => g.SetBlock(0, BlockGrid.VerticalLimit, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => g.SetBlock(0, -BlockGrid.VerticalLimit - 1, 0, 1));

            g.SetBlock(0, BlockGrid.VerticalLimit - 1, 0, 1); // граничные значения валидны
            g.SetBlock(0, -BlockGrid.VerticalLimit, 0, 1);
        }

        [Fact]
        public void Key_RoundTripsAndRejectsOverflow()
        {
            long k = BlockGrid.Key(-5, 7, -32);
            BlockGrid.UnpackKey(k, out int cx, out int cy, out int cz);
            Assert.Equal((-5, 7, -32), (cx, cy, cz));

            Assert.Throws<ArgumentOutOfRangeException>(() => BlockGrid.Key(1 << 20, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => BlockGrid.Key(0, -(1 << 20) - 1, 0));
        }

        [Fact]
        public void HorizontalReadBeyondKeyRange_ReturnsAir()
        {
            var g = new BlockGrid();
            Assert.Equal((ushort)0, g.GetBlock(int.MaxValue, 0, 0));
            Assert.Equal(0, g.GetState(0, int.MinValue, 0));
        }

        [Fact]
        public void BlockChanged_FiresOnRealChangesOnly()
        {
            var g = new BlockGrid();
            int fired = 0;
            g.BlockChanged += (_, _, _) => fired++;

            g.SetBlock(1, 1, 1, 5);
            g.SetBlock(1, 1, 1, 5);              // без изменения — не зовётся
            g.SetState(1, 1, 1, BlockState.Open);
            g.SetState(2, 2, 2, BlockState.Open); // Air — не зовётся

            Assert.Equal(2, fired);
        }

        [Fact]
        public void GetState_DefaultsToZero()
        {
            var g = new BlockGrid();
            Assert.Equal(0, g.GetState(9, 9, 9));
            g.SetBlock(9, 9, 9, 3);
            Assert.Equal(0, g.GetState(9, 9, 9));
        }
    }
}
