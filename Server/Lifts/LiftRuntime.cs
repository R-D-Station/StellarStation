using System.Collections.Generic;
using Shared.Simulation.Blocks;

namespace Server.Lifts
{
    public sealed class LiftRuntime
    {
        private readonly float _anchorX;
        private readonly float _anchorZ;
        private readonly LiftBox[] _cabinBoxes;
        private LiftSegment _segment;

        public LiftSegment Segment => _segment;
        public int LiftId { get; }
        public float AnchorX => _anchorX;
        public float AnchorZ => _anchorZ;
        public IReadOnlyList<LiftBox> CabinBoxes => _cabinBoxes;

        public LiftRuntime(float anchorX, float anchorZ, LiftBox[] cabinBoxes, in LiftSegment initial, int liftId = 0)
        {
            _anchorX = anchorX;
            _anchorZ = anchorZ;
            _cabinBoxes = cabinBoxes;
            _segment = initial;
            LiftId = liftId;
        }

        public float YAt(uint tick) => LiftTrajectory.YAt(in _segment, tick);

        public void Tick(uint tick, DynamicObstacleSet targetSet)
        {
            float y = LiftTrajectory.YAt(in _segment, tick);
            float delta = LiftTrajectory.DeltaAt(in _segment, tick);
            for (int i = 0; i < _cabinBoxes.Length; i++)
            {
                ref readonly var b = ref _cabinBoxes[i];
                targetSet.Add(_anchorX + b.CenterX, y + b.Y, _anchorZ + b.CenterZ,
                    b.HalfX, b.HalfZ, b.Height, delta);
            }
        }

        public void Retarget(float toY, uint tick)
        {
            uint anchor = tick == 0 ? 0u : tick - 1;
            _segment = new LiftSegment(LiftTrajectory.YAt(in _segment, anchor), toY, anchor, _segment.BlocksPerTick);
        }

        /// <summary>Укоротить ТЕКУЩИЙ рейс до toY, не трогая прошлое: FromY/StartTick прежние, та же прямая
        /// с более ранним клампом. Для попутной остановки <see cref="Retarget"/> НЕЛЬЗЯ — он переносит якорь
        /// на tick−1 и обнуляет DeltaAt в прошлом, из-за чего реплей клиента отклеивает пассажира.</summary>
        public void Shorten(float toY)
            => _segment = new LiftSegment(_segment.FromY, toY, _segment.StartTick, _segment.BlocksPerTick);
    }
}
