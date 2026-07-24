using Shared.Configs;
using Shared.Messages.Interaction;
using Shared.World;
using Server.Network;

namespace ServerTests.Server.Network
{
    /// <summary>ProcessInteractions/ResolveAndDispatchInteraction: range-check адресного клика + тихий дроп
    /// (вне дальности / нет обработчика / нет сущности).</summary>
    public class ProcessInteractionsTests
    {
        private static SVars Config() => new SVars
        {
            Ip = "127.0.0.1",
            Port = 0,
            MaxPlayers = 4,
            TickRate = 30,
            ConnectionKey = "t",
            MapPath = ""
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
        public void StairClick_OutOfReach_NoChange()
        {
            var server = new GameServer(Config());

            var client = Client(0.5f, 0.5f, 0); // далеко (chebyshev=5)
            server.ResolveAndDispatchInteraction(client, TileClick(5, 5, 0));

            Assert.Equal(0, client.Z);
            Assert.Equal(0.5f, client.X);
            Assert.Equal(0.5f, client.Y);
        }

        [Fact]
        public void TileClick_NoSpecial_SilentDrop()
        {
            var server = new GameServer(Config());

            var client = Client(5.5f, 5.5f, 0);
            server.ResolveAndDispatchInteraction(client, TileClick(5, 6, 0));

            Assert.Equal(0, client.Z);
            Assert.Equal(5.5f, client.X);
            Assert.Equal(5.5f, client.Y);
        }

        [Fact]
        public void EntityClick_UnknownNetId_SilentDrop()
        {
            var server = new GameServer(Config());
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
