using Shared.World.Blocks;

namespace ServerTests.Shared.World.Blocks
{
    public class BlockSpawnPointTests
    {
        [Fact]
        public void SpawnPoint_Predicates()
        {
            var spawn = new BlockInfo(1001, "Spawn", BlockCategory.SpawnPoint,
                BlockFaceFlags.None, BlockFaceFlags.None, 0, System.Array.Empty<BlockBox>());
            var divider = new BlockInfo(1002, "Divider", BlockCategory.Divider,
                BlockFaceFlags.None, BlockFaceFlags.None, 0, System.Array.Empty<BlockBox>());
            var merge = new BlockInfo(1003, "Merge", BlockCategory.MergeMarker,
                BlockFaceFlags.None, BlockFaceFlags.None, 0, System.Array.Empty<BlockBox>());

            Assert.True(spawn.IsSpawn);
            Assert.True(spawn.IsMarker);
            Assert.False(spawn.IsFloorAnchor);
            Assert.False(divider.IsSpawn);
            Assert.False(merge.IsSpawn);
            Assert.True(divider.IsMarker);
            Assert.True(merge.IsMarker);
        }

        [Fact]
        public void Find_IsSpawn_IgnoresLowerCoordDivider()
        {
            const ushort spawnId = 100;
            const ushort dividerId = 200;

            var grid = new BlockGrid();
            grid.SetBlock(5, 0, 0, dividerId);
            grid.SetBlock(0, 2, 0, spawnId);

            var spawnResult = BlockWorldSpawn.Find(grid, t => t == spawnId, -1f, -1f, -1f);
            Assert.Equal((0.5f, 2f, 0.5f), spawnResult);

            var markerResult = BlockWorldSpawn.Find(grid, t => t == spawnId || t == dividerId, -1f, -1f, -1f);
            Assert.Equal((5.5f, 0f, 0.5f), markerResult);
        }
    }
}
