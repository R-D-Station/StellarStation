using System.Collections.Generic;
using Shared.Messages.Lifts;

namespace Server.Lifts
{
    public static class LiftRegistryBuilder
    {
        public static LiftRegistry Build(IReadOnlyList<LiftRuntime> lifts,
            IReadOnlyList<LiftController> controllers = null,
            IReadOnlyList<LiftShaftSpec> shafts = null)
        {
            var entries = new LiftRegistryEntry[lifts.Count];
            for (int i = 0; i < lifts.Count; i++)
            {
                var lift = lifts[i];
                var src = lift.CabinBoxes;
                var boxes = new LiftCabinBox[src.Count];
                for (int b = 0; b < src.Count; b++)
                {
                    var box = src[b];
                    boxes[b] = new LiftCabinBox(box.CenterX, box.Y, box.CenterZ, box.HalfX, box.HalfZ, box.Height);
                }

                var controller = FindById(controllers, lift.LiftId);
                var shaft = ShaftById(shafts, lift.LiftId);
                entries[i] = new LiftRegistryEntry
                {
                    LiftId = lift.LiftId,
                    AnchorX = lift.AnchorX,
                    AnchorZ = lift.AnchorZ,
                    DoorLeadTicks = controller?.DoorLeadTicks ?? 0u,
                    CabinDefId = shaft != null ? shaft.CabinType : (ushort)0,
                    PlanW = shaft != null ? Span(shaft.PlanX0, shaft.PlanX1) : (byte)0,
                    PlanD = shaft != null ? Span(shaft.PlanZ0, shaft.PlanZ1) : (byte)0,
                    Facing = shaft != null ? (byte)(shaft.Facing & 3) : (byte)0,
                    RailDefId = shaft != null ? shaft.RailType : (ushort)0,
                    BaseY = shaft != null ? shaft.BaseY : 0,
                    FloorStep = shaft != null ? Clamp8(shaft.FloorStep) : (byte)0,
                    FloorCount = shaft != null ? Clamp8(shaft.FloorCount) : (byte)0,
                    Boxes = boxes,
                    Stops = StopsOf(controller, shaft)
                };
            }
            return new LiftRegistry { Lifts = entries };
        }

        private static byte Clamp8(int v) => v < 0 ? (byte)0 : (v > byte.MaxValue ? byte.MaxValue : (byte)v);

        private static byte Span(int from, int to)
        {
            int n = to - from + 1;
            if (n < 0)
                n = 0;
            return n > byte.MaxValue ? byte.MaxValue : (byte)n;
        }

        private static LiftShaftSpec ShaftById(IReadOnlyList<LiftShaftSpec> shafts, int liftId)
        {
            if (shafts == null)
                return null;
            for (int i = 0; i < shafts.Count; i++)
                if (shafts[i].LiftId == liftId)
                    return shafts[i];
            return null;
        }

        private static LiftController FindById(IReadOnlyList<LiftController> controllers, int liftId)
        {
            if (controllers == null)
                return null;
            for (int i = 0; i < controllers.Count; i++)
                if (controllers[i].LiftId == liftId)
                    return controllers[i];
            return null;
        }

        private static LiftStopEntry[] StopsOf(LiftController controller, LiftShaftSpec shaft)
        {
            if (controller == null)
                return System.Array.Empty<LiftStopEntry>();

            int served = 0;
            for (int k = 0; k < controller.StopCount; k++)
                if (controller.IsServed(k))
                    served++;
            if (served == 0)
                return System.Array.Empty<LiftStopEntry>();

            var stops = new LiftStopEntry[served];
            int n = 0;
            for (int k = 0; k < controller.StopCount; k++)
            {
                if (!controller.IsServed(k))
                    continue;
                stops[n++] = TryFindDoor(shaft, k, out var cell)
                    ? new LiftStopEntry((byte)k, controller.FloorY(k), cell.X, cell.Y, cell.Z)
                    : LiftStopEntry.WithoutDoor((byte)k, controller.FloorY(k));
            }
            return stops;
        }

        // Клетка двери берётся из скана (LiftStopSpec.DoorAnchor), а НЕ выводится из плана шахты:
        // сторона двери из геометрии не восстанавливается, а догадка о ней уже стреляла на этом треке.
        private static bool TryFindDoor(LiftShaftSpec shaft, int floor, out LiftCell cell)
        {
            cell = default;
            if (shaft == null)
                return false;
            foreach (var stop in shaft.Stops)
                if (stop.FloorIndex == floor && stop.HasDoor)
                {
                    cell = stop.DoorAnchor;
                    return true;
                }
            return false;
        }
    }
}
