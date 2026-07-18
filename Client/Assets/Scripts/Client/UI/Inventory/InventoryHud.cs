using UnityEngine;
using Shared.Messages.Interaction;
using Shared.World.Items;
using Client.Items;
using Client.Net;

namespace Client.UI.Inventory
{
    public sealed class InventoryHud : MonoBehaviour
    {
        [SerializeField] private InventorySlotHud[] _slots;
        [SerializeField] private ItemCatalog _catalog;
        [SerializeField] private NetworkRunner _runner;

        private readonly InventorySlotHud[] _byIndex = new InventorySlotHud[InventorySlot.SlotCount];

        private void Awake()
        {
            if (_slots == null) return;
            for (int i = 0; i < _slots.Length; i++)
            {
                var slot = _slots[i];
                if (slot == null) continue;
                slot.Clicked += OnSlotClicked;
                if (slot.Slot < _byIndex.Length) _byIndex[slot.Slot] = slot;
            }
        }

        private void OnDestroy()
        {
            if (_slots == null) return;
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i] != null) _slots[i].Clicked -= OnSlotClicked;
        }

        public void Apply(in InventorySync sync)
        {
            if (_slots != null)
                for (int i = 0; i < _slots.Length; i++)
                    if (_slots[i] != null) _slots[i].SetEmpty();

            var slots = sync.Slots;
            if (slots != null)
                for (int i = 0; i < slots.Length; i++)
                {
                    byte idx = slots[i].SlotIndex;
                    if (idx < _byIndex.Length && _byIndex[idx] != null)
                        _byIndex[idx].SetFilled(in slots[i], _catalog);
                }

            if (_slots != null)
                for (int i = 0; i < _slots.Length; i++)
                    if (_slots[i] != null) _slots[i].SetHighlight(_slots[i].Slot == sync.ActiveHand);
        }

        private void OnSlotClicked(byte slot)
        {
            if (_runner != null) _runner.SendDrop(slot);
        }
    }
}
