using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Shared.World.Items;
using Client.Items;

namespace Client.UI.Inventory
{
    /// <summary>Самодостаточный UI-слот инвентаря: рендер (иконка/стек/подсветка) + клик по адресу (Category, Index).</summary>
    public sealed class InventorySlotHud : MonoBehaviour
    {
        [SerializeField] private SlotCategory _category;
        [SerializeField] private byte _index;
        [SerializeField] private Image _icon;
        [SerializeField] private TMP_Text _count;
        [SerializeField] private Image _highlight;
        [SerializeField] private Button _button;
        [SerializeField] private Sprite _emptySprite;

        public SlotCategory Category => _category;
        public byte Index => _index;
        public event Action<SlotCategory, byte> Clicked;

        private void Awake()
        {
            if (_button != null) _button.onClick.AddListener(OnButtonClicked);
        }

        private void OnDestroy()
        {
            if (_button != null) _button.onClick.RemoveListener(OnButtonClicked);
        }

        private void OnButtonClicked() => Clicked?.Invoke(_category, _index);

        /// <summary>Отрисовать слот занятым: иконка по каталогу + счётчик стека (если &gt;1).</summary>
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

        /// <summary>Отрисовать слот пустым.</summary>
        public void SetEmpty()
        {
            if (_icon != null)
            {
                _icon.sprite = _emptySprite;
                _icon.enabled = _emptySprite != null;
            }
            if (_count != null) _count.gameObject.SetActive(false);
        }

        /// <summary>Подсветка активной руки.</summary>
        public void SetHighlight(bool on)
        {
            if (_highlight != null) _highlight.enabled = on;
        }
    }
}
