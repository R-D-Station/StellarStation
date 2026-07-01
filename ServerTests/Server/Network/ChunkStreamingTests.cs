using Server.Network;
using Shared.Messages.Core;
using Shared.World;

namespace ServerTests.Server.Network
{
    /// <summary>
    /// stream-2.3a: wire round-trip ChunkData/ChunkUnload + логика per-client стриминга. ProcessStreaming/_clients
    /// приватны → зеркалим стрим-тик на РЕАЛЬНЫХ GridMap/ClientConnection (как FsmStage/Fall-тесты). Проверяем:
    /// in-range по радиусу+z, отсутствие двойной отправки, таймер выгрузки, чанк под ногами не «роняется»,
    /// pack/unpack ключа (детерминизм + знак).
    /// </summary>
    public class ChunkStreamingTests
    {
        private const int TileBytes = 4; // v3-тайл: FloorType+StructureType+Special+flags

        // ── wire round-trip ──────────────────────────────────────────────────────────────

        [Fact]
        public void ChunkData_RoundTrips_AllTileKinds()
        {
            var chunk = new Chunk(-2, 3, 1);

            var stair = Tile.Floor();
            stair.Special = TileSpecial.StairUp;
            chunk[0, 0] = stair;

            var door = Tile.Floor();
            door.StructureType = 3; door.Openable = true; door.Open = true;
            chunk[5, 7] = door;
            // chunk[1,1] остаётся Tile.Space (дефолт конструктора) — проверим, что космос тоже сохраняется.

            var bytes = new ChunkData { Chunk = chunk }.Serialize();
            Assert.Equal(12 + Chunk.TileCount * TileBytes, bytes.Length);

            var back = new ChunkData();
            back.Deserialize(bytes);
            Assert.NotNull(back.Chunk);

            Assert.Equal(-2, back.Chunk.ChunkX);
            Assert.Equal(3, back.Chunk.ChunkY);
            Assert.Equal(1, back.Chunk.Z);

            Assert.Equal(TileSpecial.StairUp, back.Chunk[0, 0].Special);
            Assert.True(back.Chunk[0, 0].Walkable);

            Assert.Equal((byte)3, back.Chunk[5, 7].StructureType);
            Assert.True(back.Chunk[5, 7].Open);

            Assert.Equal(Tile.Space.FloorType, back.Chunk[1, 1].FloorType);
            Assert.Equal(Tile.Space.Support, back.Chunk[1, 1].Support);
        }

        [Fact]
        public void ChunkData_RejectsWrongSize()
        {
            var back = new ChunkData();
            Assert.Throws<ArgumentException>(() => back.Deserialize(new byte[10]));
        }

        [Fact]
        public void ChunkUnload_RoundTrips_12Bytes()
        {
            var bytes = new ChunkUnload { ChunkX = -5, ChunkY = 9, Z = -1 }.Serialize();
            Assert.Equal(12, bytes.Length);

            var back = new ChunkUnload();
            back.Deserialize(bytes);
            Assert.Equal(-5, back.ChunkX);
            Assert.Equal(9, back.ChunkY);
            Assert.Equal(-1, back.Z);
        }

        [Fact]
        public void ChunkUnload_RejectsWrongSize()
        {
            var back = new ChunkUnload();
            Assert.Throws<ArgumentException>(() => back.Deserialize(new byte[8]));
        }

        // ── pack/unpack ключа чанка (детерминизм ключа + sign-extend) ─────────────────────

        [Theory]
        [InlineData(0, 0, 0)]
        [InlineData(1, 2, 3)]
        [InlineData(-1, -1, -1)]
        [InlineData(-1000, 1000, -5)]
        [InlineData(1048575, -1048576, 7)] // границы 21-битного знакового поля
        public void ChunkKey_PackUnpack_RoundTrips(int cx, int cy, int z)
        {
            long key = GridMap.Key(cx, cy, z);
            GridMap.UnpackKey(key, out int rcx, out int rcy, out int rz);
            Assert.Equal(cx, rcx);
            Assert.Equal(cy, rcy);
            Assert.Equal(z, rz);
        }

        // ── логика стрима (зеркало ProcessStreaming, реальные GridMap/ClientConnection) ────

        [Fact]
        public void InRange_SendsExistingChunk_Once_NoDoubleSend()
        {
            var map = MapWithChunk(0, 0, 0);
            var c = At(8f, 8f, 0); // чанк (0,0,0)

            var t1 = StreamTick(c, map, radius: 1, depth: 1, timeoutTicks: 100, now: 0);
            Assert.Contains((0, 0, 0), t1.sent);
            Assert.Single(t1.sent); // только существующий чанк, пустые окна не шлём

            var t2 = StreamTick(c, map, radius: 1, depth: 1, timeoutTicks: 100, now: 5);
            Assert.Empty(t2.sent);     // уже отправлен → без повторной отправки
            Assert.Empty(t2.unloaded); // всё ещё в радиусе
        }

        [Fact]
        public void OutOfRange_ChunkNotSent()
        {
            var map = MapWithChunk(5, 0, 0); // чанк далеко (cx=5)
            var c = At(8f, 8f, 0);           // игрок в (0,0,0), radius=1

            var t = StreamTick(c, map, radius: 1, depth: 1, timeoutTicks: 100, now: 0);
            Assert.Empty(t.sent);
        }

        [Fact]
        public void EmptyWindow_SendsNothing()
        {
            var map = MapWithChunk(0, 0, 0); // чанк только на z=0
            var c = At(8f, 8f, 5);           // игрок на z=5, depth=1 → z∈[4..6], чанков нет

            var t = StreamTick(c, map, radius: 1, depth: 1, timeoutTicks: 100, now: 0);
            Assert.Empty(t.sent);
        }

        [Fact]
        public void ZDepth_StreamsFloorsAboveAndBelow()
        {
            var map = MapWithChunk(0, 0, 0);
            Fill(map, 0, 0, 1);
            Fill(map, 0, 0, 2);
            var c = At(8f, 8f, 1); // текущий этаж z=1, depth=1 → z∈[0..2]

            var t = StreamTick(c, map, radius: 0, depth: 1, timeoutTicks: 100, now: 0);
            Assert.Contains((0, 0, 0), t.sent);
            Assert.Contains((0, 0, 1), t.sent);
            Assert.Contains((0, 0, 2), t.sent);
            Assert.Equal(3, t.sent.Count);
        }

        [Fact]
        public void OutOfRange_UnloadsAfterTimeout()
        {
            var map = MapWithChunk(0, 0, 0);
            Fill(map, 100, 0, 0); // дальний чанк (cx=100)
            var c = At(8f, 8f, 0);

            StreamTick(c, map, 1, 1, timeoutTicks: 5, now: 0); // отправили (0,0,0), last=0
            Assert.Contains(GridMap.Key(0, 0, 0), c.SentChunks);

            c.X = 1608f; // чанк (100,0,0) — далеко от (0,0,0)

            var mid = StreamTick(c, map, 1, 1, timeoutTicks: 5, now: 3); // 3-0=3 ≤ 5 → ещё не выгружаем
            Assert.DoesNotContain((0, 0, 0), mid.unloaded);
            Assert.Contains(GridMap.Key(0, 0, 0), c.SentChunks);

            var late = StreamTick(c, map, 1, 1, timeoutTicks: 5, now: 6); // 6-0=6 > 5 → выгрузка
            Assert.Contains((0, 0, 0), late.unloaded);
            Assert.DoesNotContain(GridMap.Key(0, 0, 0), c.SentChunks);
            Assert.DoesNotContain(GridMap.Key(0, 0, 0), c.ChunkLastInRangeTick.Keys);
        }

        [Fact]
        public void ReturnBeforeTimeout_ChunkStaysLoaded()
        {
            var map = MapWithChunk(0, 0, 0);
            Fill(map, 100, 0, 0);
            var c = At(8f, 8f, 0);

            StreamTick(c, map, 1, 1, timeoutTicks: 5, now: 0); // (0,0,0) отправлен, last=0

            c.X = 1608f; // ушёл далеко
            StreamTick(c, map, 1, 1, timeoutTicks: 5, now: 3); // 3-0=3 ≤ 5 → держится

            c.X = 8f; // вернулся до таймаута
            var back = StreamTick(c, map, 1, 1, timeoutTicks: 5, now: 6); // рефреш last=6 → не выгружаем
            Assert.DoesNotContain((0, 0, 0), back.unloaded);
            Assert.Contains(GridMap.Key(0, 0, 0), c.SentChunks);
        }

        [Fact]
        public void ChunkUnderFeet_NeverUnloaded_EvenPastTimeout()
        {
            var map = MapWithChunk(0, 0, 0);
            var c = At(8f, 8f, 0); // стоит на месте

            StreamTick(c, map, 1, 1, timeoutTicks: 5, now: 0);
            var far = StreamTick(c, map, 1, 1, timeoutTicks: 5, now: 100); // ≫ таймаута, но чанк в радиусе → рефреш
            Assert.Empty(far.unloaded);
            Assert.Contains(GridMap.Key(0, 0, 0), c.SentChunks);
        }

        // ── helpers: зеркало приватного ProcessStreaming (ключ чанка — через публичный GridMap.Key/UnpackKey) ──

        private static (List<(int, int, int)> sent, List<(int, int, int)> unloaded) StreamTick(
            ClientConnection c, GridMap map, int radius, int depth, int timeoutTicks, int now)
        {
            var sent = new List<(int, int, int)>();
            var unloaded = new List<(int, int, int)>();

            int pcx = FloorDiv((int)System.MathF.Floor(c.X), Chunk.Size);
            int pcy = FloorDiv((int)System.MathF.Floor(c.Y), Chunk.Size);
            int pz = c.Z;

            for (int dz = -depth; dz <= depth; dz++)
            {
                int z = pz + dz;
                for (int dcx = -radius; dcx <= radius; dcx++)
                    for (int dcy = -radius; dcy <= radius; dcy++)
                    {
                        int cx = pcx + dcx, cy = pcy + dcy;
                        if (map.GetChunk(cx, cy, z) == null) continue;
                        long key = GridMap.Key(cx, cy, z);
                        if (c.SentChunks.Add(key)) sent.Add((cx, cy, z));
                        c.ChunkLastInRangeTick[key] = now;
                    }
            }

            var scratch = new List<long>();
            foreach (long key in c.SentChunks)
                if (c.ChunkLastInRangeTick.TryGetValue(key, out int last) && now - last > timeoutTicks)
                    scratch.Add(key);
            foreach (long key in scratch)
            {
                GridMap.UnpackKey(key, out int cx, out int cy, out int cz);
                unloaded.Add((cx, cy, cz));
                c.SentChunks.Remove(key);
                c.ChunkLastInRangeTick.Remove(key);
            }
            return (sent, unloaded);
        }

        private static int FloorDiv(int a, int b) { int q = a / b; if ((a % b != 0) && ((a < 0) != (b < 0))) q--; return q; }

        private static ClientConnection At(float x, float y, int z) => new ClientConnection(null!, 1) { X = x, Y = y, Z = z };

        // Заполнить один тайл чанка (cx,cy,z), чтобы чанк существовал в _map.
        private static void Fill(GridMap map, int cx, int cy, int z) =>
            map.SetTile(cx * Chunk.Size, cy * Chunk.Size, z, Tile.Floor());

        private static GridMap MapWithChunk(int cx, int cy, int z)
        {
            var map = new GridMap();
            Fill(map, cx, cy, z);
            return map;
        }
    }
}
