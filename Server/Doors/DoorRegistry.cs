using System;
using System.Collections.Generic;
using Shared.Simulation.Blocks;
using Shared.World.Blocks;

namespace Server.Doors
{
    public sealed class DoorRegistry
    {
        private readonly Dictionary<long, DoorRecord> _doors = new();

        public int Count => _doors.Count;

        public Dictionary<long, DoorRecord>.ValueCollection Records => _doors.Values;

        public bool TryGet(long key, out DoorRecord rec) => _doors.TryGetValue(key, out rec!);

        public DoorScanStats Build(BlockGrid world, bool debug = false)
        {
            _doors.Clear();
            var stats = new DoorScanStats();
            if (world == null)
                return stats;

            foreach (var kv in world.Sections)
            {
                BlockGrid.UnpackKey(kv.Key, out int cx, out int cy, out int cz);
                var section = kv.Value;
                for (int ly = 0; ly < ChunkSection.Size; ly++)
                    for (int lz = 0; lz < ChunkSection.Size; lz++)
                        for (int lx = 0; lx < ChunkSection.Size; lx++)
                        {
                            ushort type = section.GetBlock(ChunkSection.LocalIndex(lx, ly, lz));
                            if (type == 0)
                                continue;
                            var info = BlockCatalog.Get(type);
                            if (!info.Openable)
                                continue;
                            int wx = cx * 16 + lx, wy = cy * 16 + ly, wz = cz * 16 + lz;
                            if (world.TryGetStructOffset(wx, wy, wz, out _, out _, out _)) { stats.SkippedNotAnchor++; continue; }
                            stats.Openable++;
                            byte st = world.GetState(wx, wy, wz);
                            bool auto = info.Opening == DoorOpening.Auto;
                            if (auto && info.Triggers.Length == 0) { stats.SkippedNoTrigger++; continue; }
                            if (info.Opening == DoorOpening.Interact) stats.Interact++;
                            else if (info.Opening == DoorOpening.External) stats.External++;

                            bool open = BlockState.GetOpen(st);
                            _doors[BlockGrid.PackCell(wx, wy, wz)] = new DoorRecord
                            {
                                Ax = wx, Ay = wy, Az = wz, Type = type, Open = open,
                                CloseAtTick = open ? uint.MaxValue : 0u
                            };
                            if (debug && info.Triggers.Length > 0)
                            {
                                int facing = BlockState.GetFacing(st);
                                AutoDoorLogic.TriggerWorldBounds(wx, wy, wz, info.Triggers[0],
                                    info.SizeX, info.SizeZ, facing,
                                    out float x0, out float y0, out float z0, out float x1, out float y1, out float z1);
                                Console.WriteLine($"[Doors] анкор ({wx},{wy},{wz}) '{info.Name}' facing={facing} " +
                                    $"size={info.SizeX}x{info.SizeY}x{info.SizeZ} триггеров={info.Triggers.Length} open={open}; " +
                                    $"триггер[0] мир X[{x0:0.##}..{x1:0.##}] Y[{y0:0.##}..{y1:0.##}] Z[{z0:0.##}..{z1:0.##}]");
                            }
                            else if (debug)
                            {
                                Console.WriteLine($"[Doors] анкор ({wx},{wy},{wz}) '{info.Name}' режим={info.Opening} " +
                                    $"facing={BlockState.GetFacing(st)} " +
                                    $"size={info.SizeX}x{info.SizeY}x{info.SizeZ} без триггеров open={open}");
                            }
                        }
            }

            stats.Registered = _doors.Count;
            return stats;
        }

        // Рецепт DoorHandler: якорь структуры, иначе САМА клетка — без фолбэка одноклеточные двери не резолвятся.
        public bool TryResolveAnchorKey(BlockGrid world, int x, int y, int z, out long key)
        {
            int ax = x, ay = y, az = z;
            if (world != null && !world.AnchorOf(x, y, z, out ax, out ay, out az))
            {
                ax = x; ay = y; az = z;
            }
            key = BlockGrid.PackCell(ax, ay, az);
            return _doors.ContainsKey(key);
        }
    }
}
