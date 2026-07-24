using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

namespace Client.UI.Windows
{
    /// <summary>Корень префаба плавающего окна — заголовок, крестик закрытия, фокус по клику.</summary>
    public sealed class UiWindow : MonoBehaviour, IPointerDownHandler
    {
        [SerializeField] private TMP_Text _title;
        [SerializeField] private Button _closeButton;

        /// <summary>Запрос закрытия (клик по крестику) — контент/владелец решает, как закрыть.</summary>
        public event Action<UiWindow> CloseRequested;

        private UiWindowManager _manager;

        private void Awake()
        {
            if (_closeButton != null) _closeButton.onClick.AddListener(RaiseClose);
        }

        private void OnDestroy()
        {
            if (_closeButton != null) _closeButton.onClick.RemoveListener(RaiseClose);
        }

        public void Init(UiWindowManager manager) => _manager = manager;

        /// <summary>Задаёт текст заголовка окна.</summary>
        public void SetTitle(string title)
        {
            if (_title != null) _title.text = title;
        }

        public void BringToFront() => _manager?.BringToFront(this);

        public void OnPointerDown(PointerEventData eventData) => BringToFront();

        private void RaiseClose() => CloseRequested?.Invoke(this);
    }
}
