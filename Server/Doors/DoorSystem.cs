using System;
using System.Collections.Generic;
using LiteNetLib;
using Server.Network;
using Shared.Configs;
using Shared.Simulation.Blocks;
using Shared.World.Atmos;
using Shared.World.Blocks;

namespace Server.Doors
{
    public sealed class DoorSystem : IDoorCommands
    {
        private readonly BlockGrid _world;
        private readonly AtmosGrid _atmos;
        private readonly SVars _config;
        private readonly Dictionary<NetPeer, ClientConnection> _clients;
        private readonly DoorRegistry _registry = new();

        public DoorRegistry Registry => _registry;

        public DoorSystem(BlockGrid world, AtmosGrid atmos, SVars config, Dictionary<NetPeer, ClientConnection> clients)
        {
            _world = world;
            _atmos = atmos;
            _config = config;
            _clients = clients;
        }

        public DoorScanStats Build() => _registry.Build(_world, _config.DebugAutoDoors);

        public bool TryGetAnchorKey(int x, int y, int z, out long key)
            => _registry.TryResolveAnchorKey(_world, x, y, z, out key);

        public bool Exists(long key) => _registry.TryGet(key, out _);

        public bool IsOpen(long key) => _registry.TryGet(key, out var rec) && rec.Open;

        public DoorCommandResult TrySetOpen(long key, bool open, bool force = false)
        {
            if (_world == null || !_registry.TryGet(key, out var door))
                return DoorCommandResult.NoSuchDoor;
            if (door.Open == open)
                return DoorCommandResult.AlreadyInState;

            var info = BlockCatalog.Get(door.Type);
            int facing = BlockState.GetFacing(_world.GetState(door.Ax, door.Ay, door.Az));

            if (!force)
            {
                if (open)
                {
                    if (info.Opening == DoorOpening.Auto
                        && global::Server.Atmos.AirlockRules.BlocksAutoOpen(_world, _atmos, info,
                            door.Ax, door.Ay, door.Az, facing, _config.AirlockMaxDeltaKpa))
                        return DoorCommandResult.BlockedByPressure;
                }
                else if (FootprintOccupied(info, door, facing))
                {
                    return DoorCommandResult.BlockedByOccupant;
                }
            }

            SetOpen(door, info, facing, open);
            door.CloseAtTick = open ? uint.MaxValue : 0u;
            return DoorCommandResult.Applied;
        }

        // Per-tick авторитет авто-дверей: игрок в триггере → Open сразу; вышел → закрыть по DoorCloseDelay.
        public void Tick(uint tick)
        {
            if (_world == null || _registry.Count == 0)
                return;
            float hw = BlockMovementConfig.HalfWidth;
            float hh = BlockMovementConfig.StandHeight;

            bool dbg = _config.DebugAutoDoors && tick % (uint)Math.Max(1, _config.TickRate) == 0;
            if (dbg)
                foreach (var c in _clients.Values)
                    if (c.PlayerNetId != 0)
                        Console.WriteLine($"[Doors] player {c.PlayerNetId} pos ({c.Mover.X:0.##}, {c.Mover.Y:0.##}, {c.Mover.Z:0.##})");

            foreach (var door in _registry.Records)
            {
                var info = BlockCatalog.Get(door.Type);
                if (info.Opening == DoorOpening.External)
                    continue;
                int facing = BlockState.GetFacing(_world.GetState(door.Ax, door.Ay, door.Az));

                bool occupied = false;
                if (info.Opening == DoorOpening.Auto)
                    foreach (var client in _clients.Values)
                    {
                        if (client.PlayerNetId == 0)
                            continue; // не заспавнен
                        if (AutoDoorLogic.PlayerInTrigger(
                                client.Mover.X, client.Mover.Y, client.Mover.Z, hw, hh,
                                door.Ax, door.Ay, door.Az, info.Triggers, info.SizeX, info.SizeZ, facing))
                        {
                            occupied = true;
                            break;
                        }
                    }

                if (dbg && info.Triggers.Length > 0)
                {
                    AutoDoorLogic.TriggerWorldBounds(door.Ax, door.Ay, door.Az, info.Triggers[0],
                        info.SizeX, info.SizeZ, facing,
                        out float x0, out float y0, out float z0, out float x1, out float y1, out float z1);
                    Console.WriteLine($"[Doors] ({door.Ax},{door.Ay},{door.Az}) facing={facing} occupied={occupied} " +
                        $"open={door.Open}; триггер[0] X[{x0:0.##}..{x1:0.##}] Y[{y0:0.##}..{y1:0.##}] Z[{z0:0.##}..{z1:0.##}]");
                }

                uint closeDelay = (uint)Math.Max(1, (int)(info.CloseDelay * _config.TickRate));
                AutoDoorLogic.Tick(occupied, door.Open, door.CloseAtTick, tick,
                    closeDelay, out bool newOpen, out uint newCloseAt);
                door.CloseAtTick = newCloseAt;
                if (newOpen == door.Open)
                    continue;

                if (newOpen)
                {
                    if (info.Opening == DoorOpening.Auto
                        && global::Server.Atmos.AirlockRules.BlocksAutoOpen(_world, _atmos, info,
                            door.Ax, door.Ay, door.Az, facing, _config.AirlockMaxDeltaKpa))
                        continue;
                }
                else if (FootprintOccupied(info, door, facing))
                {
                    door.CloseAtTick = tick + closeDelay;
                    continue;
                }

                SetOpen(door, info, facing, newOpen);
            }
        }

        private bool FootprintOccupied(BlockInfo info, DoorRecord door, int facing)
        {
            float hw = BlockMovementConfig.HalfWidth;
            float hh = BlockMovementConfig.StandHeight;
            int parts = MultiBlock.PartCount(info.SizeX, info.SizeY, info.SizeZ);
            foreach (var client in _clients.Values)
            {
                if (client.PlayerNetId == 0)
                    continue;
                float minX = client.Mover.X - hw, maxX = client.Mover.X + hw;
                float minZ = client.Mover.Z - hw, maxZ = client.Mover.Z + hw;
                float minY = client.Mover.Y, maxY = client.Mover.Y + hh;
                for (int p = 0; p < parts; p++)
                {
                    MultiBlock.PartWorldOffset(p, info.SizeX, info.SizeZ, facing,
                        out int dx, out int dy, out int dz);
                    int cx = door.Ax + dx, cy = door.Ay + dy, cz = door.Az + dz;
                    if (maxX > cx && minX < cx + 1f &&
                        maxZ > cz && minZ < cz + 1f &&
                        maxY > cy && minY < cy + 1f)
                        return true;
                }
            }
            return false;
        }

        // Атомарный тоггл Open всех частей двери (SetState фиксит дельту тика через OnBlockWorldChanged).
        private void SetOpen(DoorRecord door, BlockInfo info, int facing, bool open)
        {
            int parts = MultiBlock.PartCount(info.SizeX, info.SizeY, info.SizeZ);
            for (int p = 0; p < parts; p++)
            {
                MultiBlock.PartWorldOffset(p, info.SizeX, info.SizeZ, facing,
                    out int dx, out int dy, out int dz);
                int px = door.Ax + dx, py = door.Ay + dy, pz = door.Az + dz;
                byte st = _world.GetState(px, py, pz);
                _world.SetState(px, py, pz, BlockState.WithOpen(st, open));
            }
            door.Open = open;
        }
    }
}
