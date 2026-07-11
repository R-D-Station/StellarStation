namespace Shared.World.Blocks
{
    /// <summary>
    /// Раскладка state-байта блока (per-block рантайм-состояние экземпляра, второй канал секции):
    /// Open(1) | Pipe(1) | Wire(1) | Facing(2) | Part(2) | резерв(1). Нулевой state не хранится.
    /// </summary>
    public static class BlockState
    {
        /// <summary>Открываемый объект (дверь/люк) сейчас открыт.</summary>
        public const byte Open = 1 << 0;
        /// <summary>В блоке проложена труба (подпольная инфраструктура).</summary>
        public const byte Pipe = 1 << 1;
        /// <summary>В блоке проложен провод.</summary>
        public const byte Wire = 1 << 2;

        private const int FacingShift = 3;
        private const byte FacingMask = 0b11 << FacingShift;
        private const int PartShift = 5;
        private const byte PartMask = 0b11 << PartShift;

        /// <summary>Поворот направленного блока в плане: 0=+Z(север), 1=+X(восток), 2=−Z(юг), 3=−X(запад).</summary>
        public static int GetFacing(byte state) => (state & FacingMask) >> FacingShift;

        public static byte WithFacing(byte state, int facing)
            => (byte)((state & ~FacingMask) | ((facing & 0b11) << FacingShift));

        /// <summary>Часть мульти-блока (дверь 2×1×2): 0=низ-якорь, 1=низ-второй, 2=верх-якорь, 3=верх-второй.</summary>
        public static int GetPart(byte state) => (state & PartMask) >> PartShift;

        public static byte WithPart(byte state, int part)
            => (byte)((state & ~PartMask) | ((part & 0b11) << PartShift));
    }
}
