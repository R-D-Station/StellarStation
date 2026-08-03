using UnityEngine;
using Client.Map;

namespace Client.Gameplay.Camera
{
    /// <summary>Правила зон камеры: выбор активной с гистерезисом + плавное сведение офсета/наклона (следование — не здесь).</summary>
    public sealed class CameraZoneDirector
    {
        private CameraZone _current;
        private Vector3 _offset;
        private float _pitch;
        private bool _seeded;

        public CameraZone Current => _current;

        public void Tick(Vector3 followPos, Vector3 baseOffset, float basePitch, float baseBlendSpeed, float dt,
                         out Vector3 offset, out float pitch)
        {
            var best = CameraZoneRegistry.Resolve(followPos);
            var cur = _current;
            if (cur != null && cur != best
                && cur.Contains(followPos, cur.ExitMargin)
                && (best == null || !CameraZoneRegistry.IsBetter(best, cur)))
                best = cur;
            _current = best;

            Vector3 targetOffset = best != null && best.OverrideOffset ? best.Offset : baseOffset;
            float targetPitch = best != null && best.OverridePitch ? best.Pitch : basePitch;
            float speed = best != null ? best.BlendSpeed : baseBlendSpeed;

            if (!_seeded)
            {
                _offset = targetOffset;
                _pitch = targetPitch;
                _seeded = true;
            }
            else
            {
                float t = speed * dt;
                if (t > 1f) t = 1f;
                if (t < 0f) t = 0f;
                _offset = Vector3.Lerp(_offset, targetOffset, t);
                _pitch = Mathf.Lerp(_pitch, targetPitch, t);
            }

            offset = _offset;
            pitch = _pitch;
        }
    }
}
