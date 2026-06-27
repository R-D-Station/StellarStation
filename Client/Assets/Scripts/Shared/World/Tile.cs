namespace Shared.World
{
    /// <summary>
    /// Тайл карты (общий для клиента и сервера). Независимы слой пола (FloorType) и слой
    /// настенного объекта (StructureType — стена/дверь/люк/окно). Поведение объекта — во флагах,
    /// открываемость — Openable/Open. Координаты целые (x, y, z).
    /// </summary>
    public struct Tile
    {
        public byte FloorType;

        /// <summary>Настенный объект: стена/дверь/люк/окно (0 = нет). Вид и категория — в TileCatalog.</summary>
        public byte StructureType;

        public bool Support;
        public bool BlocksHorizontalSight;
        public bool BlocksVerticalSight;
        public bool SealsHorizontal;
        public bool SealsVertical;

        /// <summary>Объект открываемый (дверь/люк), а не глухой (стена/окно).</summary>
        public bool Openable;

        /// <summary>Открыт ли сейчас (рантайм-состояние открываемого объекта, ведёт сервер).</summary>
        public bool Open;

        /// <summary>Спец-тайл перехода между этажами (лестница/лифт). См. TileSpecial.</summary>
        public TileSpecial Special;

        /// <summary>Проходим: есть опора и нет преграды (объекта нет, либо он открываемый и открыт).</summary>
        public readonly bool Walkable => Support && (StructureType == 0 || (Openable && Open));

        /// <summary>Провал: нет опоры и нет глухого объекта — падение на z-1.</summary>
        public readonly bool IsFall => !Support && (StructureType == 0 || Openable);

        /// <summary>Пустой тайл (космос): нет пола, объекта и опоры.</summary>
        public static Tile Space => new Tile
        {
            FloorType = 0,
            StructureType = 0,
            Support = false,
            BlocksHorizontalSight = false,
            BlocksVerticalSight = false,
            SealsHorizontal = false,
            SealsVertical = false,
            Openable = false,
            Open = false
        };

        /// <summary>Готовый пол: есть опора, перекрывает обзор по Z, проходим.</summary>
        public static Tile Floor(byte floorType = 1) => new Tile
        {
            FloorType = floorType,
            StructureType = 0,
            Support = true,
            BlocksHorizontalSight = false,
            BlocksVerticalSight = true,
            SealsHorizontal = false,
            SealsVertical = true
        };
    }
}
