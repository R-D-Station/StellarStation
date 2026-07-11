using Shared.World.Blocks;

namespace Shared.Simulation.Blocks
{
    /// <summary>Источник коллизионных боксов по типу и state блока (part/facing мульти-блоков; инъекция ради
    /// тестов без каталога).</summary>
    public interface IBlockShapes
    {
        /// <summary>Боксы позиции; пустой массив = без коллизии. НЕ null и без аллокаций (возврат хранимого массива).</summary>
        BlockBox[] GetBoxes(ushort type, byte state);
    }

    /// <summary>Продакшн-источник: боксы из BlockCatalog (кодоген-зеркало BlockDefinition) с учётом
    /// части и поворота мульти-блока.</summary>
    public sealed class BlockCatalogShapes : IBlockShapes
    {
        public static readonly BlockCatalogShapes Instance = new();

        private BlockCatalogShapes() { }

        public BlockBox[] GetBoxes(ushort type, byte state)
            => BlockCatalog.Get(type).GetBoxes(BlockState.GetPart(state), BlockState.GetFacing(state));
    }
}
