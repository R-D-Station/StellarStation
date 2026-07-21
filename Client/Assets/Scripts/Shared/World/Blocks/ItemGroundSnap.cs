using Shared.Simulation.Blocks;

namespace Shared.World.Blocks
{
    public static class ItemGroundSnap
    {
        public const int MaxScanDepth = 64;
        private const float FullTop = 0.999f;

        public static int SnapDown(IBlockSampler grid, IBlockShapes shapes, int x, int startY, int z)
        {
            for (int y = startY; y > startY - MaxScanDepth; y--)
            {
                var boxes = shapes.GetBoxes(grid.GetBlock(x, y, z), grid.GetState(x, y, z));
                if (boxes.Length == 0) continue;

                float partialTop = 0f;
                bool full = false;
                for (int i = 0; i < boxes.Length; i++)
                {
                    float top = boxes[i].MaxYf;
                    if (top >= FullTop) full = true;
                    else if (top > partialTop) partialTop = top;
                }

                if (full) return y + 1;
                if (partialTop > 0f) return y;
            }
            return startY;
        }
    }
}
