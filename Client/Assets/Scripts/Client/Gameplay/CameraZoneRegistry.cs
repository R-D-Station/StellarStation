using System.Collections.Generic;
using UnityEngine;
using Client.Map;

namespace Client.Gameplay.Camera
{
    /// <summary>Реестр живых <see cref="CameraZone"/>: регистрация при спавне блока, детерминированный выбор активной.</summary>
    public static class CameraZoneRegistry
    {
        private static readonly List<CameraZone> _zones = new List<CameraZone>();
        private static int _nextOrder;

        public static int Count => _zones.Count;

        public static void Register(CameraZone zone)
        {
            if (zone == null || _zones.Contains(zone))
                return;
            zone.Order = _nextOrder++;
            _zones.Add(zone);
        }

        public static void Unregister(CameraZone zone)
        {
            if (zone != null)
                _zones.Remove(zone);
        }

        public static void Clear()
        {
            _zones.Clear();
            _nextOrder = 0;
        }

        public static bool IsBetter(CameraZone a, CameraZone b)
            => CameraMath.IsBetter(a.Priority, a.Volume, a.Order, b.Priority, b.Volume, b.Order);

        /// <summary>Активная зона под точкой; null — вне зон. Перебор без аллокаций (зон в кадре единицы).</summary>
        public static CameraZone Resolve(Vector3 worldPos, float margin = 0f)
        {
            CameraZone best = null;
            for (int i = 0; i < _zones.Count; i++)
            {
                var z = _zones[i];
                if (z == null)
                    continue;
                if (!z.Contains(worldPos, margin))
                    continue;
                if (best == null || IsBetter(z, best))
                    best = z;
            }
            return best;
        }
    }
}
