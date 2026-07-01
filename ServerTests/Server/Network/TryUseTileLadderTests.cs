using Server.Network;
using Shared.World;

namespace ServerTests.Server.Network
{
    /// <summary>
    /// ladder-mvp: высадка лестницы (GameServer.TryFindLanding). Парный тайл walkable → на него (старое поведение);
    /// иначе первый walkable-сосед N/E/S/W (ладдер-проём наверху — блок-структура, на неё не встать → сходим рядом);
    /// нет walkable — перехода нет (гард «лестница в никуда»). TryUseTile применяет (nx,ny,targetZ) к клиенту (тривиально).
    /// </summary>
    public class TryUseTileLadderTests
    {
        private static Tile Blocked() => new Tile { StructureType = 1, Support = true }; // структура non-openable → !Walkable

        [Fact]
        public void WalkablePair_LandsOnPair()
        {
            var map = new GridMap();
            map.SetTile(0, 0, 1, Tile.Floor()); // парный тайл walkable
            Assert.True(GameServer.TryFindLanding(map, 0, 0, 1, out int nx, out int ny));
            Assert.Equal(0, nx);
            Assert.Equal(0, ny);
        }

        [Fact]
        public void BlockedPair_WalkableNeighbor_LandsOnNeighbor()
        {
            var map = new GridMap();
            map.SetTile(0, 0, 1, Blocked());     // парный тайл — блок-структура (ладдер-проём вниз)
            map.SetTile(0, 1, 1, Tile.Floor());  // N-сосед walkable
            Assert.True(GameServer.TryFindLanding(map, 0, 0, 1, out int nx, out int ny));
            Assert.Equal(0, nx);
            Assert.Equal(1, ny); // высадился РЯДОМ, не на blocked-тайле
        }

        [Fact]
        public void BlockedPair_AllNeighborsBlocked_NoLanding()
        {
            var map = new GridMap();
            map.SetTile(0, 0, 1, Blocked());
            map.SetTile(0, 1, 1, Blocked());
            map.SetTile(1, 0, 1, Blocked());
            map.SetTile(0, -1, 1, Blocked());
            map.SetTile(-1, 0, 1, Blocked());
            Assert.False(GameServer.TryFindLanding(map, 0, 0, 1, out int nx, out int ny)); // некуда → false
            Assert.Equal(0, nx); // out-дефолт = сам тайл (не применяется, т.к. false)
            Assert.Equal(0, ny);
        }

        [Fact]
        public void BlockedPair_NeighborOrder_PrefersNorthThenEast()
        {
            var map = new GridMap();
            map.SetTile(0, 0, 1, Blocked());
            map.SetTile(0, 1, 1, Tile.Floor()); // N walkable
            map.SetTile(1, 0, 1, Tile.Floor()); // E тоже walkable
            Assert.True(GameServer.TryFindLanding(map, 0, 0, 1, out int nx, out int ny));
            Assert.Equal(0, nx); // N (порядок N/E/S/W) выигрывает
            Assert.Equal(1, ny);
        }

        [Fact]
        public void NoChunkAbove_NoLanding()
        {
            var map = new GridMap();
            map.SetTile(0, 0, 0, Tile.Floor()); // только нижний этаж; targetZ=1 — космос (Space, !Walkable)
            Assert.False(GameServer.TryFindLanding(map, 0, 0, 1, out _, out _));
        }
    }
}
