using UnityEngine;

namespace Client.UI.Windows
{
    /// <summary>Подгоняет свой RectTransform под размер цели + отступ (окно под сетку слотов).</summary>
    public sealed class RectSizeMatcher : MonoBehaviour
    {
        [SerializeField] private RectTransform _target;
        [Tooltip("добавляется к ширине/высоте цели; для полей с каждой стороны задай 2×отступ")]
        [SerializeField] private Vector2 _padding;
        [Tooltip("следить за целью каждый кадр; выкл — только при включении")]
        [SerializeField] private bool _continuous = true;

        private RectTransform _self;

        private void Awake() => _self = GetComponent<RectTransform>();

        /// <summary>Пересчитывает свой размер от текущего размера цели.</summary>
        public void Apply()
        {
            if (_target == null || _self == null) return;
            _self.sizeDelta = _target.rect.size + _padding;
        }

        private void OnEnable() => Apply();

        private void LateUpdate()
        {
            if (_continuous) Apply();
        }
    }
}
