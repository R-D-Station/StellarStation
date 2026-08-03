namespace Shared.World.Blocks
{
    /// <summary>Раскладка state-байта блока: Open(1) | Pipe(1) | Wire(1) | Facing(2) | резерв(3).
    /// С v15 номер части мульти-блока НЕ в state: принадлежность живёт в слое смещений до якоря (BlockGrid.AnchorOf).</summary>
    public static class BlockState
    {
        /// <summary>Открываемый объект (дверь/люк) сейчас открыт.</summary>
        public const byte Open = 1 << 0;

        /// <summary>Открыт ли (бит Open).</summary>
        public static bool GetOpen(byte state) => (state & Open) != 0;

        public static byte WithOpen(byte state, bool open)
            => (byte)(open ? state | Open : state & ~Open);
        /// <summary>В блоке проложена труба (подпольная инфраструктура).</summary>
        public const byte Pipe = 1 << 1;
        /// <summary>В блоке проложен провод.</summary>
        public const byte Wire = 1 << 2;

        private const int FacingShift = 3;
        private const byte FacingMask = 0b11 << FacingShift;

        /// <summary>Поворот направленного блока в плане: 0=+Z(север), 1=+X(восток), 2=−Z(юг), 3=−X(запад).</summary>
        public static int GetFacing(byte state) => (state & FacingMask) >> FacingShift;

        public static byte WithFacing(byte state, int facing)
            => (byte)((state & ~FacingMask) | ((facing & 0b11) << FacingShift));
    }
}
