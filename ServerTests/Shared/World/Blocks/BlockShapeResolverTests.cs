using Shared.World;
using Shared.World.Blocks;

namespace ServerTests.Shared.World.Blocks
{
    public class BlockShapeResolverTests
    {
        [Fact]
        public void Resolve_AllSixteenMasks()
        {
            Assert.Equal((WallShape.Single, 0), BlockShapeResolver.Resolve(0));

            Assert.Equal((WallShape.End, 0), BlockShapeResolver.Resolve(BlockShapeResolver.North));
            Assert.Equal((WallShape.End, 1), BlockShapeResolver.Resolve(BlockShapeResolver.East));
            Assert.Equal((WallShape.End, 2), BlockShapeResolver.Resolve(BlockShapeResolver.South));
            Assert.Equal((WallShape.End, 3), BlockShapeResolver.Resolve(BlockShapeResolver.West));

            Assert.Equal((WallShape.Straight, 0), BlockShapeResolver.Resolve(BlockShapeResolver.North | BlockShapeResolver.South));
            Assert.Equal((WallShape.Straight, 1), BlockShapeResolver.Resolve(BlockShapeResolver.East | BlockShapeResolver.West));

            Assert.Equal((WallShape.Corner, 0), BlockShapeResolver.Resolve(BlockShapeResolver.North | BlockShapeResolver.East));
            Assert.Equal((WallShape.Corner, 1), BlockShapeResolver.Resolve(BlockShapeResolver.East | BlockShapeResolver.South));
            Assert.Equal((WallShape.Corner, 2), BlockShapeResolver.Resolve(BlockShapeResolver.South | BlockShapeResolver.West));
            Assert.Equal((WallShape.Corner, 3), BlockShapeResolver.Resolve(BlockShapeResolver.West | BlockShapeResolver.North));

            Assert.Equal((WallShape.T, 0), BlockShapeResolver.Resolve(BlockShapeResolver.North | BlockShapeResolver.East | BlockShapeResolver.South));
            Assert.Equal((WallShape.T, 1), BlockShapeResolver.Resolve(BlockShapeResolver.East | BlockShapeResolver.South | BlockShapeResolver.West));
            Assert.Equal((WallShape.T, 2), BlockShapeResolver.Resolve(BlockShapeResolver.South | BlockShapeResolver.West | BlockShapeResolver.North));
            Assert.Equal((WallShape.T, 3), BlockShapeResolver.Resolve(BlockShapeResolver.West | BlockShapeResolver.North | BlockShapeResolver.East));

            Assert.Equal((WallShape.Cross, 0), BlockShapeResolver.Resolve(
                BlockShapeResolver.North | BlockShapeResolver.East | BlockShapeResolver.South | BlockShapeResolver.West));
        }
    }
}
