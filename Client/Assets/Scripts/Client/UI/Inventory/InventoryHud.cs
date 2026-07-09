using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Shared.Messages.Interaction;
using Shared.World.Items;
using Client.Items;
using Client.Net;

namespace Client.UI.Inventory
{
    /// <summary>6-слотовый HUD инвентаря (uGUI): отражает серверный InventorySync 1:1, без предсказания/локального стейта.</summary>
    public sealed class InventoryHud : MonoBehaviour
    {
        [Tooltip("Иконки слотов, порядок = InventorySlot (0=ЛевРука..5=Спина).")]
        [SerializeField] private Image[] _icons = new Image[InventorySlot.SlotCount];
        [Tooltip("Счётчики стака слотов, тот же порядок.")]
        [SerializeField] private TMP_Text[] _counts = new TMP_Text[InventorySlot.SlotCount];
        [Tooltip("Рамки-подсветки слотов, тот же порядок (обычно горит только активная рука).")]
        [SerializeField] private Image[] _highlights = new Image[InventorySlot.SlotCount];
        [Tooltip("Кнопки слотов (минимум — drop по клику), тот же порядок.")]
        [SerializeField] private Button[] _buttons = new Button[InventorySlot.SlotCount];

        [Tooltip("Каталог предметов: спрайт по ItemDefId.")]
        [SerializeField] private ItemCatalog _catalog;
        [Tooltip("Спрайт пустого слота.")]
        [SerializeField] private Sprite _emptySlot;
        [Tooltip("Раннер — источник SendDrop по клику кнопки слота.")]
        [SerializeField] private NetworkRunner _runner;

        private void Awake()
        {
            // Слушатели фиксированы на индекс слота (замыкание по byte-значению, не по переменной цикла) — без per-click аллокаций дальше.
            for (int i = 0; i < _buttons.Length; i++)
            {
                if (_buttons[i] == null) continue;
                byte slot = (byte)i;
                _buttons[i].onClick.AddListener(() => OnSlotClicked(slot));
            }
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _buttons.Length; i++)
                if (_buttons[i] != null) _buttons[i].onClick.RemoveAllListeners();
        }

        private void OnSlotClicked(byte slot)
        {
            // Минимум по плану: клик по слоту = drop. MoveSlot по клику — не реализован (опционально).
            if (_runner != null) _runner.SendDrop(slot);
        }

        /// <summary>Применить полный слепок инвентаря: сброс всех 6 слотов в пусто, заполнение занятых, подсветка активной руки.</summary>
        public void Apply(in InventorySync sync)
        {
            for (int i = 0; i < InventorySlot.SlotCount; i++)
                ClearSlot(i);

            var slots = sync.Slots;
            if (slots != null)
            {
                for (int i = 0; i < slots.Length; i++)
                    FillSlot(in slots[i]);
            }

            // Подсветка активной руки — функция ТОЛЬКО от sync.ActiveHand, независимо от занятости слота (иначе
            // при пустой активной руке FillSlot по ней не вызывается и подсветка остаётся выключенной после ClearSlot).
            for (int i = 0; i < InventorySlot.SlotCount; i++)
                if (_highlights != null && i < _highlights.Length && _highlights[i] != null)
                    _highlights[i].enabled = (i == sync.ActiveHand);
        }

        // Раздельные bounds-проверки на каждый массив — инспектор может назначить их неполными/несогласованными по длине.
        private void ClearSlot(int slot)
        {
            if (_icons != null && slot >= 0 && slot < _icons.Length && _icons[slot] != null)
            {
                _icons[slot].sprite = _emptySlot;
                _icons[slot].enabled = _emptySlot != null;
            }
            if (_counts != null && slot >= 0 && slot < _counts.Length && _counts[slot] != null)
                _counts[slot].gameObject.SetActive(false);
            if (_highlights != null && slot >= 0 && slot < _highlights.Length && _highlights[slot] != null)
                _highlights[slot].enabled = false;
        }

        private void FillSlot(in SlotRecord rec)
        {
            int slot = rec.SlotIndex;
            var def = _catalog != null ? _catalog.For(rec.ItemDefId) : null;

            if (_icons != null && slot >= 0 && slot < _icons.Length && _icons[slot] != null)
            {
                Sprite s = def != null ? def.Sprite : null;
                _icons[slot].sprite = s;
                _icons[slot].enabled = s != null;
            }
            if (_counts != null && slot >= 0 && slot < _counts.Length && _counts[slot] != null)
            {
                bool show = rec.StackCount > 1;
                _counts[slot].gameObject.SetActive(show);
                if (show) _counts[slot].text = rec.StackCount.ToString();
            }
        }
    }
}
