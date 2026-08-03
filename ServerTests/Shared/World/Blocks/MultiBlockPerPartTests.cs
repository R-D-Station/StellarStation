using Shared.Messages.Core;
using Shared.Simulation;
using Shared.Simulation.Blocks;
using Shared.World.Atmos;
using Shared.World.Blocks;

namespace ServerTests.Shared.World.Blocks
{
    /// <summary>Пер-частные решения (герметичность/опора/проходимость) и коллизия движения по НЕ-якорным клеткам мульти-блока.</summary>
    public class MultiBlockPerPartTests
    {
        [Fact]
        public void IsAirtight_PerPart_OpeningLeaks_FrameSeals()
        {
            var g = new BlockGrid();
            Assert.True(g.PlaceMultiBlock(0, 0, 0, TestStructCatalog.GappyWall, 0)); // x=0 рама, x=1 проём, x=2 рама

            Assert.False(AtmosBlocks.IsAirtight(g, 1, 0, 0)); // проём — воздух течёт (было: вся стена герметична по типу)
            Assert.True(AtmosBlocks.IsAirtight(g, 0, 0, 0));
            Assert.True(AtmosBlocks.IsAirtight(g, 2, 0, 0));
        }

        [Fact]
        public void DefaultIsSolid_PerPart_OpeningIsNotSupport()
        {
            var g = new BlockGrid();
            Assert.True(g.PlaceMultiBlock(0, 0, 0, TestStructCatalog.GappyWall, 0));

            Assert.False(BlockAttach.DefaultIsSolid(g, 1, 0, 0)); // клетка проёма опорой не служит
            Assert.True(BlockAttach.DefaultIsSolid(g, 0, 0, 0));
            Assert.True(BlockAttach.DefaultIsSolid(g, 2, 0, 0));
        }

        [Fact]
        public void ZoneClassifier_PerPart_OpeningNotSolid()
        {
            var g = new BlockGrid();
            Assert.True(g.PlaceMultiBlock(0, 0, 0, TestStructCatalog.GappyWall, 0));
            var cls = CatalogZoneClassifier.Instance;

            Assert.False(cls.IsSolid(g, TestStructCatalog.GappyWall, 1, 0, 0)); // проём проходим для зоны
            Assert.True(cls.IsSolid(g, TestStructCatalog.GappyWall, 0, 0, 0));
        }

        [Fact]
        public void Movement_CollidesWithNonAnchorCell_ViaLayer()
        {
            const ushort floorType = 1; // Block: полный куб, верх = 1.0
            var g = new BlockGrid();
            for (int x = -1; x <= 6; x++)
                g.SetBlock(x, 0, 0, floorType);

            // WalkWall: якорь и часть 1 БЕЗ боксов, часть 2 (x=4) — полная. Якорные боксы (2-арг дефолт) дали бы «пусто».
            Assert.True(g.PlaceMultiBlock(2, 1, 0, TestStructCatalog.WalkWall, 0));

            var s = new BlockMoverState(1.5f, 1f, 0.5f);
            var east = new BlockMoveInput(IntentDirection.East, sprint: true);
            for (int i = 0; i < 90; i++)
                BlockMovementLogic.Step(g, BlockCatalogShapes.Instance, ref s, in east, MovementLogic.StepPerTick);

            Assert.InRange(s.X, 3.4f, 3.75f); // прошёл пустые x=2,3 и упёрся в полную НЕ-якорную клетку x=4
            Assert.True(s.Grounded);
        }
    }
}
