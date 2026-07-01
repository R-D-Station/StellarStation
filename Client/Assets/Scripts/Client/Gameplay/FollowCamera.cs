using UnityEngine;

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

        private void LateUpdate()
        {
            if (_target == null) return;

            Vector3 desiredPosition = _target.position + _offset;
            Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, _smoothSpeed * Time.deltaTime);
            transform.position = smoothedPosition;
        }

        public void SetTarget(Transform target)
        {
            _target = target;
        }
    }
}