using System;
using Shared.Simulation.Blocks;

namespace Shared.Messages.Lifts
{
    public readonly struct LiftDoorDisplayState : IEquatable<LiftDoorDisplayState>
    {
        /// <summary>ПОКАЗЫВАЕМЫЙ порядковый номер этажа кабины (0-базовый), а НЕ внутренний индекс рельса.</summary>
        public readonly int CabinLabelIndex;
        public readonly int Direction;
        public readonly bool Called;
        public readonly bool Known;

        public LiftDoorDisplayState(int cabinLabelIndex, int direction, bool called, bool known)
        {
            CabinLabelIndex = cabinLabelIndex;
            Direction = direction;
            Called = called;
            Known = known;
        }

        public static LiftDoorDisplayState ForLift(LiftStopEntry[] stops, float cabinY,
            LiftPhaseKind phase, in LiftSegment segment, bool hasPlan)
        {
            if (!hasPlan || stops == null || stops.Length == 0)
                return default;
            int direction = phase == LiftPhaseKind.Travel ? LiftPhase.Direction(in segment) : 0;
            int label = LiftStopTable.DisplayIndex(stops, LiftStopTable.FloorAtOrBelow(stops, cabinY));
            return new LiftDoorDisplayState(label, direction, false, true);
        }

        public LiftDoorDisplayState WithCall(uint calls, int doorFloor)
            => new LiftDoorDisplayState(CabinLabelIndex, Direction, Known && LiftScanQueue.Has(calls, doorFloor), Known);

        public bool Equals(LiftDoorDisplayState other)
            => CabinLabelIndex == other.CabinLabelIndex && Direction == other.Direction
               && Called == other.Called && Known == other.Known;
    }
}
