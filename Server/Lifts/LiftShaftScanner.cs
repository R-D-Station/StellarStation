using System;
using System.Collections.Generic;
using Server.Doors;
using Shared.Simulation.Blocks;
using Shared.World.Blocks;

namespace Server.Lifts
{
    public static class LiftShaftScanner
    {
        public const int MaxFloors = 32;

        private readonly struct Anchor
        {
            public readonly int X, Y, Z;
            public readonly ushort Type;
            public readonly int Facing;

            public Anchor(int x, int y, int z, ushort type, int facing)
            {
                X = x; Y = y; Z = z; Type = type; Facing = facing;
            }

            public LiftCell Cell => new LiftCell(X, Y, Z);
        }

        private sealed class RailStack
        {
            public int X, Z, Facing, BaseY, ChainTopY, Step, Count, Seen;
            public int ModuleY;
            public ushort Type;
            public bool Invalid;
            public int X0, X1, Z0, Z1;
            public bool HasCabin;

            public int AcceptedTopY => BaseY + (Count - 1) * Step;
            public int VolumeTopY => AcceptedTopY + Math.Max(1, ModuleY) - 1;
        }

        public static LiftScanResult Scan(BlockGrid world, DoorRegistry doors)
        {
            var result = new LiftScanResult();
            if (world == null)
                return result;

            var types = new HashSet<ushort>();
            foreach (var info in BlockCatalog.All)
                if (info.Lift != null || (info.Openable && info.Opening == DoorOpening.External))
                    types.Add(info.Id);
            if (types.Count == 0)
                return result;

            var rails = new List<Anchor>();
            var cabins = new List<Anchor>();
            var shaftDoors = new List<Anchor>();
            BlockScan.ForEach(world, types, (x, y, z, type, state) =>
            {
                if (world.TryGetStructOffset(x, y, z, out _, out _, out _))
                    return;
                var info = BlockCatalog.Get(type);
                var anchor = new Anchor(x, y, z, type, BlockState.GetFacing(state));
                if (info.Lift == null)
                    shaftDoors.Add(anchor);
                else if (info.Lift.Kind == LiftPartKind.Rail)
                    rails.Add(anchor);
                else
                    cabins.Add(anchor);
            });

            result.RailModules = rails.Count;
            result.Cabins = cabins.Count;

            var stacks = BuildStacks(rails, result);
            MarkOverlaps(stacks, result);
            var shafts = BindCabins(stacks, cabins, result);
            BindDoors(world, doors, shafts, shaftDoors, result);
            Finish(shafts, result);
            return result;
        }

        private static List<RailStack> BuildStacks(List<Anchor> rails, LiftScanResult result)
        {
            var columns = new Dictionary<long, List<Anchor>>();
            var order = new List<long>();
            foreach (var rail in rails)
            {
                long key = BlockGrid.PackCell(rail.X, 0, rail.Z);
                if (!columns.TryGetValue(key, out var list))
                {
                    list = new List<Anchor>();
                    columns[key] = list;
                    order.Add(key);
                }
                list.Add(rail);
            }

            var stacks = new List<RailStack>();
            foreach (long key in order)
            {
                var list = columns[key];
                list.Sort((a, b) => a.Y.CompareTo(b.Y));

                RailStack current = null;
                foreach (var rail in list)
                {
                    if (current != null)
                    {
                        int dy = rail.Y - current.ChainTopY;
                        if (dy != current.Step)
                        {
                            result.Add(LiftScanIssueKind.RailStackGap, rail.Cell, dy, current.Step);
                            current = null;
                        }
                        else if (rail.Type != current.Type || rail.Facing != current.Facing)
                        {
                            result.Add(LiftScanIssueKind.RailModuleMismatch, rail.Cell, current.Type, rail.Type);
                            current.Invalid = true;
                            current.ChainTopY = rail.Y;
                            current.Seen++;
                            current.Count++;
                            continue;
                        }
                    }

                    if (current == null)
                    {
                        current = NewStack(rail);
                        stacks.Add(current);
                        continue;
                    }

                    current.ChainTopY = rail.Y;
                    current.Seen++;
                    if (current.Count < MaxFloors)
                        current.Count++;
                }
            }

            foreach (var stack in stacks)
            {
                if (stack.Seen > MaxFloors)
                    result.Add(LiftScanIssueKind.TooManyFloors,
                        new LiftCell(stack.X, stack.BaseY, stack.Z), stack.Seen, MaxFloors);
            }
            return stacks;
        }

        private static RailStack NewStack(in Anchor rail)
        {
            var lift = BlockCatalog.Get(rail.Type).Lift;
            var stack = new RailStack
            {
                X = rail.X, Z = rail.Z, Facing = rail.Facing, Type = rail.Type,
                BaseY = rail.Y, ChainTopY = rail.Y, Count = 1, Seen = 1,
                Step = Math.Max(1, (int)lift.FloorStep),
                ModuleY = Math.Max(1, (int)lift.ModuleY)
            };
            PlanRect(rail.X, rail.Z, lift.ModuleX, lift.ModuleZ, rail.Facing,
                out stack.X0, out stack.X1, out stack.Z0, out stack.Z1);
            return stack;
        }

        private static void PlanRect(int ax, int az, int moduleX, int moduleZ, int facing,
            out int x0, out int x1, out int z0, out int z1)
            => LiftCabinPlacement.PlanRect(ax, az, moduleX, moduleZ, facing, out x0, out x1, out z0, out z1);

        private static void MarkOverlaps(List<RailStack> stacks, LiftScanResult result)
        {
            for (int i = 0; i < stacks.Count; i++)
                for (int j = i + 1; j < stacks.Count; j++)
                {
                    var a = stacks[i];
                    var b = stacks[j];
                    if (a.X1 < b.X0 || b.X1 < a.X0 || a.Z1 < b.Z0 || b.Z1 < a.Z0)
                        continue;
                    if (a.VolumeTopY < b.BaseY || b.VolumeTopY < a.BaseY)
                        continue;
                    result.Add(LiftScanIssueKind.ShaftFootprintOverlap,
                        new LiftCell(a.X, a.BaseY, a.Z), b.X, b.Z);
                    a.Invalid = true;
                    b.Invalid = true;
                }
        }

        private static List<LiftShaftSpec> BindCabins(List<RailStack> stacks, List<Anchor> cabins,
            LiftScanResult result)
        {
            var byStack = new Dictionary<RailStack, LiftShaftSpec>();
            var planMatches = new List<RailStack>();

            foreach (var cabin in cabins)
            {
                planMatches.Clear();
                foreach (var stack in stacks)
                    if (cabin.X >= stack.X0 && cabin.X <= stack.X1
                        && cabin.Z >= stack.Z0 && cabin.Z <= stack.Z1)
                        planMatches.Add(stack);

                if (planMatches.Count == 0)
                {
                    result.Add(LiftScanIssueKind.CabinWithoutRail, cabin.Cell);
                    continue;
                }

                RailStack owner = null, nearest = null;
                int bestDistance = int.MaxValue;
                foreach (var stack in planMatches)
                {
                    if (stack.Invalid)
                        continue;
                    int distance = DistanceToVolume(stack, cabin.Y);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        nearest = stack;
                    }
                    int d = cabin.Y - stack.BaseY;
                    if (owner == null && d >= 0 && d % stack.Step == 0 && d / stack.Step < stack.Count)
                        owner = stack;
                }

                var mismatched = owner ?? nearest;
                if (mismatched != null && cabin.Facing != mismatched.Facing)
                {
                    result.Add(LiftScanIssueKind.CabinFacingMismatch, cabin.Cell,
                        mismatched.Facing, cabin.Facing);
                    mismatched.Invalid = true;
                    byStack.Remove(mismatched);
                    continue;
                }

                if (nearest == null)
                {
                    result.Add(LiftScanIssueKind.CabinInBrokenShaft, cabin.Cell,
                        planMatches[0].X, planMatches[0].Z);
                    continue;
                }
                if (owner == null)
                {
                    result.Add(LiftScanIssueKind.CabinOffFloor, cabin.Cell, nearest.BaseY, nearest.Step);
                    nearest.Invalid = true;
                    byStack.Remove(nearest);
                    continue;
                }
                if (owner.HasCabin)
                {
                    result.Add(LiftScanIssueKind.DuplicateCabin, cabin.Cell, owner.X, owner.Z);
                    owner.Invalid = true;
                    byStack.Remove(owner);
                    continue;
                }

                var lift = BlockCatalog.Get(cabin.Type).Lift;
                if (lift.Boxes.Length == 0)
                {
                    result.Add(LiftScanIssueKind.CabinBoxesEmpty, cabin.Cell);
                    owner.Invalid = true;
                    continue;
                }
                if (!(lift.SpeedBlocksPerSec > 0f))
                {
                    result.Add(LiftScanIssueKind.CabinBadSpeed, cabin.Cell);
                    owner.Invalid = true;
                    continue;
                }

                var railLift = BlockCatalog.Get(owner.Type).Lift;
                // План строится по модулю РЕЛЬСА, а боксы приходят от КАБИНЫ: разные модули = визуал и коллизия
                // разъедутся молча. Ловим при загрузке карты, а не рантайм-чеком у игрока.
                if (lift.ModuleX != railLift.ModuleX || lift.ModuleZ != railLift.ModuleZ)
                {
                    result.Add(LiftScanIssueKind.CabinModuleMismatch, cabin.Cell,
                        lift.ModuleX * 256 + lift.ModuleZ, railLift.ModuleX * 256 + railLift.ModuleZ);
                    owner.Invalid = true;
                    continue;
                }

                owner.HasCabin = true;
                byStack[owner] = new LiftShaftSpec
                {
                    RailX = owner.X, RailZ = owner.Z, Facing = owner.Facing,
                    BaseY = owner.BaseY, FloorStep = owner.Step, FloorCount = owner.Count,
                    ModuleX = railLift.ModuleX, ModuleY = railLift.ModuleY, ModuleZ = railLift.ModuleZ,
                    RailType = owner.Type, CabinType = cabin.Type, CabinY = cabin.Y,
                    AnchorX = owner.X0,
                    AnchorZ = owner.Z0,
                    SpeedBlocksPerSec = lift.SpeedBlocksPerSec,
                    DwellSec = lift.DwellSec,
                    DoorLeadSec = lift.DoorLeadSec,
                    CabinBoxes = lift.Boxes,
                    PlanX0 = owner.X0, PlanX1 = owner.X1, PlanZ0 = owner.Z0, PlanZ1 = owner.Z1
                };
            }

            foreach (var stack in stacks)
                if (!stack.Invalid && !stack.HasCabin)
                    result.Add(LiftScanIssueKind.ShaftWithoutCabin, new LiftCell(stack.X, stack.BaseY, stack.Z));

            var alive = new List<LiftShaftSpec>();
            foreach (var pair in byStack)
                if (!pair.Key.Invalid)
                    alive.Add(pair.Value);
            return alive;
        }

        private static int DistanceToVolume(RailStack stack, int y)
        {
            if (y < stack.BaseY)
                return stack.BaseY - y;
            return y > stack.VolumeTopY ? y - stack.VolumeTopY : 0;
        }

        private static void BindDoors(BlockGrid world, DoorRegistry doors, List<LiftShaftSpec> shafts,
            List<Anchor> shaftDoors, LiftScanResult result)
        {
            if (shaftDoors.Count == 0)
                return;

            var footprint = new List<(int x, int y, int z)>();
            foreach (var door in shaftDoors)
            {
                var info = BlockCatalog.Get(door.Type);
                footprint.Clear();
                int parts = MultiBlock.PartCount(info.SizeX, info.SizeY, info.SizeZ);
                for (int p = 0; p < parts; p++)
                {
                    MultiBlock.PartWorldOffset(p, info.SizeX, info.SizeZ, door.Facing,
                        out int dx, out int dy, out int dz);
                    footprint.Add((door.X + dx, door.Y + dy, door.Z + dz));
                }

                LiftShaftSpec planMatch = null, owner = null;
                int ownerFloor = -1;
                bool claimedTwice = false;
                foreach (var shaft in shafts)
                {
                    if (!TouchesPlan(shaft, footprint))
                        continue;
                    if (planMatch == null)
                        planMatch = shaft;
                    int floor = FloorOf(shaft, footprint);
                    if (floor < 0)
                        continue;
                    if (owner != null)
                    {
                        claimedTwice = true;
                        break;
                    }
                    owner = shaft;
                    ownerFloor = floor;
                }

                if (claimedTwice)
                {
                    result.Add(LiftScanIssueKind.DoorClaimedByTwoShafts, door.Cell);
                    continue;
                }
                if (planMatch == null)
                {
                    result.Add(LiftScanIssueKind.OrphanShaftDoor, door.Cell);
                    continue;
                }
                if (owner == null)
                {
                    result.Add(LiftScanIssueKind.DoorOffFloor, door.Cell, planMatch.BaseY, planMatch.FloorStep);
                    continue;
                }

                var stop = new LiftStopSpec
                {
                    FloorIndex = ownerFloor,
                    FloorY = owner.FloorYAt(ownerFloor),
                    DoorAnchor = door.Cell
                };
                if (doors != null && doors.TryResolveAnchorKey(world, door.X, door.Y, door.Z, out long key))
                {
                    stop.DoorKey = key;
                    stop.HasDoor = true;
                    if (doors.TryGet(key, out var record) && record.Open)
                        result.Add(LiftScanIssueKind.DoorSavedOpen, door.Cell);
                }
                else
                {
                    result.Add(LiftScanIssueKind.DoorNotRegistered, door.Cell);
                }
                owner.Stops.Add(stop);
            }
        }

        private static bool TouchesPlan(LiftShaftSpec shaft, List<(int x, int y, int z)> footprint)
        {
            int x0 = shaft.PlanX0, x1 = shaft.PlanX1, z0 = shaft.PlanZ0, z1 = shaft.PlanZ1;
            foreach (var cell in footprint)
            {
                if (InRect(cell.x - 1, cell.z, x0, x1, z0, z1) || InRect(cell.x + 1, cell.z, x0, x1, z0, z1)
                    || InRect(cell.x, cell.z - 1, x0, x1, z0, z1) || InRect(cell.x, cell.z + 1, x0, x1, z0, z1))
                    return true;
            }
            return false;
        }

        private static bool InRect(int x, int z, int x0, int x1, int z0, int z1)
            => x >= x0 && x <= x1 && z >= z0 && z <= z1;

        private static int FloorOf(LiftShaftSpec shaft, List<(int x, int y, int z)> footprint)
        {
            for (int k = 0; k < shaft.FloorCount; k++)
            {
                int y0 = shaft.FloorYAt(k);
                int y1 = y0 + Math.Max(1, (int)shaft.ModuleY) - 1;
                foreach (var cell in footprint)
                    if (cell.y >= y0 && cell.y <= y1)
                        return k;
            }
            return -1;
        }

        private static void Finish(List<LiftShaftSpec> shafts, LiftScanResult result)
        {
            shafts.Sort(CompareShafts);
            for (int i = 0; i < shafts.Count; i++)
            {
                var shaft = shafts[i];
                shaft.LiftId = i + 1;
                shaft.Stops.Sort(CompareStops);
                LiftStopDedup.Apply(shaft, result);
                if (shaft.Stops.Count == 0)
                    result.Add(LiftScanIssueKind.ShaftWithoutStops, new LiftCell(shaft.RailX, shaft.BaseY, shaft.RailZ));
                result.Stops += shaft.Stops.Count;
                result.Shafts.Add(shaft);
            }
        }

        private static int CompareShafts(LiftShaftSpec a, LiftShaftSpec b)
        {
            int c = a.BaseY.CompareTo(b.BaseY);
            if (c != 0)
                return c;
            c = a.RailX.CompareTo(b.RailX);
            return c != 0 ? c : a.RailZ.CompareTo(b.RailZ);
        }

        private static int CompareStops(LiftStopSpec a, LiftStopSpec b)
        {
            int c = a.FloorIndex.CompareTo(b.FloorIndex);
            if (c != 0)
                return c;
            c = a.DoorAnchor.X.CompareTo(b.DoorAnchor.X);
            if (c != 0)
                return c;
            return a.DoorAnchor.Z.CompareTo(b.DoorAnchor.Z);
        }
    }
}
