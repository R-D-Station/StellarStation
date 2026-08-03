using Shared.Messages.Core;

namespace Client.Gameplay.Camera
{
    public static class CameraMath
    {
        private static readonly IntentDirection[] Ring =
        {
            IntentDirection.North, IntentDirection.NorthEast,
            IntentDirection.East, IntentDirection.SouthEast,
            IntentDirection.South, IntentDirection.SouthWest,
            IntentDirection.West, IntentDirection.NorthWest
        };

        public static IntentDirection RotateIntent(IntentDirection dir, int quadrant)
        {
            int i = RingIndex(dir);
            if (i < 0)
                return dir;
            return Ring[(i + 2 * (quadrant & 3)) & 7];
        }

        private static int RingIndex(IntentDirection dir)
        {
            switch (dir)
            {
                case IntentDirection.North: return 0;
                case IntentDirection.NorthEast: return 1;
                case IntentDirection.East: return 2;
                case IntentDirection.SouthEast: return 3;
                case IntentDirection.South: return 4;
                case IntentDirection.SouthWest: return 5;
                case IntentDirection.West: return 6;
                case IntentDirection.NorthWest: return 7;
                default: return -1;
            }
        }

        public static int NextQuadrant(int quadrant) => (quadrant + 1) & 3;

        public static float YawDegrees(int quadrant) => 90f * (quadrant & 3);

        public static void RotateOffset(float x, float y, float z, int quadrant,
                                        out float rx, out float ry, out float rz)
        {
            ry = y;
            switch (quadrant & 3)
            {
                case 0: rx = x; rz = z; break;
                case 1: rx = z; rz = -x; break;
                case 2: rx = -x; rz = -z; break;
                default: rx = -z; rz = x; break;
            }
        }

        public static bool IsBetter(int priorityA, float volumeA, int orderA,
                                    int priorityB, float volumeB, int orderB)
        {
            if (priorityA != priorityB)
                return priorityA > priorityB;
            if (volumeA != volumeB)
                return volumeA < volumeB;
            return orderA < orderB;
        }
    }
}
