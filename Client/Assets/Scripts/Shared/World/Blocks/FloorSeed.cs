namespace Shared.World.Blocks
{
    public readonly struct FloorSeed
    {
        public readonly string Name;
        public readonly int Rank;
        public readonly int Floor;

        public FloorSeed(string name, int rank, int floor)
        {
            Name = name ?? string.Empty;
            Rank = rank;
            Floor = floor;
        }

        public bool SameAs(in FloorSeed other)
            => Rank == other.Rank && Floor == other.Floor && string.Equals(Name, other.Name);
    }
}
