using System;
using System.Collections.Generic;
using System.Threading;
using LiteNetLib;
using Server.Atmos;
using Server.Network;
using Shared.Configs;
using Shared.Messages.Atmos;
using Shared.Simulation;
using Shared.Simulation.Blocks;
using Shared.World.Atmos;
using Shared.World.Blocks;

namespace ServerTests.Server.AtmosSystems
{
    /// <summary>A3: удушье/смерть/пробуждение по кислороду клетки + owner-only отправка AtmosSync.</summary>
    public class AtmosExposureTests : IDisposable
    {
        private const ushort Wall = 3;

        private readonly SVars _config;
        private readonly NetManager _sender;
        private readonly NetManager _receiver;
        private readonly NetPeer _peer;
        private readonly List<AtmosSync> _received = new List<AtmosSync>();
        private global::Server.Network.GameServer? _server;

        public AtmosExposureTests()
        {
            _config = new SVars
            {
                Ip = "127.0.0.1",
                Port = 0,
                MaxPlayers = 10,
                TickRate = 30,
                MapPath = "",
                ConnectionKey = "ExposureTest",
                AtmosMinO2Kpa = 16f,
                AtmosUnconsciousSec = 1,
                AtmosDeathSec = 2,
                AtmosSyncIntervalTicks = 5
            };

            var receiverListener = new EventBasedNetListener();
            receiverListener.ConnectionRequestEvent += r => r.AcceptIfKey(_config.ConnectionKey);
            receiverListener.NetworkReceiveEvent += (peer, reader, channel, method) =>
            {
                if (reader.GetUShort() == (ushort)global::Shared.Messages.MessageType.AtmosSync)
                {
                    var sync = new AtmosSync();
                    sync.Deserialize(reader.GetBytesWithLength());
                    _received.Add(sync);
                }
                reader.Recycle();
            };
            _receiver = new NetManager(receiverListener);
            _receiver.Start(0);
            int port = _receiver.LocalPort;

            var senderListener = new EventBasedNetListener();
            _sender = new NetManager(senderListener);
            _sender.Start();

            NetPeer? connected = null;
            senderListener.PeerConnectedEvent += p => connected = p;
            _sender.Connect("127.0.0.1", port, _config.ConnectionKey);
            TestWait.Until(() => connected != null,
                () => { _sender.PollEvents(); _receiver.PollEvents(); }, what: $"sender-receiver paired on port {port}");
            _peer = connected!;
        }

        public void Dispose()
        {
            _server?.Stop();
            _sender.Stop();
            _receiver.Stop();
        }

        private global::Server.Network.GameServer Server()
            => _server ??= new global::Server.Network.GameServer(_config);

        private void Pump(int iterations = 30)
        {
            for (int i = 0; i < iterations; i++)
            {
                _sender.PollEvents();
                _receiver.PollEvents();
                Thread.Sleep(5);
            }
        }

        private static readonly BlockGrid OpenAir = new BlockGrid();

        private static AtmosGrid Vacuum()
        {
            var atmos = new AtmosGrid();
            atmos.MarkSpace(2, 1, 2);
            return atmos;
        }

        private static AtmosGrid Breathable()
        {
            var atmos = new AtmosGrid();
            atmos.SetMix(2, 1, 2, GasMix.Standard);
            return atmos;
        }

        private static AtmosGrid WithMix(GasMix mix)
        {
            var atmos = new AtmosGrid();
            atmos.SetMix(2, 1, 2, mix);
            return atmos;
        }

        private (Dictionary<NetPeer, ClientConnection> clients, ClientConnection client) Player()
        {
            var client = new ClientConnection(_peer, 1)
            {
                Mover = new BlockMoverState(2.5f, 1f, 2.5f)
            };
            var clients = new Dictionary<NetPeer, ClientConnection> { [_peer] = client };
            return (clients, client);
        }

        private static void Run(AtmosExposure exposure, Dictionary<NetPeer, ClientConnection> clients,
            AtmosGrid atmos, global::Server.Network.GameServer server, int ticks)
            => Run(exposure, clients, OpenAir, atmos, server, ticks);

        private static void Run(AtmosExposure exposure, Dictionary<NetPeer, ClientConnection> clients,
            BlockGrid grid, AtmosGrid atmos, global::Server.Network.GameServer server, int ticks)
        {
            for (int i = 0; i < ticks; i++) exposure.Process(clients, grid, atmos, server);
        }

        [Fact]
        public void Vacuum_Timeline_Unconscious_ThenDead()
        {
            var (clients, client) = Player();
            var atmos = Vacuum();
            var exposure = new AtmosExposure(_config);
            int unconsciousAt = _config.AtmosUnconsciousSec * _config.TickRate;
            int deathAt = _config.AtmosDeathSec * _config.TickRate;

            Run(exposure, clients, atmos, Server(), unconsciousAt - 1);
            Assert.Equal(PlayerState.Stand, client.State);
            Assert.Equal(unconsciousAt - 1, client.ExposureTicks);

            exposure.Process(clients, OpenAir, atmos, Server());
            Assert.Equal(PlayerState.Unconscious, client.State);
            Assert.True(client.DisableMovement);

            Run(exposure, clients, atmos, Server(), deathAt - unconsciousAt - 1);
            Assert.Equal(PlayerState.Unconscious, client.State);

            exposure.Process(clients, OpenAir, atmos, Server());
            Assert.Equal(PlayerState.Dead, client.State);
            Assert.True(client.IgnoreCollision);
        }

        [Fact]
        public void MovingToBreathable_DrainsCounter_NoUnconscious()
        {
            var (clients, client) = Player();
            var vacuum = Vacuum();
            var air = Breathable();
            var exposure = new AtmosExposure(_config);
            int unconsciousAt = _config.AtmosUnconsciousSec * _config.TickRate;

            Run(exposure, clients, vacuum, Server(), unconsciousAt - 5);
            Assert.Equal(PlayerState.Stand, client.State);
            int peak = client.ExposureTicks;

            Run(exposure, clients, air, Server(), 3);
            Assert.True(client.ExposureTicks < peak, $"счётчик должен таять: {client.ExposureTicks} из {peak}");

            Run(exposure, clients, air, Server(), peak);
            Assert.Equal(0, client.ExposureTicks);
            Assert.Equal(PlayerState.Stand, client.State);
            Assert.False(client.DisableMovement);
        }

        [Fact]
        public void Unconscious_WakesUp_WhenCounterReachesZero()
        {
            var (clients, client) = Player();
            var vacuum = Vacuum();
            var air = Breathable();
            var exposure = new AtmosExposure(_config);
            int unconsciousAt = _config.AtmosUnconsciousSec * _config.TickRate;

            Run(exposure, clients, vacuum, Server(), unconsciousAt);
            Assert.Equal(PlayerState.Unconscious, client.State);
            Assert.True(client.DisableMovement);

            exposure.Process(clients, OpenAir, air, Server());
            Assert.Equal(PlayerState.Unconscious, client.State);
            Assert.True(client.DisableMovement);

            Run(exposure, clients, air, Server(), unconsciousAt);
            Assert.Equal(0, client.ExposureTicks);
            Assert.Equal(PlayerState.Stand, client.State);
            Assert.False(client.DisableMovement);
        }

        [Fact]
        public void StandingOnHalfHeightFloor_BreathesCellAbove()
        {
            const ushort SlapFloor = 2;

            var grid = new BlockGrid();
            for (int x = 0; x <= 4; x++)
                for (int y = 0; y <= 3; y++)
                    for (int z = 0; z <= 4; z++)
                    {
                        bool interior = (y == 1 || y == 2) && x >= 1 && x <= 3 && z >= 1 && z <= 3;
                        if (!interior) grid.SetBlock(x, y, z, Wall);
                    }
            grid.SetBlock(2, 1, 2, SlapFloor);

            var atmos = new AtmosGrid();
            AtmosInit.Classify(grid, atmos);
            Assert.True(atmos.GetMix(2, 1, 2).IsVacuum(), "клетка самой плиты герметична — в ней вакуум");
            Assert.False(atmos.GetMix(2, 2, 2).IsVacuum(), "клетка над плитой — воздух комнаты");

            var (clients, client) = Player();
            client.Mover = new BlockMoverState(2.5f, 1.5f, 2.5f);
            var exposure = new AtmosExposure(_config);

            Run(exposure, clients, grid, atmos, Server(), 100);

            Assert.Equal(0, client.ExposureTicks);
            Assert.Equal(PlayerState.Stand, client.State);
        }

        [Fact]
        public void OxygenJustBelowThreshold_Suffocates()
        {
            var (clients, client) = Player();
            var atmos = WithMix(new GasMix(0.15f, 0.85f));
            var exposure = new AtmosExposure(_config);

            Assert.True(atmos.GetMix(2, 1, 2).OxygenKpa < _config.AtmosMinO2Kpa);

            Run(exposure, clients, atmos, Server(), _config.AtmosUnconsciousSec * _config.TickRate);
            Assert.Equal(PlayerState.Unconscious, client.State);
        }

        [Fact]
        public void OxygenJustAboveThreshold_IsBreathable()
        {
            var (clients, client) = Player();
            var atmos = WithMix(new GasMix(0.17f, 0.83f));
            var exposure = new AtmosExposure(_config);

            Assert.True(atmos.GetMix(2, 1, 2).OxygenKpa > _config.AtmosMinO2Kpa);

            Run(exposure, clients, atmos, Server(), 100);
            Assert.Equal(0, client.ExposureTicks);
            Assert.Equal(PlayerState.Stand, client.State);
        }

        [Fact]
        public void PureNitrogen_AtFullPressure_Suffocates()
        {
            var (clients, client) = Player();
            var atmos = new AtmosGrid();
            atmos.SetMix(2, 1, 2, new GasMix(0f, AtmosConstants.StandardTotalMoles));
            var exposure = new AtmosExposure(_config);

            Assert.Equal(AtmosConstants.StandardPressureKpa, atmos.GetMix(2, 1, 2).PressureKpa, 2);

            Run(exposure, clients, atmos, Server(), _config.AtmosUnconsciousSec * _config.TickRate);
            Assert.Equal(PlayerState.Unconscious, client.State);
        }

        [Fact]
        public void ThinButOxygenRichAir_IsBreathable()
        {
            var (clients, client) = Player();
            var atmos = new AtmosGrid();
            atmos.SetMix(2, 1, 2, new GasMix(AtmosConstants.StandardTotalMoles * 0.25f, 0f));
            var exposure = new AtmosExposure(_config);

            Run(exposure, clients, atmos, Server(), 100);
            Assert.Equal(0, client.ExposureTicks);
            Assert.Equal(PlayerState.Stand, client.State);
        }

        [Fact]
        public void ForeignUnconscious_IsNotWokenUp_WhenNeverSuffocated()
        {
            var (clients, client) = Player();
            var air = Breathable();
            var exposure = new AtmosExposure(_config);

            client.SetUnconscious();
            Run(exposure, clients, air, Server(), 100);

            Assert.Equal(PlayerState.Unconscious, client.State);
            Assert.True(client.DisableMovement);
            Assert.Equal(0, client.ExposureTicks);
        }

        [Fact]
        public void Dead_IsFinal_NeverRevives()
        {
            var (clients, client) = Player();
            var vacuum = Vacuum();
            var air = Breathable();
            var exposure = new AtmosExposure(_config);

            Run(exposure, clients, vacuum, Server(), _config.AtmosDeathSec * _config.TickRate);
            Assert.Equal(PlayerState.Dead, client.State);

            int ticksAtDeath = client.ExposureTicks;
            Run(exposure, clients, air, Server(), 200);
            Assert.Equal(PlayerState.Dead, client.State);
            Assert.True(client.DisableMovement);
            Assert.Equal(ticksAtDeath, client.ExposureTicks);
        }

        [Fact]
        public void Sensor_SendsFirst_ThenSilentUntilInterval()
        {
            var (clients, client) = Player();
            var air = Breathable();
            var exposure = new AtmosExposure(_config);

            exposure.Process(clients, OpenAir, air, Server());
            Pump();
            Assert.Single(_received);
            Assert.Equal(AtmosConstants.StandardPressureKpa, _received[0].PressureKpa, 2);
            Assert.True(_received[0].OxygenKpa > _config.AtmosMinO2Kpa);

            Run(exposure, clients, air, Server(), _config.AtmosSyncIntervalTicks - 1);
            Pump();
            Assert.Single(_received);

            exposure.Process(clients, OpenAir, air, Server());
            Pump();
            Assert.Equal(2, _received.Count);
        }

        [Fact]
        public void Sensor_SendsImmediately_OnPressureChange()
        {
            var (clients, client) = Player();
            var air = Breathable();
            var exposure = new AtmosExposure(_config);

            exposure.Process(clients, OpenAir, air, Server());
            Pump();
            Assert.Single(_received);

            air.SetMix(2, 1, 2, new GasMix(GasMix.Standard.O2Moles * 0.5f, GasMix.Standard.N2Moles * 0.5f));
            exposure.Process(clients, OpenAir, air, Server());
            Pump();
            Assert.Equal(2, _received.Count);
            Assert.True(_received[1].PressureKpa < _received[0].PressureKpa);
        }

        [Fact]
        public void Sensor_StaysSilent_OnSubThresholdChange()
        {
            var (clients, client) = Player();
            var air = Breathable();
            var exposure = new AtmosExposure(_config);

            exposure.Process(clients, OpenAir, air, Server());
            Pump();
            Assert.Single(_received);

            var nudged = GasMix.Standard;
            nudged.N2Moles += 0.001f;
            air.SetMix(2, 1, 2, nudged);
            exposure.Process(clients, OpenAir, air, Server());
            Pump();
            Assert.Single(_received);
        }

        [Fact]
        public void SealedRoom_IsBreathable_NoExposure()
        {
            var grid = new BlockGrid();
            for (int x = 0; x <= 4; x++)
                for (int y = 0; y <= 2; y++)
                    for (int z = 0; z <= 4; z++)
                    {
                        bool interior = y == 1 && x >= 1 && x <= 3 && z >= 1 && z <= 3;
                        if (!interior) grid.SetBlock(x, y, z, Wall);
                    }

            var atmos = new AtmosGrid();
            AtmosInit.Classify(grid, atmos);

            var (clients, client) = Player();
            var exposure = new AtmosExposure(_config);
            Run(exposure, clients, atmos, Server(), 100);

            Assert.Equal(0, client.ExposureTicks);
            Assert.Equal(PlayerState.Stand, client.State);
            Assert.False(client.DisableMovement);
        }
    }
}
