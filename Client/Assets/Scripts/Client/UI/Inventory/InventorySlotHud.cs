using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Shared.Messages.Interaction;
using Shared.World.Items;
using Client.Items;

namespace Client.UI.Inventory
{
    public sealed class InventorySlotHud : MonoBehaviour
    {
        [SerializeField] private SlotKind _kind;
        [SerializeField] private Image _icon;
        [SerializeField] private TMP_Text _count;
        [SerializeField] private Image _highlight;
        [SerializeField] private Button _button;
        [SerializeField] private Sprite _emptySprite;

        public byte Slot => (byte)_kind;
        public event Action<byte> Clicked;

        private void Awake()
        {
            if (_button != null) _button.onClick.AddListener(OnButtonClicked);
        }

        private void OnDestroy()
        {
            if (_button != null) _button.onClick.RemoveListener(OnButtonClicked);
        }

        private void OnButtonClicked() => Clicked?.Invoke(Slot);

        public void SetFilled(in SlotRecord rec, ItemCatalog catalog)
        {
            var def = catalog != null ? catalog.For(rec.ItemDefId) : null;
            Sprite s = def != null ? def.Sprite : null;
            if (_icon != null)
            {
                _icon.sprite = s;
                _icon.enabled = s != null;
            }
            if (_count != null)
            {
                bool show = rec.StackCount > 1;
                _count.gameObject.SetActive(show);
                if (show) _count.text = rec.StackCount.ToString();
            }
        }

        public void SetEmpty()
        {
            if (_icon != null)
            {
                _icon.sprite = _emptySprite;
                _icon.enabled = _emptySprite != null;
            }
            if (_count != null) _count.gameObject.SetActive(false);
        }

        public void SetHighlight(bool on)
        {
            if (_highlight != null) _highlight.enabled = on;
        }
    }
}
