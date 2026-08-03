using Shared.World.Atmos;
using Shared.World.Blocks;

namespace ServerTests.Shared.World.AtmosCore
{
    public class AtmosBlocksTests
    {
        private const ushort Wall = 3;
        private const ushort SlabFloor = 2;
        private const ushort Door = 6;

        [Fact]
        public void Air_IsNotAirtight()
        {
            var grid = new BlockGrid();
            grid.SetBlock(0, 0, 0, Wall);
            Assert.False(AtmosBlocks.IsAirtight(grid, 5, 5, 5));
        }

        [Fact]
        public void Wall_IsAirtight()
        {
            var grid = new BlockGrid();
            grid.SetBlock(2, 3, 4, Wall);
            Assert.True(AtmosBlocks.IsAirtight(grid, 2, 3, 4));
        }

        [Fact]
        public void SlabFloor_IsAirtight_SealsVertically()
        {
            var grid = new BlockGrid();
            grid.SetBlock(2, 3, 4, SlabFloor);
            Assert.True(AtmosBlocks.IsAirtight(grid, 2, 3, 4));
        }

        [Fact]
        public void Door_Closed_IsAirtight_Open_IsNot()
        {
            var grid = new BlockGrid();
            grid.SetBlock(1, 1, 1, Door);

            grid.SetState(1, 1, 1, BlockState.WithOpen(0, false));
            Assert.True(AtmosBlocks.IsAirtight(grid, 1, 1, 1));

            grid.SetState(1, 1, 1, BlockState.WithOpen(0, true));
            Assert.False(AtmosBlocks.IsAirtight(grid, 1, 1, 1));
        }

        [Fact]
        public void UnknownType_FallsBackToAir_NotAirtight()
        {
            var grid = new BlockGrid();
            grid.SetBlock(0, 0, 0, 12345);
            Assert.False(AtmosBlocks.IsAirtight(grid, 0, 0, 0));
        }
    }

    public class AtmosInitTests
    {
        private const ushort Wall = 3;
        private const ushort SlabFloor = 2;
        private const ushort Door = 6;

        private static BlockGrid SealedRoom(int interiorHeight = 1)
        {
            var grid = new BlockGrid();
            int maxY = interiorHeight + 1;
            for (int x = 0; x <= 2; x++)
                for (int y = 0; y <= maxY; y++)
                    for (int z = 0; z <= 2; z++)
                        if (x != 1 || z != 1 || y == 0 || y == maxY)
                            grid.SetBlock(x, y, z, Wall);
            return grid;
        }

        private static float TotalMoles(AtmosGrid atmos)
        {
            float sum = 0f;
            foreach (var kv in atmos.Sections)
                for (int i = 0; i < GasSection.CellCount; i++)
                    sum += kv.Value.Get(i).TotalMoles;
            return sum;
        }

        private static int SpaceCount(AtmosGrid atmos)
        {
            int n = 0;
            foreach (var kv in atmos.Sections)
                for (int i = 0; i < GasSection.CellCount; i++)
                    if (kv.Value.IsSpace(i)) n++;
            return n;
        }

        [Fact]
        public void SealedRoom_InteriorStandard_OutsideSpace()
        {
            var grid = SealedRoom();
            var atmos = new AtmosGrid();
            AtmosInit.Classify(grid, atmos);

            Assert.False(atmos.IsSpace(1, 1, 1));
            Assert.Equal(AtmosConstants.StandardPressureKpa, atmos.GetMix(1, 1, 1).PressureKpa, 3);

            Assert.True(atmos.IsSpace(8, 8, 8));
            Assert.True(atmos.GetMix(8, 8, 8).IsVacuum());
        }

        [Fact]
        public void SealedRoom_MassIsExactlyOneCellOfStandard()
        {
            var grid = SealedRoom();
            var atmos = new AtmosGrid();
            AtmosInit.Classify(grid, atmos);

            Assert.Equal(AtmosConstants.StandardTotalMoles, TotalMoles(atmos), 4);
        }

        [Fact]
        public void RoomWithOpenDoor_IsFloodedAsSpace()
        {
            var grid = SealedRoom();
            grid.SetBlock(1, 1, 0, Door);
            grid.SetState(1, 1, 0, BlockState.WithOpen(0, true));

            var atmos = new AtmosGrid();
            AtmosInit.Classify(grid, atmos);

            Assert.True(atmos.IsSpace(1, 1, 1));
            Assert.True(atmos.GetMix(1, 1, 1).IsVacuum());
            Assert.Equal(0f, TotalMoles(atmos), 4);
        }

        [Fact]
        public void RoomWithClosedDoor_StaysPressurized()
        {
            var grid = SealedRoom();
            grid.SetBlock(1, 1, 0, Door);
            grid.SetState(1, 1, 0, BlockState.WithOpen(0, false));

            var atmos = new AtmosGrid();
            AtmosInit.Classify(grid, atmos);

            Assert.False(atmos.IsSpace(1, 1, 1));
            Assert.Equal(AtmosConstants.StandardPressureKpa, atmos.GetMix(1, 1, 1).PressureKpa, 3);
        }

        [Fact]
        public void SlabBetweenFloors_KeepsBothLevelsPressurized_AndSeparate()
        {
            var grid = SealedRoom(interiorHeight: 3);
            grid.SetBlock(1, 2, 1, SlabFloor);

            var atmos = new AtmosGrid();
            AtmosInit.Classify(grid, atmos);

            Assert.False(atmos.IsSpace(1, 1, 1));
            Assert.False(atmos.IsSpace(1, 3, 1));
            Assert.Equal(AtmosConstants.StandardPressureKpa, atmos.GetMix(1, 1, 1).PressureKpa, 3);
            Assert.Equal(AtmosConstants.StandardPressureKpa, atmos.GetMix(1, 3, 1).PressureKpa, 3);
            Assert.True(atmos.GetMix(1, 2, 1).IsVacuum());
            Assert.Equal(AtmosConstants.StandardTotalMoles * 2f, TotalMoles(atmos), 4);
        }

        [Fact]
        public void OpenDoorOnLowerFloor_DoesNotDrainUpperFloor()
        {
            var grid = SealedRoom(interiorHeight: 3);
            grid.SetBlock(1, 2, 1, SlabFloor);
            grid.SetBlock(1, 1, 0, Door);
            grid.SetState(1, 1, 0, BlockState.WithOpen(0, true));

            var atmos = new AtmosGrid();
            AtmosInit.Classify(grid, atmos);

            Assert.True(atmos.IsSpace(1, 1, 1));
            Assert.False(atmos.IsSpace(1, 3, 1));
            Assert.Equal(AtmosConstants.StandardTotalMoles, TotalMoles(atmos), 4);
        }

        [Fact]
        public void Classify_IsDeterministic_AcrossRuns()
        {
            var grid = SealedRoom(interiorHeight: 3);
            grid.SetBlock(1, 2, 1, SlabFloor);
            grid.SetBlock(1, 1, 0, Door);
            grid.SetState(1, 1, 0, BlockState.WithOpen(0, true));

            var a = new AtmosGrid();
            var b = new AtmosGrid();
            AtmosInit.Classify(grid, a);
            AtmosInit.Classify(grid, b);

            Assert.Equal(SpaceCount(a), SpaceCount(b));
            Assert.Equal(TotalMoles(a), TotalMoles(b), 6);

            for (int x = -1; x <= 17; x++)
                for (int y = -1; y <= 17; y++)
                    for (int z = -1; z <= 17; z++)
                    {
                        Assert.Equal(a.IsSpace(x, y, z), b.IsSpace(x, y, z));
                        Assert.Equal(a.GetMix(x, y, z).O2Moles, b.GetMix(x, y, z).O2Moles);
                        Assert.Equal(a.GetMix(x, y, z).N2Moles, b.GetMix(x, y, z).N2Moles);
                    }
        }

        [Fact]
        public void EmptyGrid_ProducesNoGas()
        {
            var atmos = new AtmosGrid();
            AtmosInit.Classify(new BlockGrid(), atmos);
            Assert.Equal(0f, TotalMoles(atmos), 6);
        }
    }
}
