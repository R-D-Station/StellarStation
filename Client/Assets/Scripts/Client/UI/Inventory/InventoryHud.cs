using System.Collections.Generic;
using UnityEngine;
using Shared.Messages.Interaction;
using Shared.World.Items;
using Client.Items;
using Client.Net;

namespace Client.UI.Inventory
{
    /// <summary>Тонкий менеджер слотов инвентаря: применяет <see cref="InventorySync"/> к <see cref="InventorySlotHud"/>-компонентам, роутит клики в drop.</summary>
    public sealed class InventoryHud : MonoBehaviour
    {
        [SerializeField] private InventorySlotHud[] _slots;
        [SerializeField] private ItemCatalog _catalog;
        [SerializeField] private NetworkRunner _runner;

        private readonly Dictionary<int, InventorySlotHud> _byKey = new Dictionary<int, InventorySlotHud>();
        private ushort _pullOverlayDefId;

        private static int Key(SlotCategory cat, byte index) => ((int)cat << 8) | index;

        private InventorySlotHud HandSlot(byte idx) => _byKey.TryGetValue(Key(SlotCategory.Hand, idx), out var s) ? s : null;

        public void SetPullOverlay(ushort itemDefId)
        {
            _pullOverlayDefId = itemDefId;
            ApplyPullOverlay();
        }

        public void ClearPullOverlay()
        {
            _pullOverlayDefId = 0;
            var h0 = HandSlot(0); if (h0 != null) h0.SetEmpty();
            var h1 = HandSlot(1); if (h1 != null) h1.SetEmpty();
        }

        private void ApplyPullOverlay()
        {
            if (_pullOverlayDefId == 0) return;
            var h0 = HandSlot(0); if (h0 != null) h0.SetFilled(_pullOverlayDefId, 1, _catalog);
            var h1 = HandSlot(1); if (h1 != null) h1.SetFilled(_pullOverlayDefId, 1, _catalog);
        }

        private void Awake()
        {
            if (_slots == null) return;
            for (int i = 0; i < _slots.Length; i++)
            {
                var slot = _slots[i];
                if (slot == null) continue;
                slot.Clicked += OnSlotClicked;
                _byKey[Key(slot.Category, slot.Index)] = slot;
            }
        }

        private void OnDestroy()
        {
            if (_slots == null) return;
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i] != null) _slots[i].Clicked -= OnSlotClicked;
        }

        /// <summary>Полный ре-рендер по слепку: всё пустое → занятые слоты из sync → подсветка ActiveHand.</summary>
        public void Apply(in InventorySync sync)
        {
            if (_slots != null)
                for (int i = 0; i < _slots.Length; i++)
                    if (_slots[i] != null) _slots[i].SetEmpty();

            var slots = sync.Slots;
            if (slots != null)
                for (int i = 0; i < slots.Length; i++)
                {
                    var rec = slots[i];
                    if (_byKey.TryGetValue(Key(rec.Category, rec.Index), out var comp) && comp != null)
                        comp.SetFilled(rec.ItemDefId, rec.StackCount, _catalog);
                }

            if (_slots != null)
                for (int i = 0; i < _slots.Length; i++)
                {
                    var slot = _slots[i];
                    if (slot != null) slot.SetHighlight(slot.Category == SlotCategory.Hand && slot.Index == sync.ActiveHand);
                }

            ApplyPullOverlay();
        }

        // ЛКМ по слоту: рука → выброс на пол, worn-слот → снять в активную руку.
        private void OnSlotClicked(SlotCategory cat, byte index)
        {
            if (_runner == null) return;
            if (cat == SlotCategory.Hand) _runner.SendDrop(cat, index);
            else _runner.Unequip(cat, index);
        }
    }
}
