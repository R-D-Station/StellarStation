namespace Server.Lifts
{
    public static class LiftStopDedup
    {
        public static int Apply(LiftShaftSpec shaft, LiftScanResult result)
        {
            if (shaft == null || shaft.Stops.Count < 2)
                return 0;

            int dropped = 0;
            for (int i = shaft.Stops.Count - 1; i > 0; i--)
            {
                var stop = shaft.Stops[i];
                if (stop.FloorIndex != shaft.Stops[i - 1].FloorIndex)
                    continue;
                result?.Add(LiftScanIssueKind.DuplicateFloorDoor, stop.DoorAnchor, stop.FloorIndex);
                shaft.Stops.RemoveAt(i);
                dropped++;
            }
            return dropped;
        }
    }
}
