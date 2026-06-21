namespace Shared.World
{
    /// <summary>
    /// Один тайл карты (чистый C#, общий для клиента и сервера). Адресуется целыми
    /// координатами (floor X, floor Y, z).
    /// </summary>
    public struct Tile
    {
        /// <summary>Тип пола (0 = пола нет).</summary>
        public byte FloorType;

        /// <summary>Тип стены (0 = стены нет).</summary>
        public byte WallType;

        /// <summary>Есть ли под тайлом опора (пол).</summary>
        public bool Support;

        /// <summary>Перекрывает ли горизонтальный обзор (стена) — для FOV/видимости.</summary>
        public bool BlocksHorizontalSight;

        /// <summary>Перекрывает ли вертикальный обзор по Z (видно ли этаж снизу).</summary>
        public bool BlocksVerticalSight;

        /// <summary>Герметичен ли по горизонтали (для атмосферы; будет позже).</summary>
        public bool SealsHorizontal;

        /// <summary>Герметичен ли по вертикали (для атмосферы; будет позже).</summary>
        public bool SealsVertical;

        /// <summary>Тип двери (0 = двери нет). Дверь динамическая — см. DoorOpen.</summary>
        public byte DoorType;

        /// <summary>Открыта ли дверь сейчас (рантайм-состояние, выставляет сервер).</summary>
        public bool DoorOpen;

        /// <summary>Спец-тайл перехода между этажами (лестница/лифт). См. TileSpecial.</summary>
        public TileSpecial Special;

        /// <summary>Можно ли пройти по тайлу (есть опора, нет стены, дверь открыта).</summary>
        public readonly bool Walkable => Support && WallType == 0 && (DoorType == 0 || DoorOpen);

        /// <summary>Тайл-провал: нет стены и нет опоры — падение на z-1.</summary>
        public readonly bool IsFall => WallType == 0 && !Support;

        /// <summary>Пустой тайл (космос): нет пола, стены и опоры.</summary>
        public static Tile Space => new Tile
        {
            FloorType = 0,
            WallType = 0,
            Support = false,
            BlocksHorizontalSight = false,
            BlocksVerticalSight = false,
            SealsHorizontal = false,
            SealsVertical = false
        };

        /// <summary>Готовый пол: есть опора, перекрывает обзор по Z, проходим.</summary>
        public static Tile Floor(byte floorType = 1) => new Tile
        {
            FloorType = floorType,
            WallType = 0,
            Support = true,
            BlocksHorizontalSight = false,
            BlocksVerticalSight = true,
            SealsHorizontal = false,
            SealsVertical = true
        };
    }
}