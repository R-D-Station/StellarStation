namespace Client.Lifts
{
    public struct BlockComponentPairing
    {
        private bool _spawned;
        private int _spawns;
        private int _despawns;

        public bool Spawned => _spawned;

        public int Outstanding => _spawns - _despawns;

        public bool BeginSpawn()
        {
            if (_spawned)
                return false;
            _spawned = true;
            _spawns++;
            return true;
        }

        public bool BeginDespawn()
        {
            if (!_spawned)
                return false;
            _spawned = false;
            _despawns++;
            return true;
        }
    }
}
