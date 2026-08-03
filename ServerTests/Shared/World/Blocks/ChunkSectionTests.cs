using Shared.World.Blocks;

namespace ServerTests.Shared.World.Blocks
{
    public class ChunkSectionTests
    {
        [Fact]
        public void SetBlock_ReturnsChangedAndTracksNonAir()
        {
            var s = new ChunkSection();
            int li = ChunkSection.LocalIndex(1, 2, 3);

            Assert.True(s.IsEmpty);
            Assert.True(s.SetBlock(li, 5));
            Assert.False(s.SetBlock(li, 5)); // то же значение — не изменилось
            Assert.Equal((ushort)5, s.GetBlock(li));
            Assert.False(s.IsEmpty);
            Assert.Equal(1, s.NonAirCount);

            Assert.True(s.SetBlock(li, 0));
            Assert.True(s.IsEmpty);
        }

        [Fact]
        public void Palette_GrowsPerDistinctType()
        {
            var s = new ChunkSection();
            for (int i = 0; i < 10; i++)
                s.SetBlock(i, (ushort)(100 + i));

            for (int i = 0; i < 10; i++)
                Assert.Equal((ushort)(100 + i), s.GetBlock(i));
            Assert.False(s.IsRaw);
        }

        [Fact]
        public void PaletteOverflow_FallsBackToRaw()
        {
            var s = new ChunkSection();
            // 256 слотов палитры: Air + 255 типов; 256-й уникальный тип должен переключить в raw.
            for (int i = 0; i < 300; i++)
                s.SetBlock(i, (ushort)(1 + i));

            Assert.True(s.IsRaw);
            for (int i = 0; i < 300; i++)
                Assert.Equal((ushort)(1 + i), s.GetBlock(i));
            Assert.Equal(300, s.NonAirCount);
        }

        [Fact]
        public void State_SparseAndClearedWithBlock()
        {
            var s = new ChunkSection();
            int li = ChunkSection.LocalIndex(4, 4, 4);

            Assert.False(s.SetState(li, BlockState.Open)); // на Air state запрещён
            s.SetBlock(li, 7);
            Assert.True(s.SetState(li, BlockState.Open));
            Assert.False(s.SetState(li, BlockState.Open)); // не изменился
            Assert.Equal(BlockState.Open, s.GetState(li));

            Assert.True(s.SetState(li, 0)); // обнуление удаляет запись
            Assert.Equal(0, s.GetState(li));

            s.SetState(li, BlockState.Pipe);
            s.SetBlock(li, 0); // Air стирает state позиции
            Assert.Equal(0, s.GetState(li));
        }

        [Fact]
        public void BlockState_FacingRoundTrip_KeepsOtherBits()
        {
            byte st = 0;
            st = BlockState.WithFacing(st, 2);
            st |= BlockState.Open | BlockState.Wire;

            Assert.Equal(2, BlockState.GetFacing(st));
            Assert.NotEqual(0, st & BlockState.Open);
            Assert.NotEqual(0, st & BlockState.Wire);
            Assert.Equal(0, st & BlockState.Pipe);

            st = BlockState.WithFacing(st, 0); // смена facing не трогает остальные биты
            Assert.Equal(0, BlockState.GetFacing(st));
            Assert.NotEqual(0, st & BlockState.Open);
            Assert.NotEqual(0, st & BlockState.Wire);
        }
    }
}
