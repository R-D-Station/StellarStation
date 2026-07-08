using Shared.Configs;
using Shared.Messages.Interaction;
using Shared.World;
using Server.Network;

namespace ServerTests.Server.Network
{
    /// <summary>ProcessInteractions/ResolveAndDispatchInteraction: адресный клик по лестнице С СОСЕДНЕЙ клетки
    /// (range-check + StairHandler читает Special целевого тайла). Вне дальности / нет Special / нет сущности — тихий дроп.</summary>
    public class ProcessInteractionsTests
    {
        private static SVars Config() => new SVars
        {
            Ip = "127.0.0.1",
            Port = 0,
            MaxPlayers = 4,
            TickRate = 30,
            ConnectionKey = "t"
        };

        private static ClientConnection Client(float x, float y, int z)
            => new ClientConnection(null!, 1) { X = x, Y = y, Z = z };

        private static InteractIntent TileClick(int tx, int ty, int tz) => new InteractIntent
        {
            TargetKind = (byte)InteractTargetKind.Tile,
            Verb = (byte)InteractVerb.Primary,
            HandIndex = 0,
            TileX = tx,
            TileY = ty,
            TileZ = tz,
            TargetNetId = -1
        };

        [Fact]
        public void AdjacentStairClick_InReach_ChangesZ_LandsOnWalkablePair()
        {
            var map = new GridMap();
            map.SetTile(5, 5, 0, new Tile { Special = TileSpecial.StairUp, StructureType = 1, Support = true }); // низ: ladder-структура
            map.SetTile(5, 5, 1, Tile.Floor()); // площадка сверху walkable → высадка на парный тайл
            var server = new GameServer(Config(), map);

            var client = Client(6.5f, 5.5f, 0); // E-сосед лестницы (chebyshev=1) на этаже 0
            server.ResolveAndDispatchInteraction(client, TileClick(5, 5, 0));

            Assert.Equal(1, client.Z);
            Assert.Equal(5.5f, client.X);
            Assert.Equal(5.5f, client.Y);
        }

        [Fact]
        public void AdjacentStairClick_BlockedPair_LandsOnWalkableNeighbor()
        {
            var map = new GridMap();
            map.SetTile(5, 5, 0, new Tile { Special = TileSpecial.StairUp, StructureType = 1, Support = true });
            map.SetTile(5, 5, 1, new Tile { Special = TileSpecial.StairDown, StructureType = 1, Support = true }); // верх непроходим
            map.SetTile(5, 6, 1, Tile.Floor()); // N-сосед целевого этажа walkable
            var server = new GameServer(Config(), map);

            var client = Client(4.5f, 5.5f, 0); // W-сосед лестницы (chebyshev=1)
            server.ResolveAndDispatchInteraction(client, TileClick(5, 5, 0));

            Assert.Equal(1, client.Z);
            Assert.Equal(5.5f, client.X); // nx=5
            Assert.Equal(6.5f, client.Y); // ny=6 (N-высадка)
        }

        [Fact]
        public void StairClick_OutOfReach_NoChange()
        {
            var map = new GridMap();
            map.SetTile(5, 5, 0, new Tile { Special = TileSpecial.StairUp });
            map.SetTile(5, 5, 1, Tile.Floor());
            var server = new GameServer(Config(), map);

            var client = Client(0.5f, 0.5f, 0); // далеко (chebyshev=5)
            server.ResolveAndDispatchInteraction(client, TileClick(5, 5, 0));

            Assert.Equal(0, client.Z);
            Assert.Equal(0.5f, client.X);
            Assert.Equal(0.5f, client.Y);
        }

        [Fact]
        public void TileClick_NoSpecial_SilentDrop()
        {
            var map = new GridMap();
            map.SetTile(5, 6, 0, Tile.Floor()); // сосед — обычный пол, без Special
            var server = new GameServer(Config(), map);

            var client = Client(5.5f, 5.5f, 0);
            server.ResolveAndDispatchInteraction(client, TileClick(5, 6, 0));

            Assert.Equal(0, client.Z);
            Assert.Equal(5.5f, client.X);
            Assert.Equal(5.5f, client.Y);
        }

        [Fact]
        public void EntityClick_UnknownNetId_SilentDrop()
        {
            var server = new GameServer(Config(), new GridMap());
            var client = Client(5.5f, 5.5f, 0);
            var intent = new InteractIntent
            {
                TargetKind = (byte)InteractTargetKind.Entity,
                Verb = (byte)InteractVerb.Primary,
                HandIndex = 0,
                TileX = 5,
                TileY = 5,
                TileZ = 0,
                TargetNetId = 999 // нет такой сущности (сервер без клиентов)
            };

            var ex = Record.Exception(() => server.ResolveAndDispatchInteraction(client, intent));

            Assert.Null(ex);
            Assert.Equal(0, client.Z);
            Assert.Equal(5.5f, client.X);
            Assert.Equal(5.5f, client.Y);
        }
    }
}
