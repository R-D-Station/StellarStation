using System.Collections.Generic;
using Shared.World.Blocks;

namespace Shared.World.Atmos
{
    public sealed class AtmosFlow
    {
        private readonly Queue<long> _active = new Queue<long>();
        private readonly HashSet<long> _queued = new HashSet<long>();
        private readonly bool[] _neighborFlow = new bool[AtmosCell.NeighborCount];

        private float _flowRate = AtmosConstants.DefaultFlowRate;
        private float _epsilon = AtmosConstants.DefaultFlowEpsilon;

        public int ActiveCount => _active.Count;

        public float FlowRate
        {
            get => _flowRate;
            set => _flowRate = value < 0f ? 0f : (value > AtmosConstants.MaxFlowRate ? AtmosConstants.MaxFlowRate : value);
        }

        public float Epsilon
        {
            get => _epsilon;
            set => _epsilon = value < 0f ? 0f : value;
        }

        public void Clear()
        {
            _active.Clear();
            _queued.Clear();
        }

        public void Wake(BlockGrid grid, AtmosGrid atmos, int x, int y, int z)
        {
            if (!IsFlowCell(grid, atmos, x, y, z)) return;

            long packed = AtmosCell.Pack(x, y, z);
            if (_queued.Add(packed))
                _active.Enqueue(packed);
        }

        public void WakeAround(BlockGrid grid, AtmosGrid atmos, int x, int y, int z)
        {
            Wake(grid, atmos, x, y, z);
            for (int n = 0; n < AtmosCell.NeighborCount; n++)
                Wake(grid, atmos, x + AtmosCell.NeighborDx[n], y + AtmosCell.NeighborDy[n], z + AtmosCell.NeighborDz[n]);
        }

        public void WakeAll(BlockGrid grid, AtmosGrid atmos)
        {
            foreach (var kv in grid.Sections)
            {
                BlockGrid.UnpackKey(kv.Key, out int cx, out int cy, out int cz);
                int ox = cx * GasSection.Size, oy = cy * GasSection.Size, oz = cz * GasSection.Size;
                for (int ly = 0; ly < GasSection.Size; ly++)
                    for (int lz = 0; lz < GasSection.Size; lz++)
                        for (int lx = 0; lx < GasSection.Size; lx++)
                            Wake(grid, atmos, ox + lx, oy + ly, oz + lz);
            }
        }

        public int Step(BlockGrid grid, AtmosGrid atmos, int maxCells)
        {
            int budget = _active.Count;
            if (maxCells > 0 && maxCells < budget) budget = maxCells;

            int processed = 0;
            for (int i = 0; i < budget; i++)
            {
                long packed = _active.Dequeue();
                _queued.Remove(packed);
                AtmosCell.Unpack(packed, out int x, out int y, out int z);
                processed++;

                if (!IsFlowCell(grid, atmos, x, y, z)) continue;

                var cell = atmos.GetMix(x, y, z);
                if (cell.TotalMoles <= 0f) continue;

                float moved = 0f;

                for (int n = 0; n < AtmosCell.NeighborCount; n++)
                {
                    _neighborFlow[n] = false;
                    int nx = x + AtmosCell.NeighborDx[n], ny = y + AtmosCell.NeighborDy[n], nz = z + AtmosCell.NeighborDz[n];

                    bool loaded = IsLoaded(grid, nx, ny, nz);
                    if (loaded && AtmosBlocks.IsAirtight(grid, nx, ny, nz)) continue;

                    if (!loaded || atmos.IsSpace(nx, ny, nz))
                    {
                        float o2Out = cell.O2Moles * _flowRate;
                        float n2Out = cell.N2Moles * _flowRate;
                        cell.O2Moles -= o2Out;
                        cell.N2Moles -= n2Out;
                        moved += o2Out + n2Out;
                        continue;
                    }

                    _neighborFlow[n] = true;

                    var other = atmos.GetMix(nx, ny, nz);
                    float o2Diff = (cell.O2Moles - other.O2Moles) * _flowRate;
                    float n2Diff = (cell.N2Moles - other.N2Moles) * _flowRate;
                    if (o2Diff <= 0f) o2Diff = 0f;
                    if (n2Diff <= 0f) n2Diff = 0f;
                    if (o2Diff + n2Diff <= 0f) continue;

                    cell.O2Moles -= o2Diff;
                    cell.N2Moles -= n2Diff;
                    other.O2Moles += o2Diff;
                    other.N2Moles += n2Diff;
                    moved += o2Diff + n2Diff;

                    Clamp(ref other);
                    atmos.SetMix(nx, ny, nz, in other);
                }

                Clamp(ref cell);
                atmos.SetMix(x, y, z, in cell);

                if (moved > _epsilon)
                {
                    Enqueue(AtmosCell.Pack(x, y, z));

                    for (int n = 0; n < AtmosCell.NeighborCount; n++)
                        if (_neighborFlow[n])
                            Enqueue(AtmosCell.Pack(x + AtmosCell.NeighborDx[n], y + AtmosCell.NeighborDy[n], z + AtmosCell.NeighborDz[n]));
                }
            }

            return processed;
        }

        private void Enqueue(long packed)
        {
            if (_queued.Add(packed))
                _active.Enqueue(packed);
        }

        private static void Clamp(ref GasMix mix)
        {
            if (mix.O2Moles < 0f) mix.O2Moles = 0f;
            if (mix.N2Moles < 0f) mix.N2Moles = 0f;
        }

        private static bool IsFlowCell(BlockGrid grid, AtmosGrid atmos, int x, int y, int z)
        {
            if (!IsLoaded(grid, x, y, z)) return false;
            if (AtmosBlocks.IsAirtight(grid, x, y, z)) return false;
            return !atmos.IsSpace(x, y, z);
        }

        private static bool IsLoaded(BlockGrid grid, int x, int y, int z)
            => grid.GetSection(AtmosGrid.SectionCoord(x), AtmosGrid.SectionCoord(y), AtmosGrid.SectionCoord(z)) != null;
    }
}
