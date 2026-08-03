using System;

namespace Shared.World.Blocks
{
    public enum LiftPartKind : byte
    {
        Rail = 0,
        Cabin = 1
    }

    public sealed class LiftPartInfo
    {
        public readonly LiftPartKind Kind;
        public readonly byte ModuleX, ModuleY, ModuleZ;
        public readonly byte FloorStep;
        public readonly float SpeedBlocksPerSec;
        public readonly float DwellSec;
        public readonly float DoorLeadSec;
        public readonly TriggerBox[] Boxes;

        public LiftPartInfo(LiftPartKind kind,
            byte moduleX = 1, byte moduleY = 1, byte moduleZ = 1, byte floorStep = 1,
            float speedBlocksPerSec = 0f, float dwellSec = 0f, float doorLeadSec = 0f,
            TriggerBox[] boxes = null)
        {
            Kind = kind;
            ModuleX = moduleX;
            ModuleY = moduleY;
            ModuleZ = moduleZ;
            FloorStep = floorStep;
            SpeedBlocksPerSec = speedBlocksPerSec;
            DwellSec = dwellSec;
            DoorLeadSec = doorLeadSec;
            Boxes = boxes ?? Array.Empty<TriggerBox>();
        }
    }
}
