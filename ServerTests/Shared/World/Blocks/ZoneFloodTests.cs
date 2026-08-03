using Shared.World.Blocks;

namespace ServerTests.Shared.World.Blocks
{
    public class ZoneFloodTests
    {
        private const ushort WallT = 1;
        private const ushort FloorT = 2;
        private const ushort DoorT = 3;
        private const ushort AnchorT = 4;
        private const ushort DividerT = 5;
        private const ushort MergeT = 6;

        private sealed class TestClassifier : IZoneClassifier
        {
            public bool IsSolid(ushort t) => t == WallT || t == FloorT;
            public bool IsDoor(ushort t) => t == DoorT;
            public bool IsFloorAnchor(ushort t) => t == AnchorT;
            public bool IsDivider(ushort t) => t == DividerT;
            public bool IsMergeMarker(ushort t) => t == MergeT;
        }

        private static readonly TestClassifier Cls = new();

        private static BlockGrid TwoRooms(ushort gapBlock)
        {
            var g = new BlockGrid();
            for (int x = 0; x <= 9; x++)
                for (int z = 0; z <= 4; z++)
                {
                    g.SetBlock(x, 0, z, FloorT);
                    g.SetBlock(x, 2, z, FloorT);
                }
            for (int x = 0; x <= 9; x++)
            {
                g.SetBlock(x, 1, 0, WallT);
                g.SetBlock(x, 1, 4, WallT);
            }
            for (int z = 0; z <= 4; z++)
            {
                g.SetBlock(0, 1, z, WallT);
                g.SetBlock(9, 1, z, WallT);
            }
            for (int z = 1; z <= 3; z++)
                g.SetBlock(5, 1, z, WallT);
            g.SetBlock(5, 1, 2, gapBlock);
            return g;
        }

        private static void PlaceSeed(BlockGrid g, int x, int y, int z, string name, int rank, int floor)
        {
            g.SetBlock(x, y, z, AnchorT);
            var s = g.GetSection(0, 0, 0);
            s.SetSeed(ChunkSection.LocalIndex(x, y, z), new FloorSeed(name, rank, floor));
        }

        [Fact]
        public void OpenGap_SameFloorSeeds_OneZone()
        {
            var g = TwoRooms(0);
            PlaceSeed(g, 1, 1, 1, "A", 1, 1);
            PlaceSeed(g, 8, 1, 3, "B", 2, 1);

            var r = ZoneFlood.Recompute(g, Cls);

            Assert.Single(r.Zones);
            Assert.Empty(r.Junctions);
            Assert.Empty(r.Conflicts);
            Assert.NotEqual(0, g.GetZone(2, 1, 2));
            Assert.Equal(g.GetZone(2, 1, 2), g.GetZone(7, 1, 2));
            Assert.Equal(2, r.Zones[0].Seeds.Count);
        }

        [Fact]
        public void InternalDoor_OneSeed_OneZone_NoJunction()
        {
            var g = TwoRooms(DoorT);
            PlaceSeed(g, 1, 1, 1, "A", 1, 1);

            var r = ZoneFlood.Recompute(g, Cls);

            Assert.Single(r.Zones);
            Assert.Empty(r.Junctions);
            ushort z = g.GetZone(2, 1, 2);
            Assert.NotEqual(0, z);
            Assert.Equal(z, g.GetZone(7, 1, 2));
            Assert.Equal(z, g.GetZone(5, 1, 2));
        }

        [Fact]
        public void Door_TwoFloors_SplitsWithJunction()
        {
            var g = TwoRooms(DoorT);
            PlaceSeed(g, 1, 1, 1, "A", 1, 1);
            PlaceSeed(g, 8, 1, 3, "B", 1, 2);

            var r = ZoneFlood.Recompute(g, Cls);

            Assert.Equal(2, r.Zones.Count);
            Assert.Empty(r.Conflicts);
            ushort za = g.GetZone(2, 1, 2);
            ushort zb = g.GetZone(7, 1, 2);
            Assert.NotEqual(0, za);
            Assert.NotEqual(0, zb);
            Assert.NotEqual(za, zb);
            Assert.Equal((ushort)0, g.GetZone(5, 1, 2));

            Assert.Single(r.Junctions);
            var j = r.Junctions[0];
            Assert.Equal(new BlockCoord(5, 1, 2), j.Cell);
            Assert.Equal(2, j.Zones.Count);
            Assert.Contains(za, j.Zones);
            Assert.Contains(zb, j.Zones);
        }

        [Fact]
        public void Rank_AdminBeatsPlayer()
        {
            var g = TwoRooms(0);
            PlaceSeed(g, 1, 1, 1, "Игрок", 5, 3);
            PlaceSeed(g, 8, 1, 3, "Админ", -1, 3);

            var r = ZoneFlood.Recompute(g, Cls);

            Assert.Single(r.Zones);
            Assert.Equal(-1, r.Zones[0].Rank);
            Assert.Equal("Админ", r.Zones[0].Name);
            Assert.Equal(3, r.Zones[0].Floor);
        }

        [Fact]
        public void DividerBlock_SplitsOpenGap()
        {
            var g = TwoRooms(DividerT);
            PlaceSeed(g, 1, 1, 1, "A", 1, 1);
            PlaceSeed(g, 8, 1, 3, "B", 1, 2);

            var r = ZoneFlood.Recompute(g, Cls);

            Assert.Equal(2, r.Zones.Count);
            Assert.Empty(r.Conflicts);
            Assert.Empty(r.Junctions);
            Assert.NotEqual(g.GetZone(2, 1, 2), g.GetZone(7, 1, 2));
        }

        [Fact]
        public void BakeDividerOnDoor_HardBoundary()
        {
            var g = TwoRooms(DoorT);
            g.SetBake(5, 1, 2, ChunkSection.BakeDivider);
            PlaceSeed(g, 1, 1, 1, "A", 1, 1);

            var r = ZoneFlood.Recompute(g, Cls);

            Assert.Single(r.Zones);
            Assert.Empty(r.Junctions);
            Assert.NotEqual(0, g.GetZone(2, 1, 2));
            Assert.Equal((ushort)0, g.GetZone(7, 1, 2));
        }

        [Fact]
        public void MergeMarker_JoinsRooms()
        {
            var g = TwoRooms(MergeT);
            PlaceSeed(g, 1, 1, 1, "A", 1, 1);
            PlaceSeed(g, 8, 1, 3, "B", 2, 1);

            var r = ZoneFlood.Recompute(g, Cls);

            Assert.Single(r.Zones);
            Assert.Equal(g.GetZone(2, 1, 2), g.GetZone(7, 1, 2));
        }

        [Fact]
        public void BakeMergeOnDoor_ForcesOneZone_DifferentFloorsConflict()
        {
            var g = TwoRooms(DoorT);
            g.SetBake(5, 1, 2, ChunkSection.BakeMerge);
            PlaceSeed(g, 1, 1, 1, "A", 1, 1);
            PlaceSeed(g, 8, 1, 3, "B", 1, 2);

            var r = ZoneFlood.Recompute(g, Cls);

            Assert.Single(r.Zones);
            Assert.Empty(r.Junctions);
            Assert.Single(r.Conflicts);
            Assert.Equal(new[] { 1, 2 }, r.Conflicts[0].Floors);
            Assert.Equal(g.GetZone(2, 1, 2), g.GetZone(7, 1, 2));
        }

        [Fact]
        public void Conflict_OpenAir_DifferentFloors()
        {
            var g = TwoRooms(0);
            PlaceSeed(g, 1, 1, 1, "A", 1, 1);
            PlaceSeed(g, 8, 1, 3, "B", 1, 2);

            var r = ZoneFlood.Recompute(g, Cls);

            Assert.Single(r.Zones);
            Assert.Single(r.Conflicts);
            Assert.Equal(r.Zones[0].Id, r.Conflicts[0].ZoneId);
            Assert.Equal(new[] { 1, 2 }, r.Conflicts[0].Floors);
        }

        [Fact]
        public void Unseeded_ZoneZero()
        {
            var g = TwoRooms(DoorT);

            var r = ZoneFlood.Recompute(g, Cls);

            Assert.Empty(r.Zones);
            Assert.Empty(r.Junctions);
            Assert.Equal((ushort)0, g.GetZone(2, 1, 2));
            Assert.Equal((ushort)0, g.GetZone(7, 1, 2));
        }

        [Fact]
        public void Determinism_SeedOrderAndRerun()
        {
            var g1 = TwoRooms(DoorT);
            PlaceSeed(g1, 1, 1, 1, "A", 1, 1);
            PlaceSeed(g1, 8, 1, 3, "B", 1, 2);

            var g2 = TwoRooms(DoorT);
            PlaceSeed(g2, 8, 1, 3, "B", 1, 2);
            PlaceSeed(g2, 1, 1, 1, "A", 1, 1);

            var r1 = ZoneFlood.Recompute(g1, Cls);
            var r2 = ZoneFlood.Recompute(g2, Cls);
            var r1Again = ZoneFlood.Recompute(g1, Cls);

            AssertSameOutcome(g1, g2, r1, r2);
            AssertSameOutcome(g1, g1, r1, r1Again);
        }

        private static void AssertSameOutcome(BlockGrid ga, BlockGrid gb, ZoneFloodResult ra, ZoneFloodResult rb)
        {
            for (int x = 0; x <= 9; x++)
                for (int z = 0; z <= 4; z++)
                    Assert.Equal(ga.GetZone(x, 1, z), gb.GetZone(x, 1, z));

            Assert.Equal(ra.Zones.Count, rb.Zones.Count);
            for (int i = 0; i < ra.Zones.Count; i++)
            {
                Assert.Equal(ra.Zones[i].Id, rb.Zones[i].Id);
                Assert.Equal(ra.Zones[i].Name, rb.Zones[i].Name);
                Assert.Equal(ra.Zones[i].Rank, rb.Zones[i].Rank);
                Assert.Equal(ra.Zones[i].Floor, rb.Zones[i].Floor);
            }

            Assert.Equal(ra.Junctions.Count, rb.Junctions.Count);
            for (int i = 0; i < ra.Junctions.Count; i++)
            {
                Assert.Equal(ra.Junctions[i].Cell, rb.Junctions[i].Cell);
                Assert.Equal(ra.Junctions[i].Zones, rb.Junctions[i].Zones);
            }

            Assert.Equal(ra.Conflicts.Count, rb.Conflicts.Count);
            for (int i = 0; i < ra.Conflicts.Count; i++)
            {
                Assert.Equal(ra.Conflicts[i].ZoneId, rb.Conflicts[i].ZoneId);
                Assert.Equal(ra.Conflicts[i].Floors, rb.Conflicts[i].Floors);
            }
        }

        private static void Bake(BlockGrid g, int x, int y, int z, byte bake)
            => Assert.True(g.SetBake(x, y, z, bake), $"SetBake должен примениться в ({x},{y},{z})");

        [Fact]
        public void DividerBake_OnAirCell_CutsZone()
        {
            var g = TwoRooms(0);
            PlaceSeed(g, 1, 1, 1, "A", 1, 1);
            PlaceSeed(g, 8, 1, 3, "B", 2, 1);
            Assert.Equal(0, g.GetBlock(5, 1, 2));

            Bake(g, 5, 1, 2, ChunkSection.BakeDivider);
            var r = ZoneFlood.Recompute(g, Cls);

            Assert.Equal(2, r.Zones.Count);
            Assert.NotEqual(g.GetZone(2, 1, 2), g.GetZone(7, 1, 2));
        }

        [Fact]
        public void DividerBake_SurvivesBlockPlacementAndRemoval()
        {
            var g = TwoRooms(0);
            PlaceSeed(g, 1, 1, 1, "A", 1, 1);
            PlaceSeed(g, 8, 1, 3, "B", 2, 1);
            Bake(g, 5, 1, 2, ChunkSection.BakeDivider);

            g.SetBlock(5, 1, 2, DoorT);
            Assert.Equal(ChunkSection.BakeDivider, g.GetBake(5, 1, 2));
            Assert.Equal(2, ZoneFlood.Recompute(g, Cls).Zones.Count);
            Assert.NotEqual(g.GetZone(2, 1, 2), g.GetZone(7, 1, 2));

            g.SetBlock(5, 1, 2, 0);
            Assert.Equal(ChunkSection.BakeDivider, g.GetBake(5, 1, 2));
            Assert.Equal(2, ZoneFlood.Recompute(g, Cls).Zones.Count);
        }

        [Fact]
        public void VisualBakeBits_ClearedOnRemoval_CellBitsKept()
        {
            var g = TwoRooms(0);
            Bake(g, 5, 1, 1, (byte)(ChunkSection.BakeCeiling | ChunkSection.BakeDivider));

            g.SetBlock(5, 1, 1, 0);

            Assert.Equal(ChunkSection.BakeDivider, g.GetBake(5, 1, 1));
        }

        [Fact]
        public void GateToExterior_IsNotZoned()
        {
            var g = TwoRooms(0);
            g.SetBlock(5, 1, 2, WallT);
            g.SetBlock(9, 1, 2, DoorT);
            PlaceSeed(g, 8, 1, 3, "B", 1, 1);

            var r = ZoneFlood.Recompute(g, Cls);

            Assert.Single(r.Zones);
            Assert.NotEqual(0, g.GetZone(7, 1, 2));
            Assert.Equal(0, g.GetZone(9, 1, 2));
        }

        [Fact]
        public void GateBetweenRoomsOfSameZone_IsZoned()
        {
            var g = TwoRooms(DoorT);
            PlaceSeed(g, 1, 1, 1, "A", 1, 1);
            PlaceSeed(g, 8, 1, 3, "B", 2, 1);

            var r = ZoneFlood.Recompute(g, Cls);

            Assert.Single(r.Zones);
            Assert.NotEqual(0, g.GetZone(5, 1, 2));
        }

        [Fact]
        public void GateToUnseededClosedRoom_KeepsZone()
        {
            var g = TwoRooms(DoorT);
            PlaceSeed(g, 1, 1, 1, "A", 1, 1);

            var r = ZoneFlood.Recompute(g, Cls);

            Assert.Single(r.Zones);
            Assert.NotEqual(0, g.GetZone(5, 1, 2));
            Assert.Empty(r.Leaks);
        }

        [Fact]
        public void SeededRegionOpenToExterior_ReportsLeak()
        {
            var g = TwoRooms(0);
            PlaceSeed(g, 1, 1, 1, "A", 1, 1);
            Assert.Empty(ZoneFlood.Recompute(g, Cls).Leaks);

            g.SetBlock(9, 1, 2, 0);
            var r = ZoneFlood.Recompute(g, Cls);

            Assert.Single(r.Leaks);
            Assert.Equal(g.GetZone(2, 1, 2), r.Leaks[0].ZoneId);
        }

        [Fact]
        public void ExteriorRegion_GetsExteriorZoneId()
        {
            var g = TwoRooms(0);
            PlaceSeed(g, 1, 1, 1, "A", 1, 1);

            ZoneFlood.Recompute(g, Cls);

            Assert.Equal(ZoneFlood.ExteriorZoneId, g.GetZone(11, 1, 2));
            Assert.Equal(ZoneFlood.ExteriorZoneId, g.GetZone(13, 5, 9));
        }

        [Fact]
        public void InteriorAndExterior_AreDifferentZones_NotMergedThroughDoor()
        {
            var g = TwoRooms(0);
            g.SetBlock(5, 1, 2, WallT);
            g.SetBlock(9, 1, 2, DoorT);
            PlaceSeed(g, 8, 1, 3, "B", 1, 1);

            var r = ZoneFlood.Recompute(g, Cls);

            ushort inside = g.GetZone(7, 1, 2);
            Assert.Single(r.Zones);
            Assert.NotEqual(0, inside);
            Assert.NotEqual(ZoneFlood.ExteriorZoneId, inside);
            Assert.Equal(ZoneFlood.ExteriorZoneId, g.GetZone(11, 1, 2));
            Assert.Equal(0, g.GetZone(9, 1, 2));
            Assert.Empty(r.Leaks);
        }

        [Fact]
        public void ClosedUnseededRoom_StaysZoneless_NotExterior()
        {
            var g = TwoRooms(WallT);
            PlaceSeed(g, 1, 1, 1, "A", 1, 1);

            ZoneFlood.Recompute(g, Cls);

            Assert.NotEqual(0, g.GetZone(2, 1, 2));
            Assert.Equal(0, g.GetZone(7, 1, 2));
        }

        [Fact]
        public void LeakingSeededRegion_KeepsOwnZone_NotExterior()
        {
            var g = TwoRooms(0);
            PlaceSeed(g, 1, 1, 1, "A", 1, 1);
            g.SetBlock(9, 1, 2, 0);

            var r = ZoneFlood.Recompute(g, Cls);

            ushort z = g.GetZone(2, 1, 2);
            Assert.NotEqual(0, z);
            Assert.NotEqual(ZoneFlood.ExteriorZoneId, z);
            Assert.Single(r.Leaks);
        }
    }
}
