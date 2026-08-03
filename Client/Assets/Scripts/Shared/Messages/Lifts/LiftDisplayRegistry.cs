using System.Collections.Generic;

namespace Shared.Messages.Lifts
{
    public interface ILiftDoorDisplay
    {
        bool IsAlive { get; }

        void Apply(in LiftDoorDisplayState state);
    }

    public sealed class LiftDisplayRegistry
    {
        private readonly Dictionary<long, List<ILiftDoorDisplay>> _byCell
            = new Dictionary<long, List<ILiftDoorDisplay>>();
        private readonly Dictionary<long, LiftDoorDisplayState> _last
            = new Dictionary<long, LiftDoorDisplayState>();

        public void Register(long cellKey, ILiftDoorDisplay display)
        {
            if (display == null)
                return;
            if (!_byCell.TryGetValue(cellKey, out var list))
            {
                list = new List<ILiftDoorDisplay>(1);
                _byCell[cellKey] = list;
            }
            if (!list.Contains(display))
                list.Add(display);
            if (_last.TryGetValue(cellKey, out var state))
                display.Apply(in state);
        }

        public void Unregister(long cellKey, ILiftDoorDisplay display)
        {
            if (display == null || !_byCell.TryGetValue(cellKey, out var list))
                return;
            list.Remove(display);
            if (list.Count == 0)
                _byCell.Remove(cellKey);
        }

        public void Push(long cellKey, in LiftDoorDisplayState state)
        {
            bool changed = !_last.TryGetValue(cellKey, out var previous) || !previous.Equals(state);
            _last[cellKey] = state;

            if (!_byCell.TryGetValue(cellKey, out var list))
                return;

            for (int i = list.Count - 1; i >= 0; i--)
            {
                var display = list[i];
                if (display == null || !display.IsAlive)
                {
                    list.RemoveAt(i);
                    continue;
                }
                if (changed)
                    display.Apply(in state);
            }

            if (list.Count == 0)
                _byCell.Remove(cellKey);
        }

        public int DisplayCount(long cellKey) => _byCell.TryGetValue(cellKey, out var list) ? list.Count : 0;

        public bool TryGetLast(long cellKey, out LiftDoorDisplayState state) => _last.TryGetValue(cellKey, out state);
    }
}
