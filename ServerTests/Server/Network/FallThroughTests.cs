using Server.Network;
using Shared.Simulation;
using Shared.World;

namespace ServerTests.Server.Network
{
    /// <summary>
    /// Mapping-2.2 / fall-partial-overlap: серверный fall-through — падаем, только если СТРОГО >50% футпринта
    /// игрока (AABB радиуса CollisionRadius) над IsFall-тайлами. ProcessFalls/_clients приватны → зеркалим per-client
    /// тик (доля перекрытия + guard-на-Z-1) на РЕАЛЬНЫХ GridMap/ClientConnection. Геометрия на суб-тайловых X/Y.
    /// </summary>
    public class FallThroughTests
    {
        private static int FloorDiv(int a, int b) { int q = a / b; if ((a % b != 0) && ((a < 0) != (b < 0))) q--; return q; }

        // Зеркало GameServer.ProcessFalls для одного клиента. Возвращает true, если упал (Z уменьшился).
        private static bool FallTick(ClientConnection c, GridMap map)
        {
            const float r = MovementLogic.CollisionRadius;
            const float total = (2f * r) * (2f * r);
            const float halfEps = 1e-4f; // float-допуск: ровно-50/50 держит (как в GameServer.ProcessFalls)

            float minX = c.X - r, maxX = c.X + r, minY = c.Y - r, maxY = c.Y + r;
            float holeArea = 0f;
            for (int tx = (int)System.MathF.Floor(minX); tx <= (int)System.MathF.Floor(maxX); tx++)
            {
                float ox = System.MathF.Max(0f, System.MathF.Min(maxX, tx + 1) - System.MathF.Max(minX, tx));
                if (ox <= 0f) continue;
                for (int ty = (int)System.MathF.Floor(minY); ty <= (int)System.MathF.Floor(maxY); ty++)
                {
                    if (!map.GetTile(tx, ty, c.Z).IsFall) continue;
                    float oy = System.MathF.Max(0f, System.MathF.Min(maxY, ty + 1) - System.MathF.Max(minY, ty));
                    holeArea += ox * oy;
                }
            }
            if (holeArea <= 0.5f * total + halfEps) return false;

            int px = (int)System.MathF.Floor(c.X), py = (int)System.MathF.Floor(c.Y);
            if (map.GetChunk(FloorDiv(px, Chunk.Size), FloorDiv(py, Chunk.Size), c.Z - 1) == null) return false;
            c.Z--;
            return true;
        }

        // Карта: на z1 колонка «пол(tx=0) | дыра(tx=1)» в ряду y=0; на z0 — пол (чанк ниже существует).
        private static GridMap FloorHoleBoundary()
        {
            var map = new GridMap();
            map.SetTile(0, 0, 1, Tile.Floor());
            map.SetTile(1, 0, 1, Tile.Space);   // дыра
            map.SetTile(0, 0, 0, Tile.Floor());
            map.SetTile(1, 0, 0, Tile.Floor());
            return map;
        }

        private static ClientConnection At(float x, float y, int z) => new ClientConnection(null!, 1) { X = x, Y = y, Z = z };

        [Fact]
        public void FullyOverHole_Falls()
        {
            var c = At(1.5f, 0.5f, 1); // футпринт целиком в дыре (tx=1)
            Assert.True(FallTick(c, FloorHoleBoundary()));
            Assert.Equal(0, c.Z);
        }

        [Fact]
        public void FullyOverFloor_DoesNotFall()
        {
            var c = At(0.5f, 0.5f, 1); // футпринт целиком на полу (tx=0)
            Assert.False(FallTick(c, FloorHoleBoundary()));
            Assert.Equal(1, c.Z);
        }

        [Fact]
        public void Exactly50PercentOverHole_DoesNotFall()
        {
            var c = At(1.0f, 0.5f, 1); // центр на границе пол/дыра → ровно 50% над дырой (строго >50% нужно)
            Assert.False(FallTick(c, FloorHoleBoundary()));
            Assert.Equal(1, c.Z);
        }

        [Fact]
        public void MoreThan50PercentOverHole_Falls()
        {
            var c = At(1.1f, 0.5f, 1); // ~61% футпринта над дырой (tx=1)
            Assert.True(FallTick(c, FloorHoleBoundary()));
            Assert.Equal(0, c.Z);
        }

        [Fact]
        public void LessThan50PercentOverHole_DoesNotFall()
        {
            var c = At(0.9f, 0.5f, 1); // ~39% футпринта над дырой — край держит
            Assert.False(FallTick(c, FloorHoleBoundary()));
            Assert.Equal(1, c.Z);
        }

        [Fact]
        public void OnHole_NoChunkBelow_DoesNotFallIntoVoid()
        {
            var map = new GridMap();
            map.SetTile(0, 0, 1, Tile.Space); // провал на z1, ниже (z0) чанка НЕТ
            var c = At(0.5f, 0.5f, 1);
            Assert.False(FallTick(c, map)); // >50% над дырой, но guard: нет чанка на z0 → стоп
            Assert.Equal(1, c.Z);
        }

        [Fact]
        public void MultiFloorHole_FallsSeveralTicks()
        {
            var map = new GridMap();
            map.SetTile(0, 0, 2, Tile.Space);
            map.SetTile(0, 0, 1, Tile.Space);
            map.SetTile(0, 0, 0, Tile.Floor());
            var c = At(0.5f, 0.5f, 2);
            Assert.True(FallTick(c, map)); Assert.Equal(1, c.Z);  // z2 → z1
            Assert.True(FallTick(c, map)); Assert.Equal(0, c.Z);  // z1 → z0
            Assert.False(FallTick(c, map)); Assert.Equal(0, c.Z); // пол z0 → стоп
        }
    }
}
