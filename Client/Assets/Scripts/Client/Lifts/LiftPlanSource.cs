using Shared.Simulation.Blocks;
using Shared.World.Blocks;

namespace Client.Lifts
{
    public readonly struct LiftShaftPlan
    {
        public readonly bool Found;
        public readonly int X0, Z0, ModuleX, ModuleZ, Facing;

        public LiftShaftPlan(int x0, int z0, int moduleX, int moduleZ, int facing)
        {
            Found = true;
            X0 = x0;
            Z0 = z0;
            ModuleX = moduleX;
            ModuleZ = moduleZ;
            Facing = facing;
        }
    }

    public static class LiftPlanSource
    {
        public static bool TryPlan(LiftPartKind kind, int cellX, int cellZ,
            int ownModuleX, int ownModuleZ, int ownFacing, in LiftShaftPlan shaft,
            out int x0, out int z0, out int moduleX, out int moduleZ, out int facing)
        {
            if (kind == LiftPartKind.Rail)
            {
                moduleX = ownModuleX;
                moduleZ = ownModuleZ;
                facing = ownFacing;
                LiftCabinPlacement.PlanRect(cellX, cellZ, moduleX, moduleZ, facing,
                    out x0, out _, out z0, out _);
            }
            else
            {
                if (!shaft.Found)
                {
                    x0 = z0 = moduleX = moduleZ = facing = 0;
                    return false;
                }
                x0 = shaft.X0;
                z0 = shaft.Z0;
                moduleX = shaft.ModuleX;
                moduleZ = shaft.ModuleZ;
                facing = shaft.Facing;
            }
            return moduleX >= 1 && moduleZ >= 1;
        }

        public static bool TryPose(LiftPartKind kind, int cellX, int cellY, int cellZ,
            int ownModuleX, int ownModuleZ, int ownFacing, in LiftShaftPlan shaft,
            out float x, out float y, out float z, out int facing)
        {
            x = 0f;
            y = 0f;
            z = 0f;
            if (!TryPlan(kind, cellX, cellZ, ownModuleX, ownModuleZ, ownFacing, in shaft,
                    out int x0, out int z0, out int moduleX, out int moduleZ, out facing))
                return false;

            LiftCabinPlacement.PlanSize(moduleX, moduleZ, facing, out int planW, out int planD);
            LiftCabinPlacement.WorldPivot(x0, z0, planW, planD, cellY, out x, out y, out z);
            return true;
        }
    }
}
