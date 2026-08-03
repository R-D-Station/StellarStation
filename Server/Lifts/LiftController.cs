using System;
using Server.Doors;
using Shared.Simulation.Blocks;

namespace Server.Lifts
{
    public sealed class LiftController
    {
        public const int MaxOpenAttempts = 3;

        private readonly float[] _stopY;
        private readonly long[] _stopDoor;
        private readonly bool[] _served;
        private readonly IDoorCommands _doors;
        private readonly uint _dwellTicks;
        private readonly uint _doorLeadTicks;
        private readonly uint _retryTicks;
        private readonly float _blocksPerTick;

        private LiftPlan _plan;
        private uint _calls;
        private sbyte _dir;
        private int _floor;
        private int _target;
        private int _openAttempts;
        private uint _lastAttemptTick;
        private bool _attempted;
        private bool _dirty;

        public int LiftId { get; }
        public LiftRuntime Runtime { get; }
        public int CurrentFloor => _floor;
        public LiftPlan Plan => _plan;
        public int StopCount => _stopY.Length;
        public int SkippedFloors { get; private set; }
        public int LastSkippedFloor { get; private set; } = -1;

        public LiftController(int liftId, LiftRuntime runtime, float[] stopY, long[] stopDoor, bool[] served,
            uint dwellTicks, uint doorLeadTicks, uint retryTicks, float blocksPerTick, IDoorCommands doors)
        {
            LiftId = liftId;
            Runtime = runtime;
            _stopY = stopY ?? Array.Empty<float>();
            _stopDoor = stopDoor ?? Array.Empty<long>();
            _served = served ?? Array.Empty<bool>();
            // Окно Dwell — ровно [Arrival, Arrival + dwellTicks). При dwellTicks == 0 оно ПУСТО: дверь не
            // командуется, бит заказа не гаснет никогда, и Ready каждый тик продлевает стоянку, взводя _dirty —
            // кабина висит вечно и шлёт LiftSync каждый тик по ReliableOrdered. DwellSec — контентное поле,
            // малое значение автор поставит легко, поэтому гард здесь, а не в вызывающем.
            _dwellTicks = dwellTicks < 1u ? 1u : dwellTicks;
            _doorLeadTicks = doorLeadTicks;
            _retryTicks = retryTicks < 1u ? 1u : retryTicks;
            _blocksPerTick = blocksPerTick;
            _doors = doors;

            _plan.Segment = runtime.Segment;
            _plan.DwellUntilTick = LiftPhase.DwellUntil(in _plan.Segment, _dwellTicks, _doorLeadTicks);
            _dir = 1;
            _floor = NearestFloor(runtime.Segment.FromY);
            _target = _floor;
        }

        private int NearestFloor(float y)
        {
            if (_stopY.Length == 0)
                return 0;
            int best = 0;
            float bestDistance = Math.Abs(_stopY[0] - y);
            for (int k = 1; k < _stopY.Length; k++)
            {
                float d = Math.Abs(_stopY[k] - y);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = k;
                }
            }
            return best;
        }

        public bool IsServed(int floor) => (uint)floor < (uint)_served.Length && _served[floor];

        public float FloorY(int floor) => (uint)floor < (uint)_stopY.Length ? _stopY[floor] : 0f;

        public bool Request(int floor)
        {
            if (!IsServed(floor) || floor >= LiftScanQueue.MaxFloors)
                return false;
            uint before = _calls;
            _calls = LiftScanQueue.Add(_calls, floor);
            if (_calls != before)
                _dirty = true;
            return true;
        }

        public bool HasCall(int floor) => LiftScanQueue.Has(_calls, floor);

        public LiftPhaseKind PhaseAt(uint tick) => LiftPhase.At(in _plan, tick, _doorLeadTicks);

        public uint Calls => _calls;

        public uint DoorLeadTicks => _doorLeadTicks;

        // true = состояние уехало → нужен LiftSync. Триггер НЕ «сменился сегмент»: нажатие этажа, на котором
        // кабина уже стоит, меняет только DwellUntilTick/Calls, и по сегменту такой пакет не ушёл бы.
        public bool Decide(uint tick)
        {
            if (_stopY.Length == 0)
                return false;

            switch (LiftPhase.At(in _plan, tick, _doorLeadTicks))
            {
                case LiftPhaseKind.Travel:
                    Travel(tick);
                    break;
                case LiftPhaseKind.Dwell:
                    Arrive();
                    Dwell(tick);
                    break;
                case LiftPhaseKind.Closing:
                    Arrive();
                    Closing();
                    break;
                default:
                    Arrive();
                    Ready(tick);
                    break;
            }

            bool dirty = _dirty;
            _dirty = false;
            return dirty;
        }

        private void Arrive()
        {
            if (_floor != _target)
            {
                _floor = _target;
                _openAttempts = 0;
                _attempted = false;
            }
        }

        private bool Travel(uint tick)
        {
            int en = LiftScanQueue.NextEnRoute(_calls, Runtime.YAt(tick), _stopY, _dir, _target, _blocksPerTick);
            if (en < 0 || en == _target)
                return false;

            _target = en;
            Runtime.Shorten(_stopY[en]);
            _dirty = true;
            _plan.Segment = Runtime.Segment;
            _plan.DwellUntilTick = LiftPhase.DwellUntil(in _plan.Segment, _dwellTicks, _doorLeadTicks);
            _openAttempts = 0;
            _attempted = false;
            return true;
        }

        private void Dwell(uint tick)
        {
            if (!LiftScanQueue.Has(_calls, _floor))
                return;

            long key = _stopDoor[_floor];
            if (key == 0L)
            {
                _calls = LiftScanQueue.Clear(_calls, _floor);
                _dirty = true;
                return;
            }
            if (_doors.IsOpen(key))
            {
                _calls = LiftScanQueue.Clear(_calls, _floor);
                _dirty = true;
                _openAttempts = 0;
                return;
            }
            if (_openAttempts >= MaxOpenAttempts)
                return;
            if (_attempted && tick - _lastAttemptTick < _retryTicks)
                return;

            _lastAttemptTick = tick;
            _attempted = true;
            _openAttempts++;
            var result = _doors.TrySetOpen(key, true);
            if (result == DoorCommandResult.Applied || result == DoorCommandResult.AlreadyInState)
            {
                _calls = LiftScanQueue.Clear(_calls, _floor);
                _dirty = true;
                _openAttempts = 0;
                return;
            }
            if (_openAttempts >= MaxOpenAttempts)
            {
                SkippedFloors++;
                LastSkippedFloor = _floor;
                _calls = LiftScanQueue.Clear(_calls, _floor);
                _dirty = true;
            }
        }

        private void Closing()
        {
            long key = _stopDoor[_floor];
            if (key != 0L && _doors.IsOpen(key))
                _doors.TrySetOpen(key, false);
        }

        private bool Ready(uint tick)
        {
            long key = _stopDoor[_floor];
            if (key != 0L && _doors.IsOpen(key))
            {
                _doors.TrySetOpen(key, false);
                return false;
            }

            int next = LiftScanQueue.Next(_calls, _floor, _dir, _stopY.Length);
            if (next < 0)
                return false;

            if (next == _floor)
            {
                _plan.DwellUntilTick = tick + _dwellTicks + _doorLeadTicks;
                _dirty = true;
                _openAttempts = 0;
                _attempted = false;
                return false;
            }

            _dir = (sbyte)(next > _floor ? 1 : -1);
            _target = next;
            Runtime.Retarget(_stopY[next], tick);
            _dirty = true;
            _plan.Segment = Runtime.Segment;
            _plan.DwellUntilTick = LiftPhase.DwellUntil(in _plan.Segment, _dwellTicks, _doorLeadTicks);
            _openAttempts = 0;
            _attempted = false;
            return true;
        }
    }
}
