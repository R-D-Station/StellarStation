using Shared.Simulation.Blocks;
using Shared.World.Blocks;
using Client.UI.Labels;

namespace Client.Map
{
    public readonly struct BlockContext
    {
        public readonly int X, Y, Z;
        public readonly ushort Type;
        public readonly BlockGrid Grid;
        public readonly IBlockShapes Shapes;
        public readonly LabelManager Labels;

        public BlockContext(int x, int y, int z, ushort type, BlockGrid grid, IBlockShapes shapes, LabelManager labels)
        {
            X = x; Y = y; Z = z; Type = type; Grid = grid; Shapes = shapes; Labels = labels;
        }
    }

    public interface IBlockComponent
    {
        void OnSpawn(in BlockContext ctx);
        void OnDespawn();
        void OnVisibility(float alpha);
    }
}
