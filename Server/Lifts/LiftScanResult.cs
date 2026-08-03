using System.Collections.Generic;
using Shared.World.Blocks;

namespace Server.Lifts
{
    public enum LiftScanIssueKind : byte
    {
        RailStackGap,
        RailModuleMismatch,
        TooManyFloors,
        ShaftFootprintOverlap,
        CabinFacingMismatch,
        CabinWithoutRail,
        CabinInBrokenShaft,
        DuplicateCabin,
        CabinModuleMismatch,
        CabinOffFloor,
        CabinBoxesEmpty,
        CabinBadSpeed,
        ShaftWithoutCabin,
        DoorOffFloor,
        OrphanShaftDoor,
        DoorNotRegistered,
        DoorSavedOpen,
        DoorClaimedByTwoShafts,
        DuplicateFloorDoor,
        ShaftWithoutStops
    }

    public enum LiftScanSeverity : byte
    {
        Warning = 0,
        Error = 1
    }

    public readonly struct LiftCell
    {
        public readonly int X, Y, Z;

        public LiftCell(int x, int y, int z)
        {
            X = x; Y = y; Z = z;
        }

        public override string ToString() => $"({X},{Y},{Z})";
    }

    public readonly struct LiftScanIssue
    {
        public readonly LiftScanIssueKind Kind;
        public readonly LiftCell Cell;
        public readonly int A, B;

        public LiftScanIssue(LiftScanIssueKind kind, LiftCell cell, int a = 0, int b = 0)
        {
            Kind = kind; Cell = cell; A = a; B = b;
        }

        public LiftScanSeverity Severity => SeverityOf(Kind);

        public static LiftScanSeverity SeverityOf(LiftScanIssueKind kind)
        {
            switch (kind)
            {
                case LiftScanIssueKind.RailModuleMismatch:
                case LiftScanIssueKind.ShaftFootprintOverlap:
                case LiftScanIssueKind.CabinFacingMismatch:
                case LiftScanIssueKind.CabinWithoutRail:
                case LiftScanIssueKind.DuplicateCabin:
                case LiftScanIssueKind.CabinModuleMismatch:
                case LiftScanIssueKind.CabinOffFloor:
                case LiftScanIssueKind.CabinBoxesEmpty:
                case LiftScanIssueKind.CabinBadSpeed:
                case LiftScanIssueKind.DoorClaimedByTwoShafts:
                case LiftScanIssueKind.DuplicateFloorDoor:
                    return LiftScanSeverity.Error;
                default:
                    return LiftScanSeverity.Warning;
            }
        }
    }

    public sealed class LiftStopSpec
    {
        public int FloorIndex;
        public int FloorY;
        public LiftCell DoorAnchor;
        public long DoorKey;
        public bool HasDoor;
    }

    public sealed class LiftShaftSpec
    {
        public int LiftId;
        public int RailX, RailZ;
        public int Facing;
        public int BaseY;
        public int FloorStep;
        public int FloorCount;
        public byte ModuleX, ModuleY, ModuleZ;
        public ushort RailType;
        public ushort CabinType;
        public int CabinY;
        public float AnchorX, AnchorZ;
        public float SpeedBlocksPerSec;
        public float DwellSec;
        public float DoorLeadSec;
        public TriggerBox[] CabinBoxes = System.Array.Empty<TriggerBox>();
        public int PlanX0, PlanX1, PlanZ0, PlanZ1;
        public readonly List<LiftStopSpec> Stops = new();

        public int FloorYAt(int floorIndex) => BaseY + floorIndex * FloorStep;
    }

    public sealed class LiftScanResult
    {
        public readonly List<LiftShaftSpec> Shafts = new();
        public readonly List<LiftScanIssue> Issues = new();
        public int RailModules;
        public int Cabins;
        public int Stops;

        public bool HasErrors
        {
            get
            {
                for (int i = 0; i < Issues.Count; i++)
                    if (Issues[i].Severity == LiftScanSeverity.Error)
                        return true;
                return false;
            }
        }

        public int ErrorCount => CountOf(LiftScanSeverity.Error);
        public int WarningCount => CountOf(LiftScanSeverity.Warning);

        private int CountOf(LiftScanSeverity severity)
        {
            int n = 0;
            for (int i = 0; i < Issues.Count; i++)
                if (Issues[i].Severity == severity)
                    n++;
            return n;
        }

        public void Add(LiftScanIssueKind kind, LiftCell cell, int a = 0, int b = 0)
            => Issues.Add(new LiftScanIssue(kind, cell, a, b));
    }
}
