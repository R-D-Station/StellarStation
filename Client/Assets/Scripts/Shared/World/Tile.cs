namespace Shared.World
{
    /// <summary>
    /// ќдин тайл сетки. „истый C#, без Unity Ч живЄт в Shared, читают и сервер,
    /// и клиент (дл€ рендера), и редактор карт (дл€ экспорта).
    ///
    /// ∆®—“ ќ≈ ѕ–ј¬»Ћќ проекта: симул€ци€ читает “ќЋ№ ќ целый тайл (floor X,
    /// floor Y, z). ƒробные позиции сущностей сюда не протекают.
    ///
    /// “ри флага описывают физику и видимость тайла независимо. »х Ќ≈Ћ№«я
    /// сливать Ч решЄтка и дырка различаютс€ именно комбинацией:
    ///   —плошной пол: Support=true,  BlocksVerticalSight=true
    ///   –ешЄтка:      Support=true,  BlocksVerticalSight=false
    ///   ƒырка:        Support=false, BlocksVerticalSight=false
    ///
    /// Walkable не хранитс€ Ч он вычисл€емый (WallType==0 &amp;&amp; Support),
    /// иначе рассинхрон при правке.
    /// </summary>
    public struct Tile
    {
        /// <summary>“ип пола (рендер; позже Ч герметичность дл€ атмоса). 0 = нет пола/космос.</summary>
        public byte FloorType;

        /// <summary>“ип стены. 0 = стены нет.</summary>
        public byte WallType;

        /// <summary>ƒержит ли тайл ногами. –ешЄтка Ч true, дырка Ч false.</summary>
        public bool Support;

        /// <summary>Ѕлокирует ли горизонтальный обзор (стена) Ч дл€ FOV в плоскости этажа.</summary>
        public bool BlocksHorizontalSight;

        /// <summary>
        /// Ѕлокирует ли вертикальный обзор по Z (в ќЅ≈ стороны). Ёто Ђполї тайла,
        /// и одновременно Ђпотолокї дл€ тайла под ним. —плошной пол Ч true,
        /// решЄтка/дырка/космос Ч false. ќтдельного флага потолка нет:
        /// потолок этажа z = пол этажа z+1.
        /// </summary>
        public bool BlocksVerticalSight;

        /// <summary>
        /// √ерметичен ли тайл по горизонтали (не пропускает газ в плоскости этажа).
        /// ќтдельно от обзора: стекло пропускает взгл€д, но держит газ. —тена Ч
        /// и взгл€д, и газ. ѕотребитель Ч атмос (этап 5); сейчас флаг заложен
        /// заранее, чтобы не версионировать формат карт позже.
        /// </summary>
        public bool SealsHorizontal;

        /// <summary>
        /// √ерметичен ли тайл по вертикали (не пропускает газ между этажами).
        /// —текл€нный пол: BlocksVerticalSight=false, SealsVertical=true (видно
        /// вниз, газ не идЄт). –ешЄтка: оба false (видно и газ проходит).
        /// —плошной пол: оба true. ѕотребитель Ч атмос (этап 5).
        /// </summary>
        public bool SealsVertical;

        /// <summary>ћожно ли войти и встать. ¬ычисл€емое, не хранитс€ в файле.</summary>
        public readonly bool Walkable => WallType == 0 && Support;

        /// <summary>
        /// ћожно войти, но нет опоры Ч шаг разрешЄн, далее падение на z-1.
        /// ¬ычисл€емое. ћеханика падени€ включаетс€ на этапе 3 (Z-переходы).
        /// </summary>
        public readonly bool IsFall => WallType == 0 && !Support;

        /// <summary>ѕустой тайл Ч открытый космос: ни пола, ни стены, ни опоры, ничего не держит.</summary>
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

        /// <summary>—плошной пол: стоишь, взгл€д по Z не проходит, газ снизу/сверху не проходит.</summary>
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