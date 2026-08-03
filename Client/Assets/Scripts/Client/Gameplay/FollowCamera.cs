using UnityEngine;
using UnityEngine.InputSystem;

namespace Client.Gameplay.Camera
{
    /// <summary>
    /// Плавно следует камерой за целью с заданным смещением.
    /// </summary>
    public class FollowCamera : MonoBehaviour
    {
        [SerializeField] private Transform _target;
        [SerializeField] private float _smoothSpeed = 5f;
        [SerializeField] private Vector3 _offset = new Vector3(0, 0, 10);

        [Tooltip("Клавиша поворота камеры на 90° вокруг персонажа.")]
        [SerializeField] private Key _rotateKey = Key.P;
        [Tooltip("Скорость доворота камеры при смене четверти.")]
        [SerializeField] private float _rotateSpeed = 8f;
        [Tooltip("Скорость возврата к базовым параметрам вне зон камеры.")]
        [SerializeField] private float _zoneBlendSpeed = 4f;

        private readonly CameraQuadrant _quadrant = new CameraQuadrant();
        private float _basePitch;
        private float _baseYaw;
        private readonly CameraZoneDirector _zones = new CameraZoneDirector();

        /// <summary>Единый источник четверти: рысканье камеры и поворот ввода читают её же.</summary>
        public CameraQuadrant Quadrant => _quadrant;

        /// <summary>Цель слежения: она же — точка проверки зон камеры и триггер-объёмов.</summary>
        public Transform Target => _target;

        private void Awake()
        {
            Vector3 e = transform.eulerAngles;
            _basePitch = e.x;
            _baseYaw = e.y;
        }

        private void LateUpdate()
        {
            if (_target == null) return;

            var keyboard = Keyboard.current;
            if (keyboard != null && _rotateKey != Key.None && keyboard[_rotateKey].wasPressedThisFrame)
                _quadrant.Next();

            float dt = Time.deltaTime;
            _zones.Tick(_target.position, _offset, _basePitch, _zoneBlendSpeed, dt,
                        out Vector3 zoneOffset, out float pitch);

            _quadrant.RotateOffset(zoneOffset.x, zoneOffset.y, zoneOffset.z,
                                   out float ox, out float oy, out float oz);

            Vector3 desiredPosition = _target.position + new Vector3(ox, oy, oz);
            Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, _smoothSpeed * dt);
            transform.position = smoothedPosition;

            Quaternion desiredRotation = Quaternion.Euler(pitch, _baseYaw + _quadrant.Yaw, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, _rotateSpeed * dt);
        }

        public void SetTarget(Transform target)
        {
            _target = target;
        }
    }
}
