using UnityEngine;
using Client.Gameplay;
using Client.Gameplay.Camera;

namespace Client.Map
{
    /// <summary>Зона камеры на префабе блока: внутри локального бокса камера берёт свой офсет/наклон.</summary>
    public sealed class CameraZone : MonoBehaviour, IBlockComponent
    {
        [Tooltip("Центр бокса в ЛОКАЛЬНЫХ координатах префаба (поворот блока учитывается сам).")]
        [SerializeField] private Vector3 _boxCenter = Vector3.zero;
        [Tooltip("Размер бокса в локальных координатах.")]
        [SerializeField] private Vector3 _boxSize = new Vector3(3f, 3f, 3f);

        [Tooltip("Переопределять смещение камеры внутри зоны.")]
        [SerializeField] private bool _overrideOffset = true;
        [Tooltip("Смещение камеры при повороте 0 (крутится клавишей поворота вместе с базовым).")]
        [SerializeField] private Vector3 _offset = new Vector3(0f, 0f, -1.5f);

        [Tooltip("Переопределять наклон камеры внутри зоны.")]
        [SerializeField] private bool _overridePitch = true;
        [Tooltip("Наклон (тангаж) в градусах.")]
        [SerializeField] private float _pitch = 75f;

        [Tooltip("Больший приоритет побеждает при перекрытии зон.")]
        [SerializeField] private int _priority;
        [Tooltip("Скорость схождения к параметрам зоны.")]
        [SerializeField] private float _blendSpeed = 4f;
        [Tooltip("Запас на выход: зона держится, пока игрок не отойдёт дальше (антидребезг на границе).")]
        [SerializeField] private float _exitMargin = 0.3f;

        public bool OverrideOffset => _overrideOffset;
        public Vector3 Offset => _offset;
        public bool OverridePitch => _overridePitch;
        public float Pitch => _pitch;
        public int Priority => _priority;
        public float BlendSpeed => _blendSpeed;
        public float ExitMargin => _exitMargin;
        public float Volume => ZoneBox.Volume(_boxSize.x, _boxSize.y, _boxSize.z);

        public int Order { get; set; }

        public bool Contains(Vector3 worldPos, float margin)
        {
            Vector3 p = transform.InverseTransformPoint(worldPos);
            return ZoneBox.ContainsLocal(p.x, p.y, p.z,
                                         _boxCenter.x, _boxCenter.y, _boxCenter.z,
                                         _boxSize.x, _boxSize.y, _boxSize.z, margin);
        }

        public void OnSpawn(in BlockContext ctx) => CameraZoneRegistry.Register(this);

        public void OnDespawn() => CameraZoneRegistry.Unregister(this);

        public void OnVisibility(float alpha) { }

        private void OnDisable() => CameraZoneRegistry.Unregister(this);

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.9f);
            Gizmos.DrawWireCube(_boxCenter, _boxSize);
            if (_exitMargin > 0f)
            {
                Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.25f);
                Gizmos.DrawWireCube(_boxCenter, _boxSize + Vector3.one * (_exitMargin * 2f));
            }
            Gizmos.matrix = Matrix4x4.identity;
        }
#endif
    }
}
