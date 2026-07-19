using Shared.Simulation.Blocks;
using Shared.World.Blocks;
using Client.UI.Labels;

namespace Client.Map
{
    /// <summary>Снимок данных блока и общих сервисов, передаётся render-компонентам один раз при спавне.</summary>
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

    /// <summary>Render-компонент на префабе блока (напр. лейбл): находится один раз при спавне, БЕЗ покадрового Update.</summary>
    public interface IBlockComponent
    {
        /// <summary>Инициализация по данным блока — вызывает BlockView.SpawnComponents при заселении из пула.</summary>
        void OnSpawn(in BlockContext ctx);

        /// <summary>Симметрично OnSpawn — вызывает BlockView.DespawnComponents при возврате в пул.</summary>
        void OnDespawn();

        /// <summary>Синхронизация с cut-away альфой блока (0 = скрыт, вызывает BlockView.SetHidden/UpdateCutaway).</summary>
        void OnVisibility(float alpha);
    }
}
