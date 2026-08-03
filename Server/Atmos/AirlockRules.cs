using System;
using Shared.Simulation.Blocks;
using Shared.World.Atmos;
using Shared.World.Blocks;

namespace Server.Atmos
{
    public static class AirlockRules
    {
        public static bool BlocksAutoOpen(BlockGrid grid, AtmosGrid atmos, BlockInfo info,
            int ax, int ay, int az, int facing, float maxDeltaKpa)
        {
            if (!info.IsAirlock)
                return false;

            Sides(info, ax, az, facing, out int x0, out int z0, out int x1, out int z1);

            float p0 = atmos.GetMix(x0, AtmosBlocks.AirCellY(grid, x0, ay, z0), z0).PressureKpa;
            float p1 = atmos.GetMix(x1, AtmosBlocks.AirCellY(grid, x1, ay, z1), z1).PressureKpa;
            return MathF.Abs(p0 - p1) > maxDeltaKpa;
        }

        private static void Sides(BlockInfo info, int ax, int az, int facing,
            out int x0, out int z0, out int x1, out int z1)
        {
            if (info.Triggers.Length == 2)
            {
                TriggerCell(info, 0, ax, az, facing, out x0, out z0);
                TriggerCell(info, 1, ax, az, facing, out x1, out z1);
                if (x0 != x1 || z0 != z1)
                    return;
            }

            MultiBlock.RotateLocal(0, -1, facing, out int dx0, out int dz0);
            MultiBlock.RotateLocal(0, 1, facing, out int dx1, out int dz1);
            x0 = ax + dx0; z0 = az + dz0;
            x1 = ax + dx1; z1 = az + dz1;
        }

        private static void TriggerCell(BlockInfo info, int index, int ax, int az, int facing,
            out int cx, out int cz)
        {
            AutoDoorLogic.TriggerWorldBounds(ax, 0, az, in info.Triggers[index], info.SizeX, info.SizeZ, facing,
                out float minX, out _, out float minZ, out float maxX, out _, out float maxZ);
            cx = (int)MathF.Floor((minX + maxX) * 0.5f);
            cz = (int)MathF.Floor((minZ + maxZ) * 0.5f);
        }
    }
}
