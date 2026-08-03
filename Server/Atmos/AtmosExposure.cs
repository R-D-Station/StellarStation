using System;
using System.Collections.Generic;
using LiteNetLib;
using Server.Network;
using Shared.Configs;
using Shared.Messages.Atmos;
using Shared.Simulation;
using Shared.World.Atmos;
using Shared.World.Blocks;

namespace Server.Atmos
{
    public sealed class AtmosExposure
    {
        private const int RecoveryPerTick = 2;
        private const float SyncDeltaKpa = 0.5f;

        private readonly SVars _config;

        public AtmosExposure(SVars config) => _config = config;

        public void Process(Dictionary<NetPeer, ClientConnection> clients, BlockGrid grid, AtmosGrid atmos, GameServer server)
        {
            int unconsciousAt = _config.AtmosUnconsciousSec * _config.TickRate;
            int deathAt = _config.AtmosDeathSec * _config.TickRate;

            foreach (var client in clients.Values)
            {
                if (client.State == PlayerState.Dead)
                    continue;

                int cx = (int)MathF.Floor(client.Mover.X);
                int cz = (int)MathF.Floor(client.Mover.Z);
                int cy = AtmosBlocks.AirCellY(grid, cx, (int)MathF.Floor(client.Mover.Y), cz);

                var mix = atmos.GetMix(cx, cy, cz);

                if (mix.OxygenKpa >= _config.AtmosMinO2Kpa)
                    Recover(client);
                else
                    Suffocate(client, unconsciousAt, deathAt);

                SyncSensor(client, in mix, server);
            }
        }

        private static void Recover(ClientConnection client)
        {
            if (client.ExposureTicks <= 0)
                return;

            client.ExposureTicks -= RecoveryPerTick;
            if (client.ExposureTicks < 0)
                client.ExposureTicks = 0;

            if (client.ExposureTicks == 0 && client.State == PlayerState.Unconscious)
            {
                client.State = PlayerState.Stand;
                client.DisableMovement = false;
            }
        }

        private static void Suffocate(ClientConnection client, int unconsciousAt, int deathAt)
        {
            int before = client.ExposureTicks;
            client.ExposureTicks = before + 1;

            if (client.ExposureTicks >= deathAt)
                client.Kill();
            else if (before < unconsciousAt && client.ExposureTicks >= unconsciousAt
                     && client.State != PlayerState.Unconscious)
                client.SetUnconscious();
        }

        private void SyncSensor(ClientConnection client, in GasMix mix, GameServer server)
        {
            float pressure = mix.PressureKpa;
            float oxygen = mix.OxygenKpa;

            bool first = float.IsNaN(client.LastSentPressureKpa);
            bool due = client.AtmosSyncCountdown <= 0;
            bool changed = !first
                && (MathF.Abs(pressure - client.LastSentPressureKpa) > SyncDeltaKpa
                    || MathF.Abs(oxygen - client.LastSentOxygenKpa) > SyncDeltaKpa);

            if (!first && !due && !changed)
            {
                client.AtmosSyncCountdown--;
                return;
            }

            client.LastSentPressureKpa = pressure;
            client.LastSentOxygenKpa = oxygen;
            client.AtmosSyncCountdown = _config.AtmosSyncIntervalTicks - 1;
            server.SendToClient(client, new AtmosSync { PressureKpa = pressure, OxygenKpa = oxygen });
        }
    }
}
