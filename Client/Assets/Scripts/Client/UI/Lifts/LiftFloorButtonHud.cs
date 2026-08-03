using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace Client.UI.Lifts
{
    /// <summary>Одна кнопка этажа в панели кабины: номер, подсветка «вызов принят»/«нажато локально».</summary>
    public sealed class LiftFloorButtonHud : MonoBehaviour
    {
        [SerializeField] private Button _button;
        [SerializeField] private TMP_Text _label;
        [SerializeField] private Image _highlight;
        [SerializeField] private Color _idle = new Color(1f, 1f, 1f, 0.15f);
        [SerializeField] private Color _called = new Color(1f, 0.8f, 0.2f, 0.9f);
        [SerializeField] private Color _pending = new Color(1f, 0.8f, 0.2f, 0.45f);

        public event Action<int> Clicked;

        public int Floor { get; private set; } = -1;

        private void Awake()
        {
            if (_button != null) _button.onClick.AddListener(OnClick);
        }

        private void OnDestroy()
        {
            if (_button != null) _button.onClick.RemoveListener(OnClick);
        }

        private void OnClick() => Clicked?.Invoke(Floor);

        public void Bind(int floor, string caption)
        {
            Floor = floor;
            if (_label != null) _label.text = caption;
        }

        /// <summary>called — эхо сервера (в т.ч. ЧУЖИЕ нажатия); pending — своё нажатие, эхо ещё не пришло.</summary>
        public void SetState(bool called, bool pending)
        {
            if (_highlight == null) return;
            _highlight.color = called ? _called : (pending ? _pending : _idle);
        }
    }
}
