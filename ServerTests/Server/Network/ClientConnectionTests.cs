using Server.Network;

namespace ServerTests.Server.Network
{
    /// <summary>ClientConnection: NetId отвязан от ConnectionId (ctor не биндит) и реализация IWorldEntity.</summary>
    public class ClientConnectionTests
    {
        [Fact]
        public void Ctor_DoesNotBindNetIdToConnectionId()
        {
            var c = new ClientConnection(null!, 7);
            Assert.Equal(7, c.ConnectionId);
            Assert.Equal(0, c.PlayerNetId); // раньше ctor ставил =connectionId; теперь NetId выдаёт аллокатор в GameServer
        }

        [Fact]
        public void ImplementsIWorldEntity_MapsNetIdAndPosition()
        {
            var c = new ClientConnection(null!, 1) { PlayerNetId = 42, X = 3.5f, Y = 4.5f, Z = 2 };
            IWorldEntity e = c;
            Assert.Equal(42, e.NetId);
            Assert.Equal(3.5f, e.X);
            Assert.Equal(4.5f, e.Y);
            Assert.Equal(2, e.Z);
        }
    }
}
