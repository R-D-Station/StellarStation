namespace Shared.World.Blocks
{
    /// <summary>Сайдкар-разметка блока этажа: имя/ранг/номер этажа — сид флудфилла зон (<see cref="ZoneFlood"/>).</summary>
    public readonly struct FloorSeed
    {
        public readonly string Name;
        /// <summary>Ранг источника; меньше = главнее (админ/мапер &lt;1 всегда перебивает игрока &gt;0).</summary>
        public readonly int Rank;
        public readonly int Floor;

        public FloorSeed(string name, int rank, int floor)
        {
            Name = name ?? string.Empty;
            Rank = rank;
            Floor = floor;
        }

        /// <summary>Change-detection для SetSeed: все три поля совпадают.</summary>
        public bool SameAs(in FloorSeed other)
            => Rank == other.Rank && Floor == other.Floor && string.Equals(Name, other.Name);
    }
}
