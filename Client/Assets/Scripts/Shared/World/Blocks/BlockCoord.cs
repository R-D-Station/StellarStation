using System;

namespace Shared.World.Blocks
{
    /// <summary>Целочисленная координата блока (1 блок = 1 мировая единица). Оси как в Unity: X/Z — план, Y — высота.</summary>
    public readonly struct BlockCoord : IEquatable<BlockCoord>
    {
        public readonly int X;
        public readonly int Y;
        public readonly int Z;

        public BlockCoord(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>Блок, в котором находится непрерывная позиция (floor по всем осям).</summary>
        public static BlockCoord FromPosition(float x, float y, float z)
            => new BlockCoord(FloorToInt(x), FloorToInt(y), FloorToInt(z));

        // MathF.Floor + каст: корректно для отрицательных (каст к int режет к нулю).
        private static int FloorToInt(float v) => (int)MathF.Floor(v);

        public bool Equals(BlockCoord other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object obj) => obj is BlockCoord other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        public override string ToString() => $"({X}, {Y}, {Z})";

        public static bool operator ==(BlockCoord a, BlockCoord b) => a.Equals(b);
        public static bool operator !=(BlockCoord a, BlockCoord b) => !a.Equals(b);
    }
}
