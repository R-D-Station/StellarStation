using Shared.World.Blocks;

namespace Shared.Simulation.Blocks
{
    /// <summary>Источник коллизионных боксов по типу и state блока; инъекция ради тестов без каталога.</summary>
    public interface IBlockShapes
    {
        /// <summary>Боксы позиции (пусто = без коллизии); не null, без аллокаций.</summary>
        BlockBox[] GetBoxes(ushort type, byte state);

        /// <summary>Боксы с координатным контекстом: часть мульти-блока берётся из слоя принадлежности грида.
        /// Дефолт игнорирует координаты — тестовые провайдеры и одиночные блоки ничего не платят.</summary>
        BlockBox[] GetBoxes(ushort type, byte state, IBlockSampler grid, int x, int y, int z)
            => GetBoxes(type, state);
    }

    /// <summary>Продакшн-источник: боксы из BlockCatalog с учётом части и поворота мульти-блока.</summary>
    public sealed class BlockCatalogShapes : IBlockShapes
    {
        public static readonly BlockCatalogShapes Instance = new();

        private BlockCatalogShapes() { }

        /// <summary>Сколько раз слой дал часть вне диапазона каталога (диагностика; печатает потребитель — Shared молчит).</summary>
        public static int BadPartReads { get; private set; }

        public BlockBox[] GetBoxes(ushort type, byte state)
            => BlockCatalog.Get(type).GetBoxes(0, BlockState.GetFacing(state), BlockState.GetOpen(state));

        public BlockBox[] GetBoxes(ushort type, byte state, IBlockSampler grid, int x, int y, int z)
        {
            var info = BlockCatalog.Get(type);
            int facing = BlockState.GetFacing(state);
            int part = MultiBlock.PartAt(grid, info.SizeX, info.SizeY, info.SizeZ, facing, x, y, z);
            if (part < 0 || part >= info.PartCount)
            {
                BadPartReads++;
                part = 0;
            }
            return info.GetBoxes(part, facing, BlockState.GetOpen(state));
        }
    }
}
