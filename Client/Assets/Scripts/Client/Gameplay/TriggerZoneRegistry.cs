using System.Collections.Generic;
using UnityEngine;
using Client.Map;

namespace Client.Gameplay
{
    public static class TriggerZoneRegistry
    {
        private static readonly List<TriggerZone> _zones = new List<TriggerZone>();

        public static int Count => _zones.Count;

        public static void Register(TriggerZone zone)
        {
            if (zone == null || _zones.Contains(zone))
                return;
            _zones.Add(zone);
        }

        public static void Unregister(TriggerZone zone)
        {
            if (zone != null)
                _zones.Remove(zone);
        }

        public static void Clear() => _zones.Clear();

        public static void Evaluate(Vector3 worldPos)
        {
            for (int i = 0; i < _zones.Count; i++)
            {
                var z = _zones[i];
                if (z != null)
                    z.Evaluate(worldPos);
            }
        }
    }
}
