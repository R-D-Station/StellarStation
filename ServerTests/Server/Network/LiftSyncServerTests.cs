using System;
using System.Collections.Generic;
using System.Threading;
using LiteNetLib;
using Shared.Configs;
using Shared.Messages.Lifts;

namespace ServerTests.Server.Network
{
    /// <summary>Сокетный путь LiftSync: пакет при коннекте, тишина у стоящей кабины, ноль пакетов без лифтов
    /// (дедуп на длинной поездке — headless, в LiftSyncDedupTests).</summary>
    public class LiftSyncServerTests : IDisposable
    {
        private int _port;
        private global::Server.Network.GameServer? _server;
        private NetManager? _client;

        public void Dispose()
        {
            _server?.Stop();
            _client?.Stop();
        }

        private global::Server.Network.GameServer StartServer(int pauseTicks)
        {
            var config = new SVars
            {
                Ip = "127.0.0.1",
                Port = 0,
                MaxPlayers = 10,
                TickRate = 30,
                MapPath = "",
                ConnectionKey = "LiftTest",
                DebugLiftEnabled = true,
                DebugLiftX = 5.5f,
                DebugLiftZ = 5.5f,
                DebugLiftFromY = 1f,
                DebugLiftToY = 2f,
                DebugLiftSpeed = 0.5f,
                DebugLiftPauseTicks = pauseTicks
            };
            _server = new global::Server.Network.GameServer(config);
            _server.Start();
            _port = _server.BoundPort;
            return _server;
        }

        private List<LiftSync> StartCollector()
        {
            var received = new List<LiftSync>();
            var listener = new EventBasedNetListener();
            listener.NetworkReceiveEvent += (peer, reader, channel, method) =>
            {
                if (reader.GetUShort() == (ushort)global::Shared.Messages.MessageType.LiftSync)
                {
                    var s = new LiftSync();
                    s.Deserialize(reader.GetBytesWithLength());
                    lock (received) received.Add(s);
                }
                reader.Recycle();
            };
            _client = new NetManager(listener);
            _client.Start();
            _client.Connect("127.0.0.1", _port, "LiftTest");
            return received;
        }

        private void Drain(int ms)
        {
            for (int i = 0; i < ms / 10; i++)
            {
                _client!.PollEvents();
                Thread.Sleep(10);
            }
        }

        private List<LiftSync> ConnectAndCollectFixedWindow(int pollMs)
        {
            var received = StartCollector();
            Drain(pollMs);
            return received;
        }

        private List<LiftSync> ConnectAndCollectUntil(Func<List<LiftSync>, bool> enough,
                                                      Func<List<LiftSync>, string> onTimeout,
                                                      int settleMs = 0, int timeoutMs = 20000)
        {
            var received = StartCollector();
            try
            {
                TestWait.Until(() => { lock (received) { return enough(received); } },
                               () => _client!.PollEvents(), timeoutMs, "LiftSync");
            }
            catch (TimeoutException)
            {
                lock (received) { throw new TimeoutException(onTimeout(received)); }
            }
            Drain(settleMs);
            return received;
        }

        [Fact]
        public void OnConnect_SendsCurrentSegment_Once()
        {
            StartServer(pauseTicks: 100000);
            var received = ConnectAndCollectUntil(
                r => r.Count >= 1,
                r => $"коннект-пакет LiftSync не пришёл: получено {r.Count}",
                settleMs: 500);

            Assert.Single(received);
            Assert.Equal(1, received[0].LiftId);
            Assert.Equal(1f, received[0].FromY);
            Assert.Equal(1f, received[0].ToY);
            Assert.Equal(0.5f, received[0].BlocksPerTick);
        }

        [Fact]
        public void ParkedLift_SendsNothingBeyondTheConnectPacket()
        {
            // Без заказа с панели кабина стоит: сервер обязан молчать, а не слать пакеты «на всякий случай».
            StartServer(pauseTicks: 10);
            var received = ConnectAndCollectUntil(
                r => r.Count >= 1,
                r => $"коннект-пакет LiftSync не пришёл: получено {r.Count}",
                settleMs: 800);

            Assert.Single(received);
        }

        [Fact]
        public void LiftDisabled_NoPackets()
        {
            var config = new SVars
            {
                Ip = "127.0.0.1",
                Port = 0,
                MaxPlayers = 10,
                TickRate = 30,
                MapPath = "",
                ConnectionKey = "LiftTest",
                DebugLiftEnabled = false
            };
            _server = new global::Server.Network.GameServer(config);
            _server.Start();
            _port = _server.BoundPort;

            var received = ConnectAndCollectFixedWindow(600);

            Assert.Empty(received);
            Assert.Empty(_server.Lifts);
        }
    }
}
