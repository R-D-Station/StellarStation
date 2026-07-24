using UnityEngine;
using UnityEngine.EventSystems;

namespace Client.UI.Windows
{
    public sealed class UiWindowDragHandle : MonoBehaviour, IDragHandler, IPointerDownHandler
    {
        [SerializeField] private RectTransform _target;

        private float _canvasScale = 1f;
        private UiWindow _window;

        private void Awake()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null) _canvasScale = canvas.rootCanvas.scaleFactor;
            _window = GetComponentInParent<UiWindow>();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_target != null)
                _target.anchoredPosition += eventData.delta / _canvasScale;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_window != null) _window.BringToFront();
        }
    }
}
