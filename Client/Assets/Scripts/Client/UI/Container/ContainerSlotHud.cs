using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Client.Items;

namespace Client.UI.Container
{
    public sealed class ContainerSlotHud : MonoBehaviour
    {
        [SerializeField] private Image _icon;
        [SerializeField] private TMP_Text _count;
        [SerializeField] private Button _button;
        [SerializeField] private int _index;

        public int Index { get => _index; set => _index = value; }
        public event Action<int> Clicked;

        private void Awake()
        {
            if (_button != null) _button.onClick.AddListener(OnButtonClicked);
        }

        private void OnDestroy()
        {
            if (_button != null) _button.onClick.RemoveListener(OnButtonClicked);
        }

        private void OnButtonClicked() => Clicked?.Invoke(_index);

        public void SetFilled(ushort itemDefId, byte stackCount, ItemCatalog catalog)
        {
            var def = catalog != null ? catalog.For(itemDefId) : null;
            Sprite s = def != null ? def.Sprite : null;
            if (_icon != null)
            {
                _icon.sprite = s;
                _icon.enabled = s != null;
            }
            if (_count != null)
            {
                bool show = stackCount > 1;
                _count.gameObject.SetActive(show);
                if (show) _count.text = stackCount.ToString();
            }
        }

        public void SetEmpty()
        {
            if (_icon != null)
            {
                _icon.sprite = null;
                _icon.enabled = false;
            }
            if (_count != null) _count.gameObject.SetActive(false);
        }
    }
}
